using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags <c>DateTime.Now</c>, <c>DateTime.Today</c> and <c>DateTimeOffset.Now</c>.
/// </summary>
/// <remarks>
/// Local time is a property of the machine the code happens to run on. Two servers in different
/// zones disagree, a value stored in spring is unreadable in autumn, and the hour that repeats at
/// the end of daylight saving time makes ordering by timestamp ambiguous — an hour of rows that
/// sort into each other. None of it shows up in a test suite that runs in one zone.
///
/// Store and compare in UTC and convert to local time at the point of display, where the reader's
/// zone is actually known. <c>DateTime.Today</c> is the same defect with the failure moved to
/// midnight: it is the machine's idea of the current date.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LocalTimestampAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0020";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Local-time clock reading",
        messageFormat: "'{0}.{1}' reads the machine's local time — use '{0}.{2}'",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "DateTime.Now, DateTime.Today and DateTimeOffset.Now depend on the time zone of whichever machine runs the code. Servers in different zones disagree, and the hour that repeats when daylight saving time ends makes stored timestamps ambiguous to order. Read UTC and convert to local time only where the value is displayed.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    /// <summary>Carries the replacement member name to the code fix so it is decided in one place.</summary>
    internal const string ReplacementPropertyKey = "Replacement";

    private const string DateTimeTypeName = "DateTime";
    private const string DateTimeOffsetTypeName = "DateTimeOffset";
    private const string SystemNamespace = "System";
    private const string NowMemberName = "Now";
    private const string TodayMemberName = "Today";
    private const string UtcNowMemberName = "UtcNow";
    private const string UtcNowDateMemberName = "UtcNow.Date";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)context.Node;
        string memberName = memberAccess.Name.Identifier.ValueText;
        if (memberName != NowMemberName && memberName != TodayMemberName)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol property
            || property.IsStatic == false)
        {
            return;
        }

        INamedTypeSymbol? containingType = property.ContainingType;
        if (containingType?.ContainingNamespace?.ToDisplayString() != SystemNamespace)
        {
            return;
        }

        string? replacement = GetReplacement(containingType.Name, memberName);
        if (replacement == null)
        {
            return;
        }

        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty
            .Add(ReplacementPropertyKey, replacement);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, memberAccess.Name.GetLocation(), properties, containingType.Name, memberName, replacement));
    }

    /// <summary>
    /// Returns what should stand in place of the member, or null for the combinations that do not
    /// exist — <c>DateTimeOffset</c> has no <c>Today</c>.
    /// </summary>
    private static string? GetReplacement(string typeName, string memberName)
    {
        if (typeName == DateTimeTypeName)
        {
            return memberName == NowMemberName ? UtcNowMemberName : UtcNowDateMemberName;
        }

        if (typeName == DateTimeOffsetTypeName && memberName == NowMemberName)
        {
            return UtcNowMemberName;
        }

        return null;
    }
}
