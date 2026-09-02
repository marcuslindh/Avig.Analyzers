using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags role, scope, policy and claim names written as string literals at the point of use —
/// <c>User.IsInRole("admin")</c>, <c>[Authorize(Policy = "ai.chat")]</c>,
/// <c>User.FindFirst("sub")</c>.
/// </summary>
/// <remarks>
/// This is the narrow, high-signal slice of the "no magic strings" rule: an auth literal that is
/// misspelled fails open or closed silently, and the same names are repeated across controllers,
/// attributes and policy registration with nothing tying them together. Lift them into a shared
/// constants class (ScopeNames/RoleNames/ClaimNames) so the compiler catches typos and rename
/// finds every use site. Broader magic-number analysis is deliberately out of scope.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuthMagicStringAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0010";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Auth role/scope/policy/claim name written as a literal",
        messageFormat: "Auth literal \"{0}\" used in '{1}' — move it to a named constant",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Role, scope, policy and claim names are security-relevant identifiers repeated across controllers, [Authorize] attributes and policy registration. Written inline, a typo silently changes who gets access and nothing links the use sites together. Declare them once as constants and reference those.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    private static readonly ImmutableHashSet<string> AuthMethodNames = ImmutableHashSet.Create(
        "IsInRole",
        "HasClaim",
        "FindFirst",
        "FindFirstValue",
        "FindAll",
        "RequireRole",
        "RequireClaim",
        "RequireScope",
        "HasScope");

    private static readonly ImmutableHashSet<string> AuthAttributeNames = ImmutableHashSet.Create(
        "Authorize",
        "AuthorizeAttribute");

    private static readonly ImmutableHashSet<string> AuthAttributeArguments = ImmutableHashSet.Create(
        "Policy",
        "Roles",
        "AuthenticationSchemes");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        string? methodName = GetInvokedName(invocation.Expression);
        if (methodName == null || AuthMethodNames.Contains(methodName) == false)
        {
            return;
        }

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            ReportIfStringLiteral(context, argument.Expression, methodName);
        }
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        AttributeSyntax attribute = (AttributeSyntax)context.Node;
        string? attributeName = GetInvokedName(attribute.Name);
        if (attributeName == null || AuthAttributeNames.Contains(attributeName) == false)
        {
            return;
        }

        if (attribute.ArgumentList == null)
        {
            return;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            string? target = argument.NameEquals?.Name.Identifier.ValueText;
            if (target == null || AuthAttributeArguments.Contains(target) == false)
            {
                continue;
            }

            ReportIfStringLiteral(context, argument.Expression, $"{attributeName}.{target}");
        }
    }

    private static void ReportIfStringLiteral(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, string usage)
    {
        if (expression is not LiteralExpressionSyntax literal || literal.IsKind(SyntaxKind.StringLiteralExpression) == false)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation(), literal.Token.ValueText, usage));
    }

    /// <summary>Returns the simple (unqualified) name of a method or attribute reference.</summary>
    private static string? GetInvokedName(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            _ => null,
        };
    }
}
