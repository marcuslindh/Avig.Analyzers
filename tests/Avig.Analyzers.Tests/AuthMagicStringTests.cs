using Avig.Analyzers.Tests.Verifiers;

namespace Avig.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<AuthMagicStringAnalyzer>;

public class AuthMagicStringTests
{
    [Fact]
    public async Task Reports_a_role_name_written_inline()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using System.Security.Claims;

            public class Guard
            {
                public bool CanEdit(ClaimsPrincipal user)
                {
                    return user.IsInRole({|AVIG0010:"Administrator"|});
                }
            }
            """);
    }

    [Fact]
    public async Task Reports_a_policy_name_on_the_authorize_attribute()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                [Authorize(Policy = {|AVIG0010:"CanEditOrders"|})]
                public void Edit() { }
            }
            """);
    }

    [Fact]
    public async Task Silent_when_the_name_comes_from_a_constant()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using System.Security.Claims;
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            public static class RoleNames
            {
                public const string Administrator = "Administrator";
            }

            public static class PolicyNames
            {
                public const string CanEditOrders = "CanEditOrders";
            }

            public class OrdersController : ControllerBase
            {
                [Authorize(Policy = PolicyNames.CanEditOrders)]
                public bool Edit(ClaimsPrincipal user)
                {
                    return user.IsInRole(RoleNames.Administrator);
                }
            }
            """);
    }

    [Fact]
    public async Task Silent_for_a_bare_authorize_attribute()
    {
        await Verify.VerifySourcesAsync(AspNetStubs.Source, """
            using Microsoft.AspNetCore.Authorization;
            using Microsoft.AspNetCore.Mvc;

            public class OrdersController : ControllerBase
            {
                [Authorize]
                public void Edit() { }
            }
            """);
    }
}
