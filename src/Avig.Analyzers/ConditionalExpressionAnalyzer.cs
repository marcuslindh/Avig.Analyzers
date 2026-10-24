using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags every conditional expression (<c>condition ? whenTrue : whenFalse</c>), the "inline if".
/// A nested conditional reports once per '?', so each one can be taken apart on its own.
/// </summary>
/// <remarks>
/// Three places are exempt because no statement can stand there at all, so the report could not
/// be acted on: a <c>const</c> declaration, an attribute argument and a parameter default value.
/// Everywhere else — field initializers, expression-bodied members, lambdas — the expression
/// can move into a block.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ConditionalExpressionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0024";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Use an if statement instead of a conditional expression",
        messageFormat: "Replace the inline if '?:' with an if statement",
        category: "Readability",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A conditional expression hides a branch inside an expression: neither arm can carry its own breakpoint, line coverage counts the line as covered when only one arm ran, and nested ones read right to left. Write the branch as an if statement. Const declarations, attribute arguments and parameter defaults are exempt.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeConditional, SyntaxKind.ConditionalExpression);
    }

    private static void AnalyzeConditional(SyntaxNodeAnalysisContext context)
    {
        ConditionalExpressionSyntax node = (ConditionalExpressionSyntax)context.Node;
        if (IsInConstantContext(node) == true)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation()));
    }

    private static bool IsInConstantContext(SyntaxNode node)
    {
        foreach (SyntaxNode ancestor in node.Ancestors())
        {
            if (ancestor is AttributeArgumentSyntax || ancestor is ParameterSyntax)
            {
                return true;
            }

            if (ancestor is LocalDeclarationStatementSyntax local)
            {
                return local.IsConst;
            }

            if (ancestor is FieldDeclarationSyntax field)
            {
                return field.Modifiers.Any(SyntaxKind.ConstKeyword);
            }

            if (ancestor is StatementSyntax || ancestor is MemberDeclarationSyntax)
            {
                return false;
            }
        }

        return false;
    }
}
