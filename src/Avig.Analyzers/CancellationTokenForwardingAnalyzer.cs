using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags an awaited call that drops a CancellationToken which is available in the enclosing
/// method — either by omitting an optional token parameter or by passing
/// <c>CancellationToken.None</c> or <c>default</c>.
/// </summary>
/// <remarks>
/// AVIG0008 requires the token to be in the signature; this rule requires it to actually go
/// somewhere. A token that is accepted and then not forwarded is worse than no token at all: the
/// signature promises cancellation, callers write code that depends on it, and the work runs to
/// completion anyway.
///
/// Deliberately narrower than CA2016, which fires at every call site with a token overload. This
/// rule only looks at awaited calls, where the cost of running past a cancellation is actually
/// paid, and only when a token is in scope to forward. <c>ConfigureAwait</c> and
/// <c>WithCancellation</c> are unwrapped on the way in.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationTokenForwardingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0015";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Awaited call drops an available CancellationToken",
        messageFormat: "Awaited call to '{0}' does not receive the CancellationToken '{1}' available here — forward it",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A method that accepts a CancellationToken and then awaits work without passing it on cannot be cancelled, while its signature says it can. Callers build on that promise and the work keeps running after the request is abandoned. Forward the token to every awaited call that accepts one, including calls where the token parameter is optional and calls currently passing CancellationToken.None or default.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    private const string ConfigureAwaitMethodName = "ConfigureAwait";
    private const string WithCancellationMethodName = "WithCancellation";
    private const string CancellationTokenNoneName = "None";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAwait, SyntaxKind.AwaitExpression);
    }

    private static void AnalyzeAwait(SyntaxNodeAnalysisContext context)
    {
        AwaitExpressionSyntax awaitExpression = (AwaitExpressionSyntax)context.Node;

        IParameterSymbol? availableToken = FindTokenInScope(context.SemanticModel, awaitExpression, context.CancellationToken);
        if (availableToken == null)
        {
            return;
        }

        InvocationExpressionSyntax? invocation = UnwrapAwaitedInvocation(awaitExpression.Expression);
        if (invocation == null)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol target)
        {
            return;
        }

        if (ReceivesRealToken(context.SemanticModel, invocation, context.CancellationToken) == true)
        {
            return;
        }

        if (AcceptsCancellationToken(target) == false && HasCancellableOverload(target) == false)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, GetInvocationLocation(invocation), target.Name, availableToken.Name));
    }

    /// <summary>
    /// Peels <c>ConfigureAwait</c> and <c>WithCancellation</c> off the awaited expression so the
    /// call that actually does the work is the one examined.
    /// </summary>
    private static InvocationExpressionSyntax? UnwrapAwaitedInvocation(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
                continue;
            }

            if (expression is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && (memberAccess.Name.Identifier.ValueText == ConfigureAwaitMethodName
                    || memberAccess.Name.Identifier.ValueText == WithCancellationMethodName))
            {
                expression = memberAccess.Expression;
                continue;
            }

            return expression as InvocationExpressionSyntax;
        }
    }

    /// <summary>
    /// Walks outward from the await through lambdas and local functions to the method, returning
    /// the first CancellationToken parameter that could be forwarded.
    /// </summary>
    private static IParameterSymbol? FindTokenInScope(SemanticModel semanticModel, SyntaxNode node, System.Threading.CancellationToken cancellationToken)
    {
        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            ISymbol? owner = ancestor switch
            {
                MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method, cancellationToken),
                LocalFunctionStatementSyntax localFunction => semanticModel.GetDeclaredSymbol(localFunction, cancellationToken),
                AnonymousFunctionExpressionSyntax anonymous => semanticModel.GetSymbolInfo(anonymous, cancellationToken).Symbol,
                _ => null,
            };

            if (owner is IMethodSymbol enclosingMethod)
            {
                IParameterSymbol? token = enclosingMethod.Parameters.FirstOrDefault(parameter => AvigSymbols.IsCancellationToken(parameter.Type));
                if (token != null)
                {
                    return token;
                }
            }

            if (ancestor is MethodDeclarationSyntax)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// True when an argument binds to a CancellationToken parameter and is not one of the no-op
    /// values. <c>CancellationToken.None</c> and <c>default</c> satisfy the compiler while leaving
    /// the call exactly as uncancellable as omitting the argument would.
    /// </summary>
    private static bool ReceivesRealToken(SemanticModel semanticModel, InvocationExpressionSyntax invocation, System.Threading.CancellationToken cancellationToken)
    {
        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            TypeInfo typeInfo = semanticModel.GetTypeInfo(argument.Expression, cancellationToken);
            if (AvigSymbols.IsCancellationToken(typeInfo.ConvertedType) == false)
            {
                continue;
            }

            if (IsEmptyToken(semanticModel, argument.Expression, cancellationToken) == false)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmptyToken(SemanticModel semanticModel, ExpressionSyntax expression, System.Threading.CancellationToken cancellationToken)
    {
        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) == true || expression is DefaultExpressionSyntax)
        {
            return true;
        }

        return expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name.Identifier.ValueText == CancellationTokenNoneName
            && semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol property
            && AvigSymbols.IsCancellationToken(property.ContainingType);
    }

    private static bool AcceptsCancellationToken(IMethodSymbol method)
    {
        return method.Parameters.Any(parameter => AvigSymbols.IsCancellationToken(parameter.Type));
    }

    /// <summary>
    /// True when the containing type offers the same method with one extra trailing
    /// CancellationToken — the shape the BCL uses for ReadAsync, SaveChangesAsync and friends.
    /// Only an exact leading-parameter match counts, so unrelated overloads that merely share a
    /// name are left alone.
    /// </summary>
    private static bool HasCancellableOverload(IMethodSymbol method)
    {
        if (method.ContainingType == null)
        {
            return false;
        }

        IMethodSymbol definition = method.OriginalDefinition;

        foreach (IMethodSymbol candidate in method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>())
        {
            IMethodSymbol candidateDefinition = candidate.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(candidateDefinition, definition) == true)
            {
                continue;
            }

            if (candidateDefinition.Parameters.Length != definition.Parameters.Length + 1)
            {
                continue;
            }

            if (AvigSymbols.IsCancellationToken(candidateDefinition.Parameters[candidateDefinition.Parameters.Length - 1].Type) == false)
            {
                continue;
            }

            if (HasSameLeadingParameters(definition, candidateDefinition) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSameLeadingParameters(IMethodSymbol definition, IMethodSymbol candidate)
    {
        for (int index = 0; index < definition.Parameters.Length; index++)
        {
            // Display strings rather than symbol identity: on a generic method each overload
            // declares its own type parameters, so T from one never equals T from the other.
            if (definition.Parameters[index].Type.ToDisplayString() != candidate.Parameters[index].Type.ToDisplayString())
            {
                return false;
            }
        }

        return true;
    }

    private static Location GetInvocationLocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
            SimpleNameSyntax simpleName => simpleName.GetLocation(),
            _ => invocation.Expression.GetLocation(),
        };
    }
}
