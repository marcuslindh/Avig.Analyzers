using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<BlankLineBeforeTryAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<BlankLineBeforeTryAnalyzer, BlankLineBeforeTryCodeFixProvider>;

public class BlankLineBeforeTryTests
{
    [Fact]
    public async Task Reports_a_try_that_follows_other_code()
    {
        await Verify.VerifyAsync("""
            using System;

            class Sample
            {
                void Run()
                {
                    int attempts = 0;
                    {|AVIG0004:try|}
                    {
                        attempts++;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_preceded_by_a_blank_line()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System;

            class Sample
            {
                void Run()
                {
                    int attempts = 0;

                    try
                    {
                        attempts++;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_try_is_the_first_statement()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System;

            class Sample
            {
                void Run()
                {
                    try
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Fix_inserts_the_blank_line()
    {
        await VerifyFix.VerifyCodeFixAsync("""
            using System;

            class Sample
            {
                void Run()
                {
                    int attempts = 0;
                    {|AVIG0004:try|}
                    {
                        attempts++;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            """, """
            using System;

            class Sample
            {
                void Run()
                {
                    int attempts = 0;

                    try
                    {
                        attempts++;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
            """);
    }
}
