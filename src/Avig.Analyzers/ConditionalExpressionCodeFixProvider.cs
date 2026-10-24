using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

/// <summary>
/// Rewrites a conditional expression into an if/else when it is the whole value of something
/// that can take a statement instead. Two families:
/// <list type="bullet">
/// <item>a statement in a block — <c>return c ? a : b;</c>, <c>yield return c ? a : b;</c>,
/// <c>x = c ? a : b;</c> and <c>T x = c ? a : b;</c>;</item>
/// <item>an expression body — a method, operator, local function, <c>get</c> accessor, property
/// or indexer written with <c>=&gt;</c>, or a lambda — which becomes a block body that returns
/// from each arm.</item>
/// </list>
/// A conditional in the false arm becomes an <c>else if</c>.
/// </summary>
/// <remarks>
/// A conditional inside an argument list or a field initializer has no fix, because moving it
/// out means inventing a local or a method the author should name themselves.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ConditionalExpressionCodeFixProvider))]
[Shared]
public sealed class ConditionalExpressionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(ConditionalExpressionAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ConditionalExpressionSyntax conditional)
        {
            return;
        }

        if (FindRewritableStatement(conditional) == null && FindBodyOwner(conditional) == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to if statement",
                createChangedDocument: cancellationToken => ConvertAsync(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                equivalenceKey: "ConvertConditionalToIf"),
            diagnostic);
    }

    private static async Task<Document> ConvertAsync(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root?.FindNode(diagnosticSpan, getInnermostNodeForTie: true) is not ConditionalExpressionSyntax conditional)
        {
            return document;
        }

        SyntaxNode? newRoot = null;
        StatementSyntax? statement = FindRewritableStatement(conditional);
        if (statement != null)
        {
            SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            newRoot = ConvertStatement(root, statement, conditional, model);
        }
        else
        {
            SyntaxNode? owner = FindBodyOwner(conditional);
            if (owner != null)
            {
                newRoot = ConvertBody(root, owner, conditional);
            }
        }

        if (newRoot == null)
        {
            return document;
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxNode? ConvertStatement(SyntaxNode root, StatementSyntax statement, ConditionalExpressionSyntax conditional, SemanticModel? model)
    {
        if (statement.Parent is not BlockSyntax block)
        {
            return null;
        }

        List<StatementSyntax>? replacements = null;
        if (statement is ReturnStatementSyntax)
        {
            replacements = [BuildIf(conditional, arm => SyntaxFactory.ReturnStatement(arm))];
        }
        else if (statement is YieldStatementSyntax)
        {
            replacements = [BuildIf(conditional, arm => SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement, arm))];
        }
        else if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
        {
            replacements = [BuildIf(conditional, arm => SyntaxFactory.ExpressionStatement(assignment.WithRight(arm).WithoutTrivia()))];
        }
        else if (statement is LocalDeclarationStatementSyntax local)
        {
            replacements = SplitDeclaration(local, conditional, model);
        }

        if (replacements == null)
        {
            return null;
        }

        replacements[0] = replacements[0].WithLeadingTrivia(statement.GetLeadingTrivia());
        int last = replacements.Count - 1;
        replacements[last] = replacements[last].WithTrailingTrivia(statement.GetTrailingTrivia());

        SyntaxList<StatementSyntax> statements = block.Statements;
        int index = statements.IndexOf(statement);
        statements = statements.RemoveAt(index).InsertRange(index, replacements);

        // The if ends in a brace, so the statement after it now needs the blank line AVIG0001
        // asks for. Without it the fix would trade one diagnostic for another.
        int nextIndex = index + replacements.Count;
        if (nextIndex < statements.Count)
        {
            StatementSyntax next = statements[nextIndex];
            statements = statements.Replace(next, WithBlankLineBefore(root, next));
        }

        return root.ReplaceNode(block, block.WithStatements(statements));
    }

    private static SyntaxNode? ConvertBody(SyntaxNode root, SyntaxNode owner, ConditionalExpressionSyntax conditional)
    {
        // The formatter keeps a block on one line if it already is on one, and a block built from
        // elastic trivia counts as one inside a lambda or a local function. Real line breaks
        // first; the formatter then only has to indent.
        string newLine = AvigNewLine.Detect(root, conditional.GetFirstToken());
        BlockSyntax body = SyntaxFactory.Block(BuildIf(conditional, arm => SyntaxFactory.ReturnStatement(arm)))
            .NormalizeWhitespace(eol: newLine)
            .WithAdditionalAnnotations(Formatter.Annotation);
        SyntaxNode? newOwner = WithBlockBody(owner, body);
        if (newOwner == null)
        {
            return null;
        }

        // A member or local function that ended in ';' now ends in '}', so whatever follows it
        // needs the blank line AVIG0001 asks for — the same reasoning as for a statement.
        SyntaxNode? next = FindNextSibling(owner);
        if (next == null)
        {
            return root.ReplaceNode(owner, newOwner);
        }

        return root.ReplaceNodes([owner, next], (original, rewritten) =>
        {
            if (original == owner)
            {
                return newOwner;
            }

            return WithBlankLineBefore(root, rewritten);
        });
    }

    /// <summary>
    /// The statement whose entire value is <paramref name="conditional"/> (through parentheses),
    /// in one of the four shapes the fix understands — or null.
    /// </summary>
    private static StatementSyntax? FindRewritableStatement(ConditionalExpressionSyntax conditional)
    {
        ExpressionSyntax outer = WalkUpParentheses(conditional);

        StatementSyntax? statement = null;
        if (outer.Parent is ReturnStatementSyntax returnStatement)
        {
            statement = returnStatement;
        }
        else if (outer.Parent is YieldStatementSyntax yieldStatement && yieldStatement.IsKind(SyntaxKind.YieldReturnStatement) == true)
        {
            statement = yieldStatement;
        }
        else if (outer.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == outer
            && assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) == false
            && IsSideEffectFree(assignment.Left) == true
            && assignment.Parent is ExpressionStatementSyntax expressionStatement)
        {
            statement = expressionStatement;
        }
        else if (outer.Parent is EqualsValueClauseSyntax
            && outer.Parent.Parent is VariableDeclaratorSyntax
            && outer.Parent.Parent.Parent is VariableDeclarationSyntax { Variables.Count: 1 }
            && outer.Parent.Parent.Parent.Parent is LocalDeclarationStatementSyntax local
            && local.IsConst == false
            && local.UsingKeyword.IsKind(SyntaxKind.None) == true
            && local.Declaration.Type is not RefTypeSyntax)
        {
            statement = local;
        }

        if (statement?.Parent is not BlockSyntax)
        {
            return null;
        }

        return statement;
    }

    /// <summary>
    /// The lambda or <c>=&gt;</c> member whose whole body is <paramref name="conditional"/>, or
    /// null. A body like that always produces a value — a conditional is not a statement
    /// expression, so it cannot be the body of something void — which is why every arm can
    /// simply return.
    /// </summary>
    private static SyntaxNode? FindBodyOwner(ConditionalExpressionSyntax conditional)
    {
        ExpressionSyntax outer = WalkUpParentheses(conditional);
        if (outer.Parent is LambdaExpressionSyntax lambda && lambda.ExpressionBody == outer)
        {
            return lambda;
        }

        if (outer.Parent is not ArrowExpressionClauseSyntax arrow)
        {
            return null;
        }

        SyntaxNode? owner = arrow.Parent;
        if (owner is BaseMethodDeclarationSyntax
            || owner is LocalFunctionStatementSyntax
            || owner is PropertyDeclarationSyntax
            || owner is IndexerDeclarationSyntax
            || owner.IsKind(SyntaxKind.GetAccessorDeclaration) == true)
        {
            return owner;
        }

        return null;
    }

    private static SyntaxNode? WithBlockBody(SyntaxNode owner, BlockSyntax body)
    {
        switch (owner)
        {
            case LambdaExpressionSyntax lambda:
                return lambda.WithExpressionBody(null).WithBlock(body);

            case BaseMethodDeclarationSyntax method:
                return method
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(body.WithTrailingTrivia(method.SemicolonToken.TrailingTrivia));

            case LocalFunctionStatementSyntax function:
                return function
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(body.WithTrailingTrivia(function.SemicolonToken.TrailingTrivia));

            case AccessorDeclarationSyntax accessor:
                return accessor
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithBody(body.WithTrailingTrivia(accessor.SemicolonToken.TrailingTrivia));

            case PropertyDeclarationSyntax property:
                return property
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithAccessorList(GetterOnly(body, property.SemicolonToken));

            case IndexerDeclarationSyntax indexer:
                return indexer
                    .WithExpressionBody(null)
                    .WithSemicolonToken(default)
                    .WithAccessorList(GetterOnly(body, indexer.SemicolonToken));

            default:
                return null;
        }
    }

    private static AccessorListSyntax GetterOnly(BlockSyntax body, SyntaxToken semicolon)
    {
        return SyntaxFactory.AccessorList(
                SyntaxFactory.SingletonList(SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, body)))
            .WithTrailingTrivia(semicolon.TrailingTrivia)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    /// <summary>
    /// The member or statement that follows <paramref name="node"/> in the same list, or null
    /// when nothing does or when the node is not in a list at all (a lambda, an accessor).
    /// </summary>
    private static SyntaxNode? FindNextSibling(SyntaxNode node)
    {
        if (node is not MemberDeclarationSyntax && node is not StatementSyntax)
        {
            return null;
        }

        bool found = false;
        foreach (SyntaxNode sibling in node.Parent!.ChildNodes())
        {
            if (found == true)
            {
                return sibling;
            }

            found = sibling == node;
        }

        return null;
    }

    private static TNode WithBlankLineBefore<TNode>(SyntaxNode root, TNode node)
        where TNode : SyntaxNode
    {
        SyntaxToken firstToken = node.GetFirstToken();
        if (StartsWithBlankLine(firstToken) == true)
        {
            return node;
        }

        string newLine = AvigNewLine.Detect(root, firstToken);

        return node.WithLeadingTrivia(node.GetLeadingTrivia().Insert(0, SyntaxFactory.EndOfLine(newLine)));
    }

    private static ExpressionSyntax WalkUpParentheses(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized;
        }

        return expression;
    }

    /// <summary>
    /// The left side is copied into both arms and evaluated after the condition instead of before
    /// it, so it may only be something whose evaluation cannot be observed: a name or a member
    /// chain of names.
    /// </summary>
    private static bool IsSideEffectFree(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax || expression is ThisExpressionSyntax)
        {
            return true;
        }

        if (expression is MemberAccessExpressionSyntax memberAccess && memberAccess.IsKind(SyntaxKind.SimpleMemberAccessExpression) == true)
        {
            return IsSideEffectFree(memberAccess.Expression);
        }

        return false;
    }

    private static List<StatementSyntax>? SplitDeclaration(LocalDeclarationStatementSyntax local, ConditionalExpressionSyntax conditional, SemanticModel? model)
    {
        VariableDeclaratorSyntax declarator = local.Declaration.Variables[0];
        TypeSyntax type = local.Declaration.Type;
        if (type.IsVar == true)
        {
            ITypeSymbol? inferred = model?.GetTypeInfo(conditional).ConvertedType;
            if (model == null || inferred == null || inferred.TypeKind == TypeKind.Error)
            {
                return null;
            }

            type = SyntaxFactory.ParseTypeName(inferred.ToMinimalDisplayString(model, local.SpanStart));
        }

        IdentifierNameSyntax name = SyntaxFactory.IdentifierName(declarator.Identifier.WithoutTrivia());
        LocalDeclarationStatementSyntax declaration = SyntaxFactory.LocalDeclarationStatement(
            SyntaxFactory.VariableDeclaration(
                type.WithoutTrivia(),
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name.Identifier))))
            .WithModifiers(local.Modifiers)
            .WithAdditionalAnnotations(Formatter.Annotation);

        IfStatementSyntax ifStatement = BuildIf(conditional, arm => SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, name, arm)));

        return [declaration, ifStatement];
    }

    private static IfStatementSyntax BuildIf(ConditionalExpressionSyntax conditional, Func<ExpressionSyntax, StatementSyntax> makeStatement)
    {
        ElseClauseSyntax elseClause;
        if (Unwrap(conditional.WhenFalse) is ConditionalExpressionSyntax nested)
        {
            elseClause = SyntaxFactory.ElseClause(BuildIf(nested, makeStatement));
        }
        else
        {
            elseClause = SyntaxFactory.ElseClause(BuildArm(conditional.WhenFalse, makeStatement));
        }

        return SyntaxFactory.IfStatement(Unwrap(conditional.Condition).WithoutTrivia(), BuildArm(conditional.WhenTrue, makeStatement))
            .WithElse(elseClause)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static BlockSyntax BuildArm(ExpressionSyntax arm, Func<ExpressionSyntax, StatementSyntax> makeStatement)
    {
        // "c ? a : throw x" is legal in an expression; "return throw x;" is not.
        StatementSyntax statement;
        if (Unwrap(arm) is ThrowExpressionSyntax throwExpression)
        {
            statement = SyntaxFactory.ThrowStatement(throwExpression.Expression.WithoutTrivia());
        }
        else
        {
            statement = makeStatement(Unwrap(arm).WithoutTrivia());
        }

        return SyntaxFactory.Block(statement);
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool StartsWithBlankLine(SyntaxToken token)
    {
        foreach (SyntaxTrivia trivia in token.LeadingTrivia)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia) == true)
            {
                return true;
            }

            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) == false)
            {
                return false;
            }
        }

        return false;
    }
}
