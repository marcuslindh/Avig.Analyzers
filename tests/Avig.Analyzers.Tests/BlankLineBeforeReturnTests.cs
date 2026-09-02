using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<BlankLineBeforeReturnAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<BlankLineBeforeReturnAnalyzer, BlankLineBeforeReturnCodeFixProvider>;

public class BlankLineBeforeReturnTests
{
    [Fact]
    public async Task Reports_a_return_that_follows_other_code()
    {
        await Verify.VerifyAsync("""
            class Sample
            {
                int Sum(int[] values)
                {
                    int total = 0;
                    {|AVIG0003:return total;|}
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
                int Sum(int[] values)
                {
                    int total = 0;

                    return total;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_return_is_the_first_statement()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            class Sample
            {
                int Sum(int[] values)
                {
                    return values.Length;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_guard_clause()
    {
        // A return directly inside an if body is a guard clause. Demanding a blank line
        // inside a two-line block would make the guard harder to read, not easier.
        await Verify.VerifyNoDiagnosticAsync("""
            class Sample
            {
                int Sum(int[] values)
                {
                    int total = 0;
                    if (values.Length == 0)
                    {
                        total = -1;
                        return total;
                    }

                    return total;
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
                int Sum(int[] values)
                {
                    int total = 0;
                    {|AVIG0003:return total;|}
                }
            }
            """, """
            class Sample
            {
                int Sum(int[] values)
                {
                    int total = 0;

                    return total;
                }
            }
            """);
    }
}
