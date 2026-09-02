using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

/// <summary>
/// Flags an XML documentation comment that documents the details — &lt;param&gt;,
/// &lt;returns&gt;, &lt;typeparam&gt;, &lt;exception&gt; or &lt;value&gt; — without ever
/// saying what the member does, i.e. without a &lt;summary&gt;.
/// </summary>
/// <remarks>
/// Roslynator has the same check as RCS1139, but only in its Visual Studio extension:
/// the rule is not in the Roslynator.Analyzers NuGet package the build uses. The result
/// was a diagnostic that showed as an error in the IDE while the build knew nothing
/// about it. Implementing it here puts the check where CI can see it.
///
/// The comment is read as raw trivia text rather than as a DocumentationCommentTriviaSyntax.
/// When the consuming project does not set GenerateDocumentationFile, DocumentationMode is
/// None and Roslyn never parses doc comments into structured nodes — a syntax-node action on
/// SingleLineDocumentationCommentTrivia simply never fires. Same reason
/// <see cref="RequiredXmlDocumentationAnalyzer"/> reads trivia.
///
/// Unlike AVIG0009, which demands a summary on specific public categories, this rule only
/// fires when the author already started documenting — it never asks for docs that do not
/// exist.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SummaryRequiredWithParamDocsAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0011";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Documentation comment has element docs but no summary",
        messageFormat: "Documentation comment documents <{0}> but has no <summary>",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A comment that only carries <param> or <returns> describes the parts without ever stating what the member is for. Add a <summary>, or use <inheritdoc/> when the base member already explains it.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    /// <summary>Elements that describe a detail of the member rather than the member itself.</summary>
    private static readonly string[] DetailElements = { "param", "typeparam", "returns", "exception", "value" };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);

        // Sviten måste scopas per token, inte över hela trivia-strömmen. Tokens
        // ingår inte i DescendantTrivia(), så en global genomgång skulle slå ihop
        // typens doc-kommentar med medlemmens — och typens <summary> skulle då
        // felaktigt räknas som medlemmens.
        List<SyntaxTrivia> run = new List<SyntaxTrivia>();
        foreach (SyntaxToken token in root.DescendantTokens())
        {
            run.Clear();
            foreach (SyntaxTrivia trivia in token.LeadingTrivia)
            {
                if (IsDocComment(trivia) == true)
                {
                    run.Add(trivia);
                    continue;
                }

                // Blanksteg och radbrytningar bryter inte upp en svit av /// -rader.
                if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) == true
                    || trivia.IsKind(SyntaxKind.EndOfLineTrivia) == true)
                {
                    continue;
                }

                ReportIfMissingSummary(context, run);
                run.Clear();
            }

            ReportIfMissingSummary(context, run);
        }
    }

    /// <summary>
    /// Identifies a doc comment without trusting the trivia kind. When the consuming project
    /// does not set GenerateDocumentationFile, DocumentationMode is None and Roslyn lexes "///"
    /// as an ordinary SingleLineCommentTrivia — the dedicated documentation kinds never appear.
    /// The prefix is the only reliable signal. The kinds are still tested first, for projects
    /// that do turn documentation generation on.
    /// </summary>
    private static bool IsDocComment(SyntaxTrivia trivia)
    {
        if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) == true
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia) == true)
        {
            return true;
        }

        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) == true)
        {
            return trivia.ToString().TrimStart().StartsWith("///", StringComparison.Ordinal);
        }

        if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) == true)
        {
            return trivia.ToString().TrimStart().StartsWith("/**", StringComparison.Ordinal);
        }

        return false;
    }

    private static void ReportIfMissingSummary(SyntaxTreeAnalysisContext context, List<SyntaxTrivia> run)
    {
        if (run.Count == 0)
        {
            return;
        }

        string text = string.Concat(run.ConvertAll(trivia => trivia.ToFullString()));

        if (Contains(text, "<summary") == true || Contains(text, "<inheritdoc") == true)
        {
            return;
        }

        foreach (string element in DetailElements)
        {
            if (Contains(text, "<" + element) == false)
            {
                continue;
            }

            TextSpan span = TextSpan.FromBounds(run[0].SpanStart, run[run.Count - 1].Span.End);
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, Location.Create(context.Tree, span), element));

            return;
        }
    }

    private static bool Contains(string text, string value)
    {
        return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
