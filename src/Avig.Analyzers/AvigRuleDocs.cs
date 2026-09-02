namespace Avig.Analyzers;

/// <summary>
/// Builds the documentation URL for a rule id.
/// </summary>
/// <remarks>
/// Every rule has a section in the repository README headed exactly <c>## AVIGxxxx</c>, which
/// GitHub slugifies to <c>#avigxxxx</c>. Keeping the human title out of the heading is what
/// makes the anchor stable: rewording a rule's title must not break its help link.
/// </remarks>
internal static class AvigRuleDocs
{
    private const string Base = "https://github.com/marcuslindh/Avig.Analyzers#";

    internal static string For(string diagnosticId)
    {
        return Base + diagnosticId.ToLowerInvariant();
    }
}
