using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers.Tests;

/// <summary>
/// Guards the descriptor metadata the release-tracking file and the README both depend on.
/// </summary>
/// <remarks>
/// RS2001 catches a category or severity that drifts away from AnalyzerReleases.Shipped.md, but
/// only at build time in this repository. These tests also cover what RS2001 does not: that the
/// twelve ids are the twelve we shipped, and that every rule has a help link pointing at its
/// README section.
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
    public void Ships_exactly_the_twelve_documented_rules()
    {
        string[] expected =
        [
            "AVIG0001", "AVIG0002", "AVIG0003", "AVIG0004", "AVIG0005", "AVIG0006",
            "AVIG0007", "AVIG0008", "AVIG0009", "AVIG0010", "AVIG0011", "AVIG0012",
        ];

        Assert.Equal(expected, Descriptors.Select(descriptor => descriptor.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Only_AVIG0010_warns_out_of_the_box()
    {
        // A public package that starts breaking builds on install gets uninstalled. Everything
        // else is a suggestion until the consumer says otherwise.
        string[] warning = Descriptors
            .Where(descriptor => descriptor.DefaultSeverity == DiagnosticSeverity.Warning)
            .Select(descriptor => descriptor.Id)
            .ToArray();

        Assert.Equal(["AVIG0010"], warning);
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
