using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<NullableReturnAnalyzer>;

public class NullableReturnTests
{
    [Fact]
    public async Task Reports_null_on_one_path_and_a_value_on_another()
    {
        await Verify.VerifyAsync("""
            #nullable enable

            class Order
            {
                public Order(string raw) { }
            }

            class Parser
            {
                public Order? {|AVIG0007:Parse|}(string raw)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return null;
                    }

                    return new Order(raw);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_every_path_returns_a_value()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            #nullable enable

            class Order
            {
                public Order(string raw) { }
            }

            class Parser
            {
                public Order? Parse(string raw)
                {
                    return new Order(raw);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_method_already_returns_a_result_type()
    {
        // The escape hatch is name-only on purpose: the rule asks for a result type, and it has
        // no business insisting on whose.
        await Verify.VerifyNoDiagnosticAsync("""
            #nullable enable

            namespace Some.Other.Library
            {
                public class Result<T>
                {
                    public static Result<T>? Failure(string message) => null;
                    public static Result<T>? Success(T value) => null;
                }
            }

            namespace Consumer
            {
                using Some.Other.Library;

                class Order { }

                class Parser
                {
                    public Result<Order>? Parse(string raw)
                    {
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            return null;
                        }

                        return Result<Order>.Success(new Order());
                    }
                }
            }
            """);
    }
}
