using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlankLineAfterClosingBraceCodeFixProvider))]
[Shared]
public sealed class BlankLineAfterClosingBraceCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(BlankLineAfterClosingBraceAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Insert blank line after closing brace",
                createChangedDocument: cancellationToken => InsertBlankLineAsync(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                equivalenceKey: "InsertBlankLineAfterClosingBrace"),
            diagnostic);
        return Task.CompletedTask;
    }

    private static async Task<Document> InsertBlankLineAsync(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        SyntaxToken closeBraceToken = root.FindToken(diagnosticSpan.Start);
        SyntaxNode? nextSibling = BlankLineAfterClosingBraceAnalyzer.FindNextSiblingRequiringBlankLine(closeBraceToken);
        if (nextSibling == null)
        {
            return document;
        }

        SyntaxToken nextToken = nextSibling.GetFirstToken(includeZeroWidth: true);
        string newLineText = AvigNewLine.Detect(root, nextToken);
        SyntaxTriviaList newLeading = nextToken.LeadingTrivia.Insert(0, SyntaxFactory.EndOfLine(newLineText));
        SyntaxToken newNextToken = nextToken.WithLeadingTrivia(newLeading);

        SyntaxNode newRoot = root.ReplaceToken(nextToken, newNextToken);
        return document.WithSyntaxRoot(newRoot);
    }

}
