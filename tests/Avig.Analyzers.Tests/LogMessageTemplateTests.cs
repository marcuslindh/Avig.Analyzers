using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<LogMessageTemplateAnalyzer>;

public class LogMessageTemplateTests
{
    /// <summary>
    /// Stubs rather than the real packages: the rule matches on the shape of the call and the name
    /// of the receiver's type, never on ILogger or Serilog symbols.
    /// </summary>
    private const string LoggerSource = """
        using System;

        public interface ILogger
        {
            void LogInformation(string message, params object[] args);

            void LogError(Exception exception, string message, params object[] args);
        }

        public static class Log
        {
            public static void Information(string message, params object[] args) { }
        }

        public static class Telemetry
        {
            public static void Information(string message, params object[] args) { }
        }
        """;

    [Fact]
    public async Task Reports_an_interpolated_message()
    {
        await Verify.VerifySourcesAsync(LoggerSource, """
            public class Orders
            {
                private readonly ILogger _logger;

                public Orders(ILogger logger)
                {
                    _logger = logger;
                }

                public void Place(int id)
                {
                    _logger.LogInformation({|AVIG0021:$"Order {id} placed"|});
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_string_format()
    {
        await Verify.VerifySourcesAsync(LoggerSource, """
            using System;

            public class Orders
            {
                private readonly ILogger _logger;

                public Orders(ILogger logger)
                {
                    _logger = logger;
                }

                public void Place(int id)
                {
                    _logger.LogInformation({|AVIG0021:string.Format("Order {0} placed", id)|});
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_the_template_but_not_the_exception_overloads_other_arguments()
    {
        await Verify.VerifySourcesAsync(LoggerSource, """
            using System;

            public class Orders
            {
                private readonly ILogger _logger;

                public Orders(ILogger logger)
                {
                    _logger = logger;
                }

                public void Fail(Exception exception, int id)
                {
                    _logger.LogError(exception, {|AVIG0021:$"Order {id} failed"|});
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_a_static_logger_entry_point()
    {
        await Verify.VerifySourcesAsync(LoggerSource, """
            public class Orders
            {
                public void Place(int id)
                {
                    Log.Information({|AVIG0021:$"Order {id} placed"|});
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_template_with_named_placeholders()
    {
        await Verify.VerifySourcesAsync(LoggerSource, """
            public class Orders
            {
                private readonly ILogger _logger;

                public Orders(ILogger logger)
                {
                    _logger = logger;
                }

                public void Place(int id)
                {
                    _logger.LogInformation("Order {OrderId} placed", id);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_an_interpolated_string_without_holes()
    {
        await Verify.VerifySourcesAsync(LoggerSource, """
            public class Orders
            {
                private readonly ILogger _logger;

                public Orders(ILogger logger)
                {
                    _logger = logger;
                }

                public void Place()
                {
                    _logger.LogInformation($"Order placed");
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_an_interpolated_value_argument()
    {
        // Odd, but it is the template that has to stay constant, not the values bound to it.
        await Verify.VerifySourcesAsync(LoggerSource, """
            public class Orders
            {
                private readonly ILogger _logger;

                public Orders(ILogger logger)
                {
                    _logger = logger;
                }

                public void Place(string first, string last)
                {
                    _logger.LogInformation("Order placed by {Customer}", $"{first} {last}");
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_receiver_that_is_not_a_logger()
    {
        await Verify.VerifyNoDiagnosticAsync("""
            public class Mailer
            {
                public void Information(string message) { }
            }

            public class Orders
            {
                private readonly Mailer _mailer = new Mailer();

                public void Place(int id)
                {
                    _mailer.Information($"Order {id} placed");
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_a_logger_type_named_in_editorconfig()
    {
        Verify.Test test = new()
        {
            TestState =
            {
                Sources =
                {
                    LoggerSource,
                    """
                    public class Orders
                    {
                        public void Place(int id)
                        {
                            Telemetry.Information({|AVIG0021:$"Order {id} placed"|});
                        }
                    }
                    """,
                },
                AnalyzerConfigFiles =
                {
                    ("/.editorconfig", """
                        root = true

                        [*.cs]
                        dotnet_code_quality.AVIG0021.additional_logger_types = Telemetry,AuditTrail
                        """),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }
}
