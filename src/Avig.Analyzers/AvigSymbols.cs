using Microsoft.CodeAnalysis;

namespace Avig.Analyzers;

/// <summary>
/// Shared symbol predicates. Deliberately small: only AVIG0008 needs anything from here,
/// but this is the natural home for the next predicate two rules end up wanting.
/// </summary>
internal static class AvigSymbols
{
    private const string TasksNamespace = "System.Threading.Tasks";
    private const string ThreadingNamespace = "System.Threading";
    private const string CancellationTokenTypeName = "CancellationToken";

    /// <summary>True when <paramref name="type"/> is a non-generic <c>Task</c>/<c>ValueTask</c> or a generic one.</summary>
    internal static bool IsTaskLike(ITypeSymbol? type)
    {
        return type != null
            && (type.Name == "Task" || type.Name == "ValueTask")
            && type.ContainingNamespace?.ToDisplayString() == TasksNamespace;
    }

    /// <summary>True for <c>System.Threading.CancellationToken</c>.</summary>
    internal static bool IsCancellationToken(ITypeSymbol? type)
    {
        return type != null
            && type.Name == CancellationTokenTypeName
            && type.ContainingNamespace?.ToDisplayString() == ThreadingNamespace;
    }
}
