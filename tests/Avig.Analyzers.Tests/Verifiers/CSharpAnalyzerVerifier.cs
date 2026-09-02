using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Avig.Analyzers.Tests.Verifiers;

/// <summary>Thin wrapper around <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/>.</summary>
public static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public sealed class Test : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = TestReferences.Default;
        }
    }

    /// <summary>
    /// Runs the analyzer over <paramref name="source"/>. Expected diagnostics are marked up
    /// inline as {|AVIG0001:...|}, so there are no hand-counted line and column numbers to
    /// keep in sync.
    /// </summary>
    public static async Task VerifyAsync(string source)
    {
        Test test = new() { TestCode = source };

        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Runs the analyzer with explicitly listed expected diagnostics. Needed when the source
    /// has to be built programmatically and inline markup is not an option.
    /// </summary>
    public static async Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        Test test = new() { TestCode = source };
        test.ExpectedDiagnostics.AddRange(expected);

        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Runs the analyzer over several source files. Used to keep shared stub declarations in
    /// their own file — concatenating them ahead of a test source puts using directives after
    /// namespace declarations, which is a compile error.
    /// </summary>
    public static async Task VerifySourcesAsync(params string[] sources)
    {
        Test test = new();
        foreach (string source in sources)
        {
            test.TestState.Sources.Add(source);
        }

        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>Code that must produce no diagnostics at all.</summary>
    public static Task VerifyNoDiagnosticAsync(string source)
    {
        return VerifyAsync(source);
    }

    public static DiagnosticResult Diagnostic(string diagnosticId)
    {
        return new DiagnosticResult(diagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
    }
}
