using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers.Tests;

/// <summary>
/// Guards the descriptor metadata the release-tracking file and the README both depend on.
/// </summary>
/// <remarks>
/// RS2001 catches a category or severity that drifts away from the release-tracking files, but
/// only at build time in this repository. These tests also cover what RS2001 does not: that the
/// shipped ids are the ones the README documents, and that every rule has a help link pointing at
/// its README section.
/// </remarks>
public class RuleContractTests
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Descriptors = LoadDescriptors();

    private static ImmutableArray<DiagnosticDescriptor> LoadDescriptors()
    {
        return typeof(BlankLineAfterClosingBraceAnalyzer).Assembly
            .GetTypes()
            .Where(type => type.IsAbstract == false && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .ToImmutableArray();
    }

    [Fact]
    public void Ships_exactly_the_documented_rules()
    {
        string[] expected =
        [
            "AVIG0001", "AVIG0002", "AVIG0003", "AVIG0004", "AVIG0005", "AVIG0006",
            "AVIG0007", "AVIG0008", "AVIG0009", "AVIG0010", "AVIG0011", "AVIG0012",
            "AVIG0014", "AVIG0015", "AVIG0020", "AVIG0021", "AVIG0022", "AVIG0023", "AVIG0024",
        ];

        Assert.Equal(expected, Descriptors.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Only_the_silent_failure_rules_warn_out_of_the_box()
    {
        // A public package that starts breaking builds on install gets uninstalled, so a rule
        // only earns Warning when a suggestion would defeat its purpose: three of these fail
        // silently in production, where the build log is the only place the problem can
        // surface, and the fourth exists to stop something being forgotten. Everything else is
        // a suggestion until the consumer says otherwise.
        string[] warning = Descriptors
            .Where(descriptor => descriptor.DefaultSeverity == DiagnosticSeverity.Warning)
            .Select(descriptor => descriptor.Id)
            .ToArray();

        Assert.Equal(["AVIG0010", "AVIG0014", "AVIG0015", "AVIG0023"], warning.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_rule_links_to_its_readme_section()
    {
        foreach (DiagnosticDescriptor descriptor in Descriptors)
        {
            Assert.Equal(
                "https://github.com/marcuslindh/Avig.Analyzers#" + descriptor.Id.ToLowerInvariant(),
                descriptor.HelpLinkUri);
        }
    }

    [Fact]
    public void Every_rule_has_a_title_and_a_description()
    {
        foreach (DiagnosticDescriptor descriptor in Descriptors)
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Title.ToString(CultureInfo.InvariantCulture)), descriptor.Id);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description.ToString(CultureInfo.InvariantCulture)), descriptor.Id);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Category), descriptor.Id);
        }
    }
}
