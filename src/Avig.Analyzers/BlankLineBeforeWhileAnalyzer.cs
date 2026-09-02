using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Requires a blank line before a 'while' loop that follows another statement in the same
/// block. Only matches an actual while-loop (<see cref="SyntaxKind.WhileStatement"/>) — the
/// trailing 'while (...)' of a do-loop is a different node (<see cref="SyntaxKind.DoStatement"/>)
/// and is never matched here, since it directly follows the do-block's own closing brace.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineBeforeWhileAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0005";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Insert a blank line before this while loop",
        messageFormat: "Insert a blank line before this while loop",
        category: "Formatting",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A 'while' loop that follows other code in the same block must be preceded by a blank line.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeWhile, SyntaxKind.WhileStatement);
    }

    private static void AnalyzeWhile(SyntaxNodeAnalysisContext context)
    {
        WhileStatementSyntax node = (WhileStatementSyntax)context.Node;

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
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.WhileKeyword.GetLocation()));
        }
    }
}
