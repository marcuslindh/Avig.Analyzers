using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags a method whose (possibly Task/ValueTask-unwrapped) return type is nullable and whose
/// body returns null on some paths and a non-null value on others. That "value on success,
/// null on failure" shape uses null as an error signal, so callers can silently ignore the
/// failure the way a null check invites them to.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullableReturnAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0007";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Method returns null on failure — prefer Result<T>",
        messageFormat: "'{0}' returns null on some paths and a value on others — return a result type instead",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A method that returns a value when everything went fine and null otherwise is using null as an error signal. A result type (Result<T>, Option<T>, a discriminated union) makes the failure explicit in the signature instead of relying on every caller to remember a null check.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        MethodDeclarationSyntax methodDecl = (MethodDeclarationSyntax)context.Node;
        if (methodDecl.Body == null && methodDecl.ExpressionBody == null)
        {
            // Abstract/interface/partial-without-implementation — nothing to inspect.
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(methodDecl, context.CancellationToken) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        ITypeSymbol returnType = UnwrapTaskLike(methodSymbol.ReturnType);
        if (IsNullableType(returnType) == false || IsResultType(returnType) == true)
        {
            return;
        }

        List<ExpressionSyntax> leaves = new List<ExpressionSyntax>();
        if (methodDecl.ExpressionBody != null)
        {
            CollectReturnValueLeaves(methodDecl.ExpressionBody.Expression, leaves);
        }
        else if (methodDecl.Body != null)
        {
            foreach (ReturnStatementSyntax returnStatement in methodDecl.Body.DescendantNodes(DescendIntoSameMethodScope).OfType<ReturnStatementSyntax>())
            {
                if (returnStatement.Expression != null)
                {
                    CollectReturnValueLeaves(returnStatement.Expression, leaves);
                }
            }
        }

        bool hasNullReturn = leaves.Any(IsNullLikeLiteral);
        bool hasValueReturn = leaves.Any(leaf => IsNullLikeLiteral(leaf) == false);
        if (hasNullReturn == false || hasValueReturn == false)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, methodDecl.Identifier.GetLocation(), methodSymbol.Name));
    }

    /// <summary>Stops descent at nested lambdas/anonymous methods/local functions — their return statements belong to a different method scope.</summary>
    private static bool DescendIntoSameMethodScope(SyntaxNode node)
    {
        return node is SimpleLambdaExpressionSyntax == false
            && node is ParenthesizedLambdaExpressionSyntax == false
            && node is AnonymousMethodExpressionSyntax == false
            && node is LocalFunctionStatementSyntax == false;
    }

    /// <summary>Recurses into ternary/switch expressions so each arm is judged individually, e.g. <c>cond ? Find(id) : null</c>.</summary>
    private static void CollectReturnValueLeaves(ExpressionSyntax expression, List<ExpressionSyntax> leaves)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                CollectReturnValueLeaves(parenthesized.Expression, leaves);
                break;
            case ConditionalExpressionSyntax conditional:
                CollectReturnValueLeaves(conditional.WhenTrue, leaves);
                CollectReturnValueLeaves(conditional.WhenFalse, leaves);
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (SwitchExpressionArmSyntax arm in switchExpression.Arms)
                {
                    CollectReturnValueLeaves(arm.Expression, leaves);
                }
                break;
            default:
                leaves.Add(expression);
                break;
        }
    }

    private static bool IsNullLikeLiteral(ExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.NullLiteralExpression) == true
            || expression.IsKind(SyntaxKind.DefaultLiteralExpression) == true;
    }

    private static ITypeSymbol UnwrapTaskLike(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
            && (named.Name == "Task" || named.Name == "ValueTask")
            && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
        {
            return named.TypeArguments[0];
        }

        return type;
    }

    private static bool IsNullableType(ITypeSymbol type)
    {
        if (type.IsReferenceType == true)
        {
            return type.NullableAnnotation == NullableAnnotation.Annotated;
        }

        return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    /// <summary>
    /// True for a type named Result/Result&lt;T&gt; or Option/Option&lt;T&gt;, regardless of namespace.
    /// A method that already returns a result type is doing the thing this rule asks for; the
    /// check is name-only because the rule has no opinion about whose Result it is.
    /// </summary>
    private static bool IsResultType(ITypeSymbol type)
    {
        return type.Name == "Result" || type.Name == "Option";
    }
}
