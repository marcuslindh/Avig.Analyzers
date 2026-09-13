using Avig.Analyzers.Tests.Verifiers;
using Microsoft.CodeAnalysis;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<SyncOverAsyncAnalyzer>;

public class SyncOverAsyncTests
{
    [Fact]
    public async Task Reports_the_three_blocking_forms()
    {
        await Verify.VerifyAsync("""
            using System.Threading.Tasks;

            public class Consumer
            {
                public int Read(Task<int> task)
                {
                    return task.{|AVIG0014:Result|};
                }

                public void Block(Task task)
                {
                    task.{|AVIG0014:Wait|}();
                }

                public int Drain(Task<int> task)
                {
                    return task.GetAwaiter().{|AVIG0014:GetResult|}();
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_through_configure_await()
    {
        await Verify.VerifyAsync("""
            using System.Threading.Tasks;

            public class Consumer
            {
                public int Read(Task<int> task)
                {
                    return task.ConfigureAwait(false).GetAwaiter().{|AVIG0014:GetResult|}();
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_inside_a_constructor()
    {
        // A constructor cannot be made async, which is an argument for moving the work out of it
        // rather than for hiding the deadlock.
        await Verify.VerifyAsync("""
            using System.Threading.Tasks;

            public class Consumer
            {
                private readonly int _value;

                public Consumer(Task<int> task)
                {
                    _value = task.{|AVIG0014:Result|};
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_call_is_awaited()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            using System.Threading.Tasks;

            public class Consumer
            {
                public async Task<int> ReadAsync(Task<int> task)
                {
                    return await task;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_unrelated_members_with_the_same_names()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Measurement
            {
                public int Result { get; set; }

                public void Wait() { }
            }

            public class Consumer
            {
                public int Read(Measurement measurement)
                {
                    measurement.Wait();

                    return measurement.Result;
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_in_the_entry_point()
    {
        // Main has to block somewhere. Everything it calls does not.
        Verify.Test test = new()
        {
            TestCode = """
                using System.Threading.Tasks;

                public static class Program
                {
                    public static void Main()
                    {
                        Task.Delay(1).Wait();
                    }
                }
                """,
        };

        test.TestState.OutputKind = OutputKind.ConsoleApplication;

        await test.RunAsync(CancellationToken.None);
    }
}
