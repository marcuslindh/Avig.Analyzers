using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MutableCollectionReturnCodeFixProvider))]
[Shared]
public sealed class MutableCollectionReturnCodeFixProvider : CodeFixProvider
{
    private const string GenericCollectionsNamespace = "System.Collections.Generic.";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(MutableCollectionReturnAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        if (diagnostic.Properties.TryGetValue(MutableCollectionReturnAnalyzer.ReplacementPropertyKey, out string? replacement) == false
            || string.IsNullOrEmpty(replacement) == true)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Return " + replacement,
                createChangedDocument: cancellationToken => ChangeReturnTypeAsync(context.Document, diagnostic.Location.SourceSpan, replacement!, cancellationToken),
                equivalenceKey: "ReturnReadOnlyCollection"),
            diagnostic);

        return Task.CompletedTask;
    }

    private static async Task<Document> ChangeReturnTypeAsync(Document document, TextSpan diagnosticSpan, string replacement, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        if (root.FindNode(diagnosticSpan) is not TypeSyntax declaredType)
        {
            return document;
        }

        // Written fully qualified and left to the simplifier to shorten: the file may or may not
        // import System.Collections.Generic, and with implicit usings there is no directive to read.
        TypeSyntax replacementType = SyntaxFactory
            .ParseTypeName(GenericCollectionsNamespace + replacement)
            .WithTriviaFrom(declaredType)
            .WithAdditionalAnnotations(Simplifier.Annotation, Formatter.Annotation);

        Document changed = document.WithSyntaxRoot(root.ReplaceNode(declaredType, replacementType));

        return await Simplifier.ReduceAsync(changed, Simplifier.Annotation, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
