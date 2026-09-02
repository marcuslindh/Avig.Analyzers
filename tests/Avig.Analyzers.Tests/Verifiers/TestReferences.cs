using Microsoft.CodeAnalysis.Testing;

namespace Avig.Analyzers.Tests.Verifiers;

/// <summary>
/// Reference assemblies used by every verifier.
/// </summary>
/// <remarks>
/// Net60 is deliberate. The testing framework predates .NET 10 and ships nothing newer, and
/// none of the twelve rules touch an API added after .NET 6. The rules that look like they
/// need ASP.NET (AVIG0009, AVIG0010, AVIG0012) match on type <em>names</em>, never on real
/// ASP.NET symbols, so the tests declare their own stubs instead of pulling in the shared
/// framework.
/// </remarks>
internal static class TestReferences
{
    internal static readonly ReferenceAssemblies Default = ReferenceAssemblies.Net.Net60;
}
