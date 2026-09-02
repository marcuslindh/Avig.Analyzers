using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Requires every line break in a file's trivia (whitespace between tokens, comments, blank
/// lines) to be CRLF ('\r\n'), matching a Windows-oriented line-ending convention.
/// Deliberately trivia-only — it never touches line breaks embedded inside a string literal's
/// own text (verbatim/raw strings), since "fixing" those would silently change the literal's
/// runtime value instead of just reformatting the file.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrlfLineEndingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0006";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Use CRLF line endings",
        messageFormat: "Line ending must be CRLF ('\\r\\n')",
        category: "Formatting",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false,
        description: "Every line break outside of string literal content must be CRLF ('\\r\\n'), not a bare LF.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);
        foreach (SyntaxTrivia trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia) == false)
            {
                continue;
            }

            if (trivia.ToString() != "\r\n")
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, trivia.GetLocation()));
            }
        }
    }
}
