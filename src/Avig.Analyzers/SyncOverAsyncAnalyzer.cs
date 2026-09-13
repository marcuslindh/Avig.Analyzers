using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags synchronous blocking on an asynchronous operation — <c>task.Result</c>,
/// <c>task.Wait()</c> and <c>task.GetAwaiter().GetResult()</c>.
/// </summary>
/// <remarks>
/// Blocking a thread on a task that has not completed is the classic deadlock: on any host with
/// a synchronization context the continuation needs the very thread that is now sleeping, and
/// neither side ever moves again. Where it does not deadlock it still burns a pool thread for
/// the duration and rewraps exceptions in <c>AggregateException</c>, so the catch clause that
/// used to work stops matching.
///
/// The compilation's entry point is exempt — <c>Main</c> has to block somewhere, and on a
/// modern SDK it can be async instead. Constructors are not exempt: a constructor cannot be
/// made async, but that is an argument for moving the work out of it, not for hiding the
/// deadlock. A completed task read behind an <c>IsCompleted</c> check is reported too; suppress
/// those individually.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncOverAsyncAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0014";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Blocking call on an asynchronous operation",
        messageFormat: "'{0}' blocks the calling thread on an async operation — await it instead",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Task.Result, Task.Wait() and GetAwaiter().GetResult() block the calling thread until the operation completes. On a host with a synchronization context that deadlocks, because the continuation is queued to the thread that is now waiting; elsewhere it still occupies a pool thread and wraps the exception in an AggregateException. Await the call and make the caller async. The compilation entry point is exempt.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    private const string ResultPropertyName = "Result";
    private const string WaitMethodName = "Wait";
    private const string GetResultMethodName = "GetResult";
    private const string AwaiterTypeSuffix = "Awaiter";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationStart =>
        {
            // Resolved once per compilation: GetEntryPoint walks every type looking for Main.
            IMethodSymbol? entryPoint = compilationStart.Compilation.GetEntryPoint(compilationStart.CancellationToken);
            compilationStart.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeMemberAccess(nodeContext, entryPoint),
                SyntaxKind.SimpleMemberAccessExpression);
        });
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context, IMethodSymbol? entryPoint)
    {
        MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)context.Node;
        string memberName = memberAccess.Name.Identifier.ValueText;
        if (memberName != ResultPropertyName && memberName != WaitMethodName && memberName != GetResultMethodName)
        {
            return;
        }

        ISymbol? symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        string? blockingForm = DescribeBlockingCall(symbol, memberName);
        if (blockingForm == null)
        {
            return;
        }

        if (IsInsideEntryPoint(context.SemanticModel, memberAccess, entryPoint) == true)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), blockingForm));
    }

    /// <summary>
    /// Returns the source form to name in the message, or null when the member access is one of
    /// the many unrelated members that happen to share the name.
    /// </summary>
    private static string? DescribeBlockingCall(ISymbol? symbol, string memberName)
    {
        if (symbol == null)
        {
            return null;
        }

        if (memberName == ResultPropertyName)
        {
            return symbol is IPropertySymbol property && AvigSymbols.IsTaskLike(property.ContainingType)
                ? "Result"
                : null;
        }

        if (memberName == WaitMethodName)
        {
            return symbol is IMethodSymbol wait && AvigSymbols.IsTaskLike(wait.ContainingType)
                ? "Wait()"
                : null;
        }

        // GetResult lives on the awaiter, not on the task: TaskAwaiter, ValueTaskAwaiter and the
        // ConfiguredTaskAwaitable nested awaiter all end in "Awaiter", as does any hand-written one.
        return symbol is IMethodSymbol getResult
            && getResult.ContainingType?.Name.EndsWith(AwaiterTypeSuffix, System.StringComparison.Ordinal) == true
            ? "GetAwaiter().GetResult()"
            : null;
    }

    private static bool IsInsideEntryPoint(SemanticModel semanticModel, SyntaxNode node, IMethodSymbol? entryPoint)
    {
        if (entryPoint == null)
        {
            return false;
        }

        for (ISymbol? enclosing = semanticModel.GetEnclosingSymbol(node.SpanStart);
             enclosing != null;
             enclosing = enclosing.ContainingSymbol)
        {
            if (enclosing is IMethodSymbol method
                && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, entryPoint) == true)
            {
                return true;
            }

            if (enclosing is INamedTypeSymbol || enclosing is INamespaceSymbol)
            {
                return false;
            }
        }

        return false;
    }
}
