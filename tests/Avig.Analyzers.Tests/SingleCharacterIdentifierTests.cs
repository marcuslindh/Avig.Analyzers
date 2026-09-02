using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<SingleCharacterIdentifierAnalyzer>;

public class SingleCharacterIdentifierTests
{
    [Fact]
    public async Task Reports_locals_parameters_loop_variables_and_catch_declarations()
    {
        await Verify.VerifyAsync("""
            using System;
            using System.Collections.Generic;

            class Sample
            {
                void Run(IEnumerable<string> values, int {|AVIG0002:n|})
                {
                    int {|AVIG0002:x|} = n;

                    foreach (string {|AVIG0002:v|} in values)
                    {
                        Console.WriteLine(v);
                    }

                    try
                    {
                        Console.WriteLine(x);
                    }
                    catch (Exception {|AVIG0002:e|})
                    {
                        Console.WriteLine(e);
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_descriptive_names()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System;
            using System.Collections.Generic;

            class Sample
            {
                void Run(IEnumerable<string> values, int count)
                {
                    int total = count;

                    foreach (string value in values)
                    {
                        Console.WriteLine(value + total);
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_the_discard()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Collections.Generic;

            class Sample
            {
                bool Run(Dictionary<string, int> cache, string key)
                {
                    return cache.TryGetValue(key, out _);
                }
            }
            """);
    }
}
