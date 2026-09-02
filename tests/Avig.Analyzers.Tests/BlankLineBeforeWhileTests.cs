using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<BlankLineBeforeWhileAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<BlankLineBeforeWhileAnalyzer, BlankLineBeforeWhileCodeFixProvider>;

public class BlankLineBeforeWhileTests
{
    [Fact]
    public async Task Reports_a_while_that_follows_other_code()
    {
        await Verify.VerifyAsync("""
            class Sample
            {
                void Run(int count)
                {
                    int remaining = count;
                    {|AVIG0005:while|} (remaining > 0)
                    {
                        remaining--;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_preceded_by_a_blank_line()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            class Sample
            {
                void Run(int count)
                {
                    int remaining = count;

                    while (remaining > 0)
                    {
                        remaining--;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_do_while()
    {
        // The trailing 'while' of a do-while is a different syntax node and must never be
        // reported — inserting a blank line there would be a syntax error.
        await Verify.VerifyNoDiagnosticAsync("""
            class Sample
            {
                void Run(int count)
                {
                    int remaining = count;

                    do
                    {
                        remaining--;
                    }
                    while (remaining > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task Fix_inserts_the_blank_line()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            class Sample
            {
                void Run(int count)
                {
                    int remaining = count;
                    {|AVIG0005:while|} (remaining > 0)
                    {
                        remaining--;
                    }
                }
            }
            """, """
            class Sample
            {
                void Run(int count)
                {
                    int remaining = count;

                    while (remaining > 0)
                    {
                        remaining--;
                    }
                }
            }
            """);
    }
}
