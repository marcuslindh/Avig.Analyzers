using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags any local, parameter, field, foreach variable, catch variable, or pattern/out-var
/// designation whose name is a single character (the discard "_" is exempt).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleCharacterIdentifierAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0002";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Avoid single-character identifier names",
        messageFormat: "'{0}' is a single-character name — use a descriptive name instead",
        category: "Naming",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Locals, parameters, and fields must have descriptive names — no single-character identifiers, including loop counters and lambda parameters.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclarator, SyntaxKind.VariableDeclarator);
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
        context.RegisterSyntaxNodeAction(AnalyzeCatchDeclaration, SyntaxKind.CatchDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeSingleVariableDesignation, SyntaxKind.SingleVariableDesignation);
    }

    private static void AnalyzeVariableDeclarator(SyntaxNodeAnalysisContext context)
    {
        VariableDeclaratorSyntax node = (VariableDeclaratorSyntax)context.Node;
        Report(context, node.Identifier);
    }

    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        ParameterSyntax node = (ParameterSyntax)context.Node;
        Report(context, node.Identifier);
    }

    private static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
    {
        ForEachStatementSyntax node = (ForEachStatementSyntax)context.Node;
        Report(context, node.Identifier);
    }

    private static void AnalyzeCatchDeclaration(SyntaxNodeAnalysisContext context)
    {
        CatchDeclarationSyntax node = (CatchDeclarationSyntax)context.Node;
        Report(context, node.Identifier);
    }

    private static void AnalyzeSingleVariableDesignation(SyntaxNodeAnalysisContext context)
    {
        SingleVariableDesignationSyntax node = (SingleVariableDesignationSyntax)context.Node;
        Report(context, node.Identifier);
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxToken identifier)
    {
        string name = identifier.ValueText;
        if (name.Length != 1 || name == "_")
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), name));
    }
}
