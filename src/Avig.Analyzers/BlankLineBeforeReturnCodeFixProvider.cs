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

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlankLineBeforeReturnCodeFixProvider))]
[Shared]
public sealed class BlankLineBeforeReturnCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(BlankLineBeforeReturnAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Insert blank line before return statement",
                createChangedDocument: cancellationToken => InsertBlankLineAsync(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                equivalenceKey: "InsertBlankLineBeforeReturn"),
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

        SyntaxNode? node = root.FindNode(diagnosticSpan);
        if (node == null)
        {
            return document;
        }

        SyntaxToken firstToken = node.GetFirstToken(includeZeroWidth: true);
        string newLineText = AvigNewLine.Detect(root, firstToken);
        SyntaxTriviaList newLeading = firstToken.LeadingTrivia.Insert(0, SyntaxFactory.EndOfLine(newLineText));
        SyntaxToken newToken = firstToken.WithLeadingTrivia(newLeading);

        SyntaxNode newRoot = root.ReplaceToken(firstToken, newToken);

        return document.WithSyntaxRoot(newRoot);
    }

}
