using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

// This file has to name the markers it searches for, in its own documentation and in
// its own defaults. Suppressed here rather than reworded: the alternative is
// documentation that cannot say what the rule does.
#pragma warning disable AVIG0023

/// <summary>
/// Flags a TODO, FIXME, HACK or XXX comment that carries no tracking reference.
/// </summary>
/// <remarks>
/// The rule does not object to TODO comments. It objects to a TODO that nobody is coming back
/// to: written in a hurry, agreed with by every reviewer since, and invisible to whatever list
/// the team actually works from. An issue id or a link turns the note into something with an
/// owner and a place in a queue; without one it is a comment that documents a decision not to
/// decide.
///
/// This matters more the more code is generated. "TODO: handle the error case" is the cheapest
/// possible way for a model to finish a method, and it reads as diligence. Requiring a
/// reference leaves two honest options: file the issue, or do the work now.
///
/// A reference is an issue number (<c>#123</c>), an issue key (<c>ABC-123</c>) or a URL, and
/// it has to be in the same comment as the marker. Both halves are configurable:
/// <code>
///     dotnet_code_quality.AVIG0023.additional_markers = NOTE,REVIEW
///     dotnet_code_quality.AVIG0023.reference_pattern = (#\d+)|(WORK-\d+)
/// </code>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TrackedTodoCommentAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0023";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "TODO comment without a tracking reference",
        messageFormat: "'{0}' comment has no tracking reference — add an issue id or a link, or do the work now",
        category: "Maintainability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A TODO, FIXME, HACK or XXX comment with no issue id, key or link is a promise with no owner and no place in any queue: it survives every review because nobody can tell whether it is still true. Reference the issue that tracks the work, or do the work instead. The markers and what counts as a reference are configurable through the dotnet_code_quality.AVIG0023.additional_markers and dotnet_code_quality.AVIG0023.reference_pattern options.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    private const string AdditionalMarkersOption = "dotnet_code_quality.AVIG0023.additional_markers";
    private const string ReferencePatternOption = "dotnet_code_quality.AVIG0023.reference_pattern";

    private static readonly char[] Separators = { ',' };

    private static readonly ImmutableArray<string> DefaultMarkers = ImmutableArray.Create(
        "TODO",
        "FIXME",
        "HACK",
        "XXX");

    /// <summary>
    /// An issue number, an issue key or a URL. Deliberately not a person's name: a name says
    /// who wrote the note, not where the work is written down.
    /// </summary>
    private const string DefaultReferencePattern = @"#\d+|\b[A-Z][A-Z0-9]{1,9}-\d+\b|https?://\S";

    private static readonly Regex DefaultReference = new Regex(
        DefaultReferencePattern,
        RegexOptions.CultureInvariant);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);
        ImmutableArray<string> markers = ReadMarkers(context);
        string? referencePattern = ReadReferencePattern(context);

        foreach (SyntaxTrivia trivia in root.DescendantTrivia())
        {
            if (IsComment(trivia) == false)
            {
                continue;
            }

            string text = trivia.ToFullString();
            int markerStart = FindMarker(text, markers, out string marker);
            if (markerStart < 0)
            {
                continue;
            }

            if (HasReference(text, referencePattern) == true)
            {
                continue;
            }

            Location location = Location.Create(
                context.Tree,
                new TextSpan(trivia.FullSpan.Start + markerStart, marker.Length));

            context.ReportDiagnostic(Diagnostic.Create(Rule, location, marker));
        }
    }

    private static bool IsComment(SyntaxTrivia trivia)
    {
        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
    }

    /// <summary>
    /// Returns the offset of the first marker in <paramref name="text"/>, or -1. The marker has
    /// to stand as its own word, so an identifier that merely contains it — <c>TodoListItem</c>
    /// mentioned in prose — is not a match.
    /// </summary>
    private static int FindMarker(string text, ImmutableArray<string> markers, out string marker)
    {
        int best = -1;
        marker = string.Empty;

        foreach (string candidate in markers)
        {
            for (int index = text.IndexOf(candidate, StringComparison.OrdinalIgnoreCase);
                 index >= 0;
                 index = text.IndexOf(candidate, index + 1, StringComparison.OrdinalIgnoreCase))
            {
                if (IsWholeWord(text, index, candidate.Length) == false)
                {
                    continue;
                }

                if (best < 0 || index < best)
                {
                    best = index;
                    marker = text.Substring(index, candidate.Length);
                }

                break;
            }
        }

        return best;
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        if (start > 0 && IsWordCharacter(text[start - 1]) == true)
        {
            return false;
        }

        int after = start + length;

        return after >= text.Length || IsWordCharacter(text[after]) == false;
    }

    private static bool IsWordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    private static bool HasReference(string text, string? pattern)
    {
        if (pattern == null)
        {
            return DefaultReference.IsMatch(text);
        }

        try
        {
            // Regex caches compiled patterns internally, so the configured one is not rebuilt
            // for every comment in the compilation.
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            // An unparseable pattern in .editorconfig must not take the build down with an
            // analyzer crash; fall back to the built-in one.
            return DefaultReference.IsMatch(text);
        }
    }

    private static ImmutableArray<string> ReadMarkers(SyntaxTreeAnalysisContext context)
    {
        ImmutableArray<string> additional = ReadOption(context, AdditionalMarkersOption);

        return additional.IsEmpty == true ? DefaultMarkers : DefaultMarkers.AddRange(additional);
    }

    private static string? ReadReferencePattern(SyntaxTreeAnalysisContext context)
    {
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
        if (options.TryGetValue(ReferencePatternOption, out string? configured) == false
            || string.IsNullOrWhiteSpace(configured) == true)
        {
            return null;
        }

        return configured;
    }

    private static ImmutableArray<string> ReadOption(SyntaxTreeAnalysisContext context, string key)
    {
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree);
        if (options.TryGetValue(key, out string? configured) == false
            || string.IsNullOrWhiteSpace(configured) == true)
        {
            return ImmutableArray<string>.Empty;
        }

        return configured
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Trim())
            .Where(entry => entry.Length > 0)
            .ToImmutableArray();
    }
}

#pragma warning restore AVIG0023
