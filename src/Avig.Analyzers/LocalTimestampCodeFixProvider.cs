using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Avig.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalTimestampCodeFixProvider))]
[Shared]
public sealed class LocalTimestampCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(LocalTimestampAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        Diagnostic diagnostic = context.Diagnostics[0];
        if (diagnostic.Properties.TryGetValue(LocalTimestampAnalyzer.ReplacementPropertyKey, out string? replacement) == false
            || string.IsNullOrEmpty(replacement) == true)
        {
            return Task.CompletedTask;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use " + replacement,
                createChangedDocument: cancellationToken => ReplaceWithUtcAsync(context.Document, diagnostic.Location.SourceSpan, replacement!, cancellationToken),
                equivalenceKey: "UseUtcClock." + replacement),
            diagnostic);

        return Task.CompletedTask;
    }

    private static async Task<Document> ReplaceWithUtcAsync(Document document, TextSpan diagnosticSpan, string replacement, CancellationToken cancellationToken)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
        {
            return document;
        }

        if (root.FindNode(diagnosticSpan) is not SimpleNameSyntax name
            || name.Parent is not MemberAccessExpressionSyntax memberAccess)
        {
            return document;
        }

        // "UtcNow" swaps the member in place; "UtcNow.Date" has to wrap the whole access, because
        // DateTime.Today is a date and DateTime.UtcNow is not.
        string[] parts = replacement.Split('.');
        ExpressionSyntax replacementExpression = memberAccess
            .WithoutTrivia()
            .WithName(SyntaxFactory.IdentifierName(parts[0]));
        for (int index = 1; index < parts.Length; index++)
        {
            replacementExpression = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                replacementExpression,
                SyntaxFactory.IdentifierName(parts[index]));
        }

        SyntaxNode newRoot = root.ReplaceNode(memberAccess, replacementExpression.WithTriviaFrom(memberAccess));

        return document.WithSyntaxRoot(newRoot);
    }
}
