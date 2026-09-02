using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<RequiredXmlDocumentationAnalyzer>;

public class RequiredXmlDocumentationTests
{
    [Fact]
    public async Task Reports_an_undocumented_controller_and_repository_interface()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Mvc;

            public class {|AVIG0009:OrdersController|} : ControllerBase { }

            public interface {|AVIG0009:IOrderRepository|} { }
            """);
    }

    [Fact]
    public async Task Silent_when_both_carry_a_summary()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Mvc;

            /// <summary>Reads and places orders.</summary>
            public class OrdersController : ControllerBase { }

            /// <summary>Persistence for orders.</summary>
            public interface IOrderRepository { }
            """);
    }

    [Fact]
    public async Task Silent_for_non_public_types()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Mvc;

            internal class OrdersController : ControllerBase { }

            internal interface IOrderRepository { }
            """);
    }

    [Fact]
    public async Task Silent_for_an_ordinary_public_class()
    {
        // Regression guard for the three categories that were dropped when the rule left its
        // original repository: a plain public type is not this rule's business.
        await Verify.VerifyNoDiagnosticAsync("""
            public interface IOrderCommand { }

            public class PlaceOrderCommand : IOrderCommand { }
            """);
    }

    [Fact]
    public async Task Reports_a_marker_interface_named_in_editorconfig()
    {
        Verify.Test test = new()
        {
            TestState =
            {
                Sources = { """
                    public interface IAppCommand { }

                    public class {|AVIG0009:PlaceOrderCommand|} : IAppCommand { }
                    """ },
                AnalyzerConfigFiles =
                {
                    ("/.editorconfig", """
                        root = true

                        [*.cs]
                        dotnet_code_quality.AVIG0009.additional_documented_interfaces = IAppCommand,IAppQuery
                        """),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }
}
