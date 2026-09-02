using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Requires a blank line before a 'try' statement that follows another statement in the same
/// block — makes the start of a try/catch/finally construct visually obvious. Paired with
/// <see cref="BlankLineAfterClosingBraceAnalyzer"/>, which (as of its try/catch/finally
/// refinement) requires a blank line after the construct's closing brace too.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineBeforeTryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0004";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Insert a blank line before this try statement",
        messageFormat: "Insert a blank line before this try statement",
        category: "Formatting",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A 'try' statement that follows other code in the same block must be preceded by a blank line, so the try/catch/finally construct reads as visually distinct.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeTry, SyntaxKind.TryStatement);
    }

    private static void AnalyzeTry(SyntaxNodeAnalysisContext context)
    {
        TryStatementSyntax node = (TryStatementSyntax)context.Node;

        if (node.Parent is not BlockSyntax block)
        {
            return;
        }

        int index = block.Statements.IndexOf(node);
        if (index <= 0)
        {
            // First (or only) statement in the block — nothing precedes it to separate from.
            return;
        }

        if (BlankLineAfterClosingBraceAnalyzer.HasBlankLineBefore(node) == false)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.TryKeyword.GetLocation()));
        }
    }
}
