using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Avig.Analyzers;

/// <summary>
/// Finds the line ending a document already uses, so an inserted blank line matches it.
/// </summary>
/// <remarks>
/// Looking only at the token the fix is anchored to is not enough. A statement that follows a
/// closing brace has no end-of-line in its leading trivia at all — the newline belongs to the
/// brace's trailing trivia — so a leading-trivia-only search finds nothing and a hardcoded
/// fallback would rewrite an LF file with a stray CRLF. The search therefore widens: the
/// token's own trivia first, then the whole tree, and only then a default.
/// </remarks>
internal static class AvigNewLine
{
    private const string Fallback = "\r\n";

    internal static string Detect(SyntaxNode root, SyntaxToken token)
    {
        return FromTrivia(token.LeadingTrivia)
            ?? FromTrivia(token.TrailingTrivia)
            ?? FromTree(root)
            ?? Fallback;
    }

    private static string? FromTrivia(SyntaxTriviaList trivia)
    {
        foreach (SyntaxTrivia candidate in trivia)
        {
            if (candidate.IsKind(SyntaxKind.EndOfLineTrivia) == true)
            {
                return candidate.ToFullString();
            }
        }

        return null;
    }

    private static string? FromTree(SyntaxNode root)
    {
        foreach (SyntaxTrivia candidate in root.DescendantTrivia())
        {
            if (candidate.IsKind(SyntaxKind.EndOfLineTrivia) == true)
            {
                return candidate.ToFullString();
            }
        }

        return null;
    }
}
