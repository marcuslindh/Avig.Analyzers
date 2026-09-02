namespace Avig.Analyzers.Tests;

/// <summary>
/// Minimal ASP.NET declarations pasted into test sources.
/// </summary>
/// <remarks>
/// AVIG0009, AVIG0010 and AVIG0012 match on type and attribute <em>names</em>, never on real
/// ASP.NET symbols — which is what makes them work in a project that rolls its own controller
/// base. Stubbing the names instead of referencing Microsoft.AspNetCore.App keeps the tests
/// fast and, more usefully, proves the name-matching claim.
/// </remarks>
internal static class AspNetStubs
{
    internal const string Source = """
        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase { }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class HttpGetAttribute : System.Attribute
            {
                public HttpGetAttribute() { }
                public HttpGetAttribute(string template) { }
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class HttpPostAttribute : System.Attribute
            {
                public HttpPostAttribute() { }
                public HttpPostAttribute(string template) { }
            }

            public interface IActionResult { }
        }

        namespace Microsoft.AspNetCore.Authorization
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class AuthorizeAttribute : System.Attribute
            {
                public AuthorizeAttribute() { }
                public AuthorizeAttribute(string policy) { }
                public string? Policy { get; set; }
                public string? Roles { get; set; }
            }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
            public sealed class AllowAnonymousAttribute : System.Attribute { }
        }

        """;
}
