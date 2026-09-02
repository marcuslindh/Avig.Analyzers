using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CrlfLineEndingCodeFixProvider))]
[Shared]
public sealed class CrlfLineEndingCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(CrlfLineEndingAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to CRLF",
                createChangedDocument: cancellationToken => ConvertToCrlfAsync(context.Document, diagnostic.Location.SourceSpan, cancellationToken),
                equivalenceKey: "ConvertToCrlf"),
            diagnostic);
        return Task.CompletedTask;
    }

    private static async Task<Document> ConvertToCrlfAsync(Document document, TextSpan diagnosticSpan, CancellationToken cancellationToken)
    {
        SourceText? text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (text == null)
        {
            return document;
        }

        SourceText newText = text.Replace(diagnosticSpan, "\r\n");
        return document.WithText(newText);
    }
}
