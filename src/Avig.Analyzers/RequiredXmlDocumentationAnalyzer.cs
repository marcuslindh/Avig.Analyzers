using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags a public controller or <c>I*Repository</c> interface that has no XML &lt;summary&gt;.
/// These are the two categories whose contract is somebody else's problem, so they are the two
/// worth requiring documentation for. Additional marker interfaces can be named in .editorconfig:
///
///     dotnet_code_quality.AVIG0009.additional_documented_interfaces = IAppCommand,IAppQuery
/// </summary>
/// <remarks>
/// CS1591 cannot cover this without switching on GenerateDocumentationFile, which would demand
/// docs on every public member including every DTO property. The check reads the leading trivia
/// rather than GetDocumentationCommentXml() so it works regardless of the compilation's
/// DocumentationMode.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequiredXmlDocumentationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0009";

    private const string SummaryElement = "<summary>";

    private const string AdditionalInterfacesOption = "dotnet_code_quality.AVIG0009.additional_documented_interfaces";

    private static readonly char[] Separators = { ',' };

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Public controller or repository interface needs an XML summary",
        messageFormat: "{0} '{1}' has no XML <summary>",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Repository interfaces and controllers document the contract callers depend on, and that contract is read far more often than it is written. Additional marker interfaces can be required through the dotnet_code_quality.AVIG0009.additional_documented_interfaces option.",
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
        if (type.DeclaredAccessibility != Accessibility.Public)
        {
            return;
        }

        SyntaxNode? first = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken);
        if (first == null)
        {
            return;
        }

        // The option is per-file, and RegisterSymbolAction hands us no tree — take it from the
        // declaration we have to look up anyway.
        ImmutableArray<string> extra = ReadAdditionalInterfaces(context, first.SyntaxTree);

        string? category = DescribeCategory(type, extra);
        if (category == null)
        {
            return;
        }

        foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(context.CancellationToken) is not BaseTypeDeclarationSyntax declaration)
            {
                continue;
            }

            if (HasSummary(declaration) == true)
            {
                return;
            }
        }

        Location location = first is BaseTypeDeclarationSyntax typeDecl
            ? typeDecl.Identifier.GetLocation()
            : type.Locations.FirstOrDefault() ?? Location.None;

        context.ReportDiagnostic(Diagnostic.Create(Rule, location, category, type.Name));
    }

    /// <summary>
    /// Returns a human-readable category name for the documented kinds, or null when the rule
    /// does not apply.
    /// </summary>
    private static string? DescribeCategory(INamedTypeSymbol type, ImmutableArray<string> additionalInterfaces)
    {
        if (IsController(type) == true)
        {
            return "Controller";
        }

        if (type.TypeKind == TypeKind.Interface
            && IsRepositoryInterfaceName(type.Name) == true)
        {
            return "Repository interface";
        }

        if (additionalInterfaces.IsEmpty == false)
        {
            foreach (INamedTypeSymbol iface in type.AllInterfaces)
            {
                if (additionalInterfaces.Contains(iface.Name) == true)
                {
                    return "Type";
                }
            }
        }

        return null;
    }

    /// <summary>Matches the repository naming convention: an interface named <c>I…Repository</c>.</summary>
    private static bool IsRepositoryInterfaceName(string name)
    {
        return name.Length > "IRepository".Length
            && name[0] == 'I'
            && name.EndsWith("Repository", StringComparison.Ordinal) == true;
    }

    private static ImmutableArray<string> ReadAdditionalInterfaces(SymbolAnalysisContext context, SyntaxTree tree)
    {
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        if (options.TryGetValue(AdditionalInterfacesOption, out string? configured) == false
            || string.IsNullOrWhiteSpace(configured) == true)
        {
            return ImmutableArray<string>.Empty;
        }

        return configured
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .ToImmutableArray();
    }

    private static bool IsController(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.Name == "Controller"
                || current.Name == "ControllerBase")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the declaration carries a doc comment containing &lt;summary&gt;. Reads raw trivia
    /// so it also works when DocumentationMode leaves /// lexed as an ordinary comment.
    /// </summary>
    private static bool HasSummary(BaseTypeDeclarationSyntax declaration)
    {
        foreach (SyntaxTrivia trivia in declaration.GetLeadingTrivia())
        {
            if (trivia.ToFullString().IndexOf(SummaryElement, StringComparison.Ordinal) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
