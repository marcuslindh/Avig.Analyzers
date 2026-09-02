using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags an async method on an <c>I*Repository</c> interface that does not accept a
/// CancellationToken as its last parameter.
/// </summary>
/// <remarks>
/// A CancellationToken that is threaded through the call stack and then dropped at the
/// repository boundary buys nothing: a cancelled or abandoned request still runs its data
/// access to completion. Taking the token as the last parameter is the convention both the
/// BCL and EF Core follow, and it gives the "forward the CancellationToken" rules (CA2016,
/// MA0040) a target to enforce — without it they have no method to forward to.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepositoryCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0008";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Repository method must accept a CancellationToken",
        messageFormat: "'{0}.{1}' returns {2} without a CancellationToken — add one as the last parameter and forward it",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Async repository methods must take a CancellationToken as their last parameter so cancellation propagates all the way to the data store. Without it a cancelled or abandoned request keeps its work running, and the rules that forward cancellation tokens (CA2016, MA0040) have nothing to enforce.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Interface || IsRepositoryInterfaceName(type.Name) == false)
        {
            return;
        }

        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            if (AvigSymbols.IsTaskLike(method.ReturnType) == false)
            {
                continue;
            }

            if (HasTrailingCancellationToken(method) == true)
            {
                continue;
            }

            Location location = GetMethodLocation(method, context.CancellationToken);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, location, type.Name, method.Name, method.ReturnType.ToDisplayString()));
        }
    }

    /// <summary>Matches the repository naming convention: an interface named <c>I…Repository</c>.</summary>
    private static bool IsRepositoryInterfaceName(string name)
    {
        return name.Length > "IRepository".Length
            && name[0] == 'I'
            && name.EndsWith("Repository", System.StringComparison.Ordinal) == true;
    }

    private static bool HasTrailingCancellationToken(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
        {
            return false;
        }

        IParameterSymbol last = method.Parameters[method.Parameters.Length - 1];

        return AvigSymbols.IsCancellationToken(last.Type);
    }

    private static Location GetMethodLocation(IMethodSymbol method, System.Threading.CancellationToken cancellationToken)
    {
        SyntaxNode? syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (syntax is MethodDeclarationSyntax methodDecl)
        {
            return methodDecl.Identifier.GetLocation();
        }

        return method.Locations.FirstOrDefault() ?? Location.None;
    }
}
