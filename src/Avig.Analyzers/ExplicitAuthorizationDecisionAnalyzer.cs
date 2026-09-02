using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags a controller action that carries neither <c>[Authorize]</c> nor
/// <c>[AllowAnonymous]</c>, on a controller whose class does not carry one either.
/// </summary>
/// <remarks>
/// Aimed at a specific failure: an endpoint reaches production unprotected because nobody
/// decided, not because someone decided to open it. An action with no attribute looks exactly
/// like an action someone deliberately opened, and the difference only shows up when somebody
/// goes looking.
///
/// The rule does not ask for authorization. It asks for the decision to be visible at the
/// signature, so that "anonymous" is something a reviewer can see rather than something they
/// have to infer from absence.
///
/// Custom auth counts. Any attribute implementing <c>IAuthorizationFilter</c> or
/// <c>IAsyncAuthorizationFilter</c> satisfies the rule, so a project's own auth attributes
/// work without configuration. Detecting the interface rather than a list of names is what
/// makes the rule portable between repositories.
///
/// Additional attribute names can be accepted through .editorconfig:
///
///     dotnet_code_quality.AVIG0012.additional_auth_attributes = HmacAuthenticated,InternalOnly
///
/// Authorization performed inside the method body is invisible here and will still be reported.
/// That is deliberate: a guard that only exists in the body does not protect the next action
/// someone adds to the same controller.
///
/// <b>The remediation is not always [AllowAnonymous].</b> A host that registers a global
/// <c>AuthorizeFilter</c> makes an unattributed action <em>closed</em>, and adding
/// [AllowAnonymous] would open it. A host without one leaves it genuinely reachable. The rule
/// reports both because the reader cannot tell them apart — which is the whole complaint — but
/// the fix has to be chosen per host.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExplicitAuthorizationDecisionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic id.</summary>
    public const string DiagnosticId = "AVIG0012";

    private const string AdditionalAttributesOption = "dotnet_code_quality.AVIG0012.additional_auth_attributes";

    private static readonly char[] Separators = { ',' };

    /// <summary>The rule descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Controller action has no explicit authorization decision",
        messageFormat: "'{0}' has neither [Authorize] nor [AllowAnonymous] — whether it is reachable anonymously depends on host configuration a reader cannot see here",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Without an attribute, whether the action requires authentication is decided somewhere else entirely: a host with a global AuthorizeFilter closes it, a host without one leaves it open. Neither is visible where the endpoint is written, so the same code means opposite things in two projects. Add [Authorize] or [AllowAnonymous] to say which one is intended. Note that the correct remediation differs: on a host with a global filter, adding [AllowAnonymous] opens an endpoint that is currently closed.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;

        if (method.Modifiers.Any(SyntaxKind.PublicKeyword) == false)
        {
            return;
        }

        if (method.Parent is not ClassDeclarationSyntax)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is not IMethodSymbol methodSymbol)
        {
            return;
        }

        if (IsControllerAction(methodSymbol) == false)
        {
            return;
        }

        ImmutableArray<string> extra = ReadAdditionalAttributes(context, method);

        if (HasDecision(methodSymbol.GetAttributes(), extra) == true)
        {
            return;
        }

        if (methodSymbol.ContainingType != null && HasDecision(methodSymbol.ContainingType.GetAttributes(), extra) == true)
        {
            return;
        }

        // A base controller may carry the attribute for everything that derives from it.
        INamedTypeSymbol? baseType = methodSymbol.ContainingType?.BaseType;
        while (baseType != null)
        {
            if (HasDecision(baseType.GetAttributes(), extra) == true)
            {
                return;
            }

            baseType = baseType.BaseType;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            method.Identifier.GetLocation(),
            methodSymbol.Name));
    }

    /// <summary>True when the method is reachable as an MVC/API endpoint.</summary>
    private static bool IsControllerAction(IMethodSymbol method)
    {
        if (method.IsStatic == true || method.MethodKind != MethodKind.Ordinary)
        {
            return false;
        }

        bool hasHttpVerb = method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name is
                "HttpGetAttribute" or "HttpPostAttribute" or "HttpPutAttribute"
                or "HttpDeleteAttribute" or "HttpPatchAttribute" or "HttpHeadAttribute"
                or "HttpOptionsAttribute" or "RouteAttribute");

        if (hasHttpVerb == false)
        {
            return false;
        }

        // Only inside something that actually is a controller — a plain class with a
        // [Route]-looking attribute should not drag the rule in.
        INamedTypeSymbol? type = method.ContainingType;
        while (type != null)
        {
            if (type.Name is "Controller" or "ControllerBase")
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    /// <summary>True when the attribute set contains an explicit authorization decision.</summary>
    private static bool HasDecision(ImmutableArray<AttributeData> attributes, ImmutableArray<string> extra)
    {
        foreach (AttributeData attribute in attributes)
        {
            INamedTypeSymbol? attributeClass = attribute.AttributeClass;
            if (attributeClass == null)
            {
                continue;
            }

            string name = attributeClass.Name;
            if (name is "AuthorizeAttribute" or "AllowAnonymousAttribute")
            {
                return true;
            }

            if (extra.Contains(name) == true || extra.Contains(TrimAttributeSuffix(name)) == true)
            {
                return true;
            }

            // Custom auth: anything that filters authorization is a decision.
            foreach (INamedTypeSymbol implemented in attributeClass.AllInterfaces)
            {
                if (implemented.Name is "IAuthorizationFilter" or "IAsyncAuthorizationFilter")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string TrimAttributeSuffix(string name)
    {
        return name.EndsWith("Attribute", System.StringComparison.Ordinal) == true
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;
    }

    private static ImmutableArray<string> ReadAdditionalAttributes(SyntaxNodeAnalysisContext context, SyntaxNode node)
    {
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(node.SyntaxTree);
        if (options.TryGetValue(AdditionalAttributesOption, out string? configured) == false
            || string.IsNullOrWhiteSpace(configured) == true)
        {
            return ImmutableArray<string>.Empty;
        }

        return configured
            .Split(Separators, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .ToImmutableArray();
    }
}
