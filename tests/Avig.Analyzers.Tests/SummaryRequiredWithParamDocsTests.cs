using Avig.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<SummaryRequiredWithParamDocsAnalyzer>;

public class SummaryRequiredWithParamDocsTests
{
    [Fact]
    public async Task Reports_param_and_returns_without_a_summary()
    {
        // The span is pinned rather than marked up inline: a structured documentation comment's
        // Span starts after the "///" exterior, so the reported start is three columns to the
        // right of where the comment visually begins.
        await Verify.VerifyAsync("""
            public class Repository
            {
                /// <param name="id">The order id.</param>
                /// <returns>The order, or null.</returns>
                public object? Get(int id) => null;
            }
            """, Verify.Diagnostic("AVIG0011").WithSpan(3, 8, 5, 1));
    }

    [Fact]
    public async Task Silent_when_a_summary_is_present()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Repository
            {
                /// <summary>Reads a single order.</summary>
                /// <param name="id">The order id.</param>
                /// <returns>The order, or null when it does not exist.</returns>
                public object? Get(int id) => null;
            }
            """);
    }

    [Fact]
    public async Task Silent_for_inheritdoc()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Repository
            {
                /// <inheritdoc/>
                /// <param name="id">The order id.</param>
                public object? Get(int id) => null;
            }
            """);
    }

    [Fact]
    public async Task A_types_summary_is_not_credited_to_its_members()
    {
        // The check is scoped per token's leading trivia. Without that, the class summary
        // directly above would silently satisfy the member below it.
        await Verify.VerifyAsync("""
            /// <summary>Persistence for orders.</summary>
            public class Repository
            {
                /// <param name="id">The order id.</param>
                public object? Get(int id) => null;
            }
            """, Verify.Diagnostic("AVIG0011").WithSpan(4, 8, 5, 1));
    }

    [Fact]
    public async Task Works_when_documentation_parsing_is_off()
    {
        // The case the rule was written for. With DocumentationMode.None — the default when a
        // project does not set GenerateDocumentationFile — Roslyn lexes "///" as an ordinary
        // comment and never produces the structured nodes a syntax-node action would need.
        // The trivia then spans the "///" itself, so the diagnostic starts three columns left
        // of where it does above.
        Verify.Test test = new()
        {
            TestCode = """
                public class Repository
                {
                    /// <param name="id">The order id.</param>
                    public object? Get(int id) => null;
                }
                """,
        };

        test.SolutionTransforms.Add((solution, projectId) =>
        {
            Project project = solution.GetProject(projectId)!;
            CSharpParseOptions options = ((CSharpParseOptions)project.ParseOptions!)
                .WithDocumentationMode(DocumentationMode.None);

            return solution.WithProjectParseOptions(projectId, options);
        });

        test.ExpectedDiagnostics.Add(Verify.Diagnostic("AVIG0011").WithSpan(3, 5, 3, 47));

        await test.RunAsync(CancellationToken.None);
    }
}
