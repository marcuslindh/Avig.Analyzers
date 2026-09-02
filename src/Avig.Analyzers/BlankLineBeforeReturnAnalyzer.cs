using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Requires a blank line before a 'return' statement that follows another statement in the same
/// block — except when the 'return' sits directly inside an 'if' statement's body (braced or
/// not), since early-return guard clauses read fine without one.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineBeforeReturnAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0003";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Insert a blank line before this return statement",
        messageFormat: "Insert a blank line before this return statement",
        category: "Formatting",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A 'return' statement that follows other code in the same block must be preceded by a blank line, except when it sits directly inside an 'if' statement's body.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeReturn, SyntaxKind.ReturnStatement);
    }

    private static void AnalyzeReturn(SyntaxNodeAnalysisContext context)
    {
        ReturnStatementSyntax node = (ReturnStatementSyntax)context.Node;

        // Braceless "if (x) return;" has no BlockSyntax parent at all — nothing to check.
        if (node.Parent is not BlockSyntax block)
        {
            return;
        }

        // "if (x) { ...; return; }" (with or without preceding statements) is exempt — this
        // covers "else if" too, since that's a nested IfStatementSyntax under the ElseClause.
        if (block.Parent is IfStatementSyntax)
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
            context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
        }
    }
}
