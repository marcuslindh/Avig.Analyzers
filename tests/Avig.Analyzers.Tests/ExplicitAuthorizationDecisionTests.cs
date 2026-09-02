using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<ExplicitAuthorizationDecisionAnalyzer>;

public class ExplicitAuthorizationDecisionTests
{
    [Fact]
    public async Task Reports_an_action_with_no_decision_at_all()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                [HttpGet("orders/{id}")]
                public IActionResult? {|AVIG0012:Get|}(int id) => null;
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_action_is_authorized()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                [Authorize]
                [HttpGet("orders/{id}")]
                public IActionResult? Get(int id) => null;
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_action_is_deliberately_anonymous()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            public class HealthController : ControllerBase
            {
                [AllowAnonymous]
                [HttpGet("health")]
                public IActionResult? Health() => null;
            }
            """);
    }

    [Fact]
    public async Task Silent_when_a_base_controller_carries_the_attribute()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            [Authorize]
            public abstract class SecuredController : ControllerBase { }

            public class OrdersController : SecuredController
            {
                [HttpGet("orders/{id}")]
                public IActionResult? Get(int id) => null;
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_custom_attribute_named_in_editorconfig()
    {
        Verify.Test test = new()
        {
            TestState =
            {
                Sources = { AspNetStubs.Source, """
                    using Microsoft.AspNetCore.Mvc;

                    [System.AttributeUsage(System.AttributeTargets.Method)]
                    public sealed class HmacAuthenticatedAttribute : System.Attribute { }

                    public class WebhookController : ControllerBase
                    {
                        [HmacAuthenticated]
                        [HttpPost("hooks/delivery")]
                        public IActionResult? Deliver() => null;
                    }
                    """ },
                AnalyzerConfigFiles =
                {
                    ("/.editorconfig", """
                        root = true

                        [*.cs]
                        dotnet_code_quality.AVIG0012.additional_auth_attributes = HmacAuthenticated
                        """),
                },
            },
        };

        await test.RunAsync(CancellationToken.None);
    }
}
