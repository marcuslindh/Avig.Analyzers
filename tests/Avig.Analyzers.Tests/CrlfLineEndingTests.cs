using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<CrlfLineEndingAnalyzer>;

public class CrlfLineEndingTests
{
    private const string Template = """
        class Sample
        {
        }
        """;

    [Fact]
    public async Task Reports_every_bare_LF_line_ending()
    {
        // The repository is CRLF, so an LF fixture cannot be written as a literal — it has to
        // be produced at run time or .gitattributes would silently repair it.
        string source = Template.Replace("\r\n", "\n");

        await Verify.VerifyAsync(
            source,
            Verify.Diagnostic("AVIG0006").WithSpan(1, 13, 2, 1),
            Verify.Diagnostic("AVIG0006").WithSpan(2, 2, 3, 1));
    }

    [Fact]
    public async Task Silent_when_every_line_ending_is_CRLF()
    {
        string source = Template.Replace("\r\n", "\n").Replace("\n", "\r\n");

        await Verify.VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public void Ships_disabled_so_an_LF_repository_is_not_flooded_on_install()
    {
        // The test host force-enables every supported diagnostic, so this contract can only be
        // checked on the descriptor itself — which is also where a consumer's tooling reads it.
        CrlfLineEndingAnalyzer analyzer = new();

        Assert.False(analyzer.SupportedDiagnostics[0].IsEnabledByDefault);
    }
}
