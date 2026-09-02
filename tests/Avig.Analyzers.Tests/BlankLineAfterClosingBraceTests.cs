using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<BlankLineAfterClosingBraceAnalyzer>;
using VerifyFix = CSharpCodeFixVerifier<BlankLineAfterClosingBraceAnalyzer, BlankLineAfterClosingBraceCodeFixProvider>;

public class BlankLineAfterClosingBraceTests
{
    [Fact]
    public async Task Reports_when_a_statement_follows_the_closing_brace_directly()
    {
        await Verify.VerifyAsync("""
            class Sample
            {
                int Run(int input)
                {
                    if (input > 0)
                    {
                        input = 0;
                    {|AVIG0001:}|}
                    return input;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_a_blank_line_follows()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            class Sample
            {
                int Run(int input)
                {
                    if (input > 0)
                    {
                        input = 0;
                    }

                    return input;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_mid_construct_try_catch_finally_braces()
    {
        // Braces that close one part of a construct the next keyword continues are not
        // paragraph breaks, so they are exempt.
        await Verify.VerifyNoDiagnosticAsync("""
            using System;

            class Sample
            {
                void Run()
                {
                    try
                    {
                        Console.WriteLine("work");
                    }
                    catch (InvalidOperationException)
                    {
                        Console.WriteLine("first");
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("second");
                    }
                    finally
                    {
                        Console.WriteLine("cleanup");
                    }
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
                int Run(int input)
                {
                    if (input > 0)
                    {
                        input = 0;
                    {|AVIG0001:}|}
                    return input;
                }
            }
            """, """
            class Sample
            {
                int Run(int input)
                {
                    if (input > 0)
                    {
                        input = 0;
                    }

                    return input;
                }
            }
            """);
    }
}
