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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlankLineBeforeTryCodeFixProvider))]
[Shared]
public sealed class BlankLineBeforeTryCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(BlankLineBeforeTryAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Insert blank line before try statement",
                createChangedDocument: cancellationToken => InsertBlankLineAsync(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                equivalenceKey: "InsertBlankLineBeforeTry"),
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

        SyntaxToken tryKeyword = root.FindToken(diagnosticSpan.Start);
        string newLineText = AvigNewLine.Detect(root, tryKeyword);
        SyntaxTriviaList newLeading = tryKeyword.LeadingTrivia.Insert(0, SyntaxFactory.EndOfLine(newLineText));
        SyntaxToken newToken = tryKeyword.WithLeadingTrivia(newLeading);

        SyntaxNode newRoot = root.ReplaceToken(tryKeyword, newToken);
        return document.WithSyntaxRoot(newRoot);
    }

}
