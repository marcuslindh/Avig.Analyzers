using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Requires a blank line after every '}' before the next statement or member, except when the
/// '}' closes a try block followed by its own catch/finally, or a catch block followed by
/// another catch/finally — i.e. a mid-construct brace inside one try/catch/finally statement.
/// The brace closing the WHOLE construct (the last catch with no finally, or any finally block)
/// is not excluded, so the try/catch/finally block reads as visually separated from what follows.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineAfterClosingBraceAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0001";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Insert a blank line after this closing brace",
        messageFormat: "Insert a blank line after this closing brace",
        category: "Formatting",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every '}' must be followed by a blank line before the next statement or member, except mid-construct try/catch/finally braces (the brace closing the whole construct still needs one).",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);
        foreach (SyntaxToken token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.CloseBraceToken) == false)
            {
                continue;
            }

            SyntaxNode? nextSibling = FindNextSiblingRequiringBlankLine(token);
            if (nextSibling == null)
            {
                continue;
            }

            if (HasBlankLineBefore(nextSibling) == false)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation()));
            }
        }
    }

    /// <summary>
    /// Climbs from a '}' token to the nearest enclosing statement/member whose last token it is
    /// (allowing a single trailing ';'), then returns the next sibling in that container's
    /// statement/member list — or null if the brace is excluded, nested inside a larger
    /// expression, followed by a continuation clause (else/catch/finally/do-while), or is the
    /// last entry in its list.
    /// </summary>
    internal static SyntaxNode? FindNextSiblingRequiringBlankLine(SyntaxToken closeBraceToken)
    {
        if (closeBraceToken.IsKind(SyntaxKind.CloseBraceToken) == false)
        {
            return null;
        }

        if (IsExcludedByTryCatchFinally(closeBraceToken) == true)
        {
            return null;
        }

        SyntaxNode? current = closeBraceToken.Parent;

        while (current != null)
        {
            SyntaxToken lastReal = current.GetLastToken();
            bool matches = lastReal == closeBraceToken
                || (lastReal.IsKind(SyntaxKind.SemicolonToken) == true && lastReal.GetPreviousToken() == closeBraceToken);
            if (matches == false)
            {
                return null;
            }

            if (TryGetNextSiblingInRelevantList(current, out SyntaxNode? nextSibling) == true)
            {
                return nextSibling;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsExcludedByTryCatchFinally(SyntaxToken closeBraceToken)
    {
        if (closeBraceToken.Parent is not BlockSyntax block)
        {
            return false;
        }

        // The try{} block's own brace: always excluded — a valid try always has at least one
        // catch or a finally right after it, and no blank line is wanted there.
        if (block.Parent is TryStatementSyntax)
        {
            return true;
        }

        // A catch{} block's brace: excluded only when another catch or a finally follows it —
        // i.e. this isn't the last block of the construct yet.
        if (block.Parent is CatchClauseSyntax catchClause && catchClause.Parent is TryStatementSyntax outerTry)
        {
            int index = outerTry.Catches.IndexOf(catchClause);
            bool hasLaterCatch = index >= 0 && index < outerTry.Catches.Count - 1;

            return hasLaterCatch || outerTry.Finally != null;
        }

        // A finally{} block's brace is always the true end of the construct — never excluded.
        return false;
    }

    private static bool TryGetNextSiblingInRelevantList(SyntaxNode node, out SyntaxNode? nextSibling)
    {
        switch (node.Parent)
        {
            case BlockSyntax block:
                nextSibling = NextOrNull(block.Statements, node);
                return true;
            case SwitchSectionSyntax section:
                nextSibling = NextOrNull(section.Statements, node);
                return true;
            case CompilationUnitSyntax unit:
                nextSibling = NextOrNull(unit.Members, node);
                return true;
            case NamespaceDeclarationSyntax ns:
                nextSibling = NextOrNull(ns.Members, node);
                return true;
            case FileScopedNamespaceDeclarationSyntax fileNs:
                nextSibling = NextOrNull(fileNs.Members, node);
                return true;
            case TypeDeclarationSyntax type:
                nextSibling = NextOrNull(type.Members, node);
                return true;
            case AccessorListSyntax accessorList:
                nextSibling = NextOrNull(accessorList.Accessors, node);
                return true;
            default:
                nextSibling = null;
                return false;
        }
    }

    private static SyntaxNode? NextOrNull<T>(SyntaxList<T> list, SyntaxNode node) where T : SyntaxNode
    {
        int index = list.IndexOf((T)node);
        if (index < 0 || index >= list.Count - 1)
        {
            return null;
        }

        return list[index + 1];
    }

    /// <summary>
    /// True when the line immediately following the previous token's own line is entirely blank
    /// (whitespace-only or empty), i.e. <paramref name="node"/>'s leading trivia starts with an
    /// end-of-line trivia (optionally preceded by whitespace-only trivia on that same first line).
    /// Preprocessor directives (<c>#pragma</c>, <c>#region</c>, etc.) are skipped first: a
    /// directive trivia node absorbs its OWN trailing newline internally — unlike a comment,
    /// it never surfaces as a separate sibling <see cref="SyntaxKind.EndOfLineTrivia"/> — so
    /// without this, a "#pragma warning restore" line sitting directly before an otherwise
    /// correctly blank-line-separated statement made this method report false negatives
    /// regardless of how many real blank lines followed it.
    /// </summary>
    internal static bool HasBlankLineBefore(SyntaxNode node)
    {
        SyntaxTriviaList leadingTrivia = node.GetFirstToken(includeZeroWidth: true).LeadingTrivia;
        int index = 0;

        while (index < leadingTrivia.Count
            && (leadingTrivia[index].IsKind(SyntaxKind.WhitespaceTrivia) == true || leadingTrivia[index].IsDirective == true))
        {
            index++;
        }

        return index < leadingTrivia.Count && leadingTrivia[index].IsKind(SyntaxKind.EndOfLineTrivia) == true;
    }
}
