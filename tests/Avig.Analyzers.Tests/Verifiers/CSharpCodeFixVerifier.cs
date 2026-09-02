using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Avig.Analyzers.Tests.Verifiers;

/// <summary>Thin wrapper around <see cref="CSharpCodeFixTest{TAnalyzer, TCodeFix, TVerifier}"/>.</summary>
public static class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public sealed class Test : CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = TestReferences.Default;
        }
    }

    /// <summary>Applies the fix to <paramref name="source"/> and asserts the result equals <paramref name="fixedSource"/>.</summary>
    public static async Task VerifyCodeFixAsync(string source, string fixedSource)
    {
        Test test = new()
        {
            TestCode = source,
            FixedCode = fixedSource,
        };

        await test.RunAsync(CancellationToken.None);
    }
}
