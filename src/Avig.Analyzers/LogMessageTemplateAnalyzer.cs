using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags a log call whose message is built with string interpolation or <c>string.Format</c>
/// instead of a message template with named placeholders.
/// </summary>
/// <remarks>
/// A log message template is the only thing that lets a structured sink tell one event from
/// another: <c>"User {UserId} was denied"</c> is one event with a searchable field, while
/// <c>$"User {userId} was denied"</c> is a new, unique message string per user. Grouping,
/// alerting on a rate, and filtering on a value all stop working, and the interpolation is
/// evaluated even when the level is disabled.
///
/// Recognised by shape rather than by reference, so it covers <c>ILogger</c>, Serilog's static
/// <c>Log</c> and any wrapper of your own: the receiver's type name has to contain "Logger" or
/// be exactly "Log". Add your own with:
/// <code>
///     dotnet_code_quality.AVIG0021.additional_logger_types = Telemetry,AuditTrail
/// </code>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LogMessageTemplateAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0021";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Log message built instead of templated",
        messageFormat: "Log message built with {0} — use a message template with named placeholders and pass the values as arguments",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "String interpolation and string.Format produce a finished message string, so a structured logging sink sees a different message for every value and cannot group, filter or alert on the events. Pass a constant template with named placeholders and let the logger bind the values. Additional logger types can be recognised through the dotnet_code_quality.AVIG0021.additional_logger_types option.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    private const string AdditionalLoggerTypesOption = "dotnet_code_quality.AVIG0021.additional_logger_types";
    private const string LoggerTypeNameFragment = "Logger";
    private const string SerilogStaticTypeName = "Log";
    private const string StringTypeName = "String";
    private const string FormatMethodName = "Format";
    private const string InterpolationDescription = "string interpolation";
    private const string FormatDescription = "string.Format";

    private static readonly char[] Separators = { ',' };

    /// <summary>
    /// The log-level members shared by ILogger extension methods, Serilog and NLog. Names only —
    /// binding to a framework would defeat the point of a project-independent rule.
    /// </summary>
    private static readonly ImmutableHashSet<string> LogMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Log",
        "LogTrace",
        "LogDebug",
        "LogInformation",
        "LogWarning",
        "LogError",
        "LogCritical",
        "Verbose",
        "Trace",
        "Debug",
        "Information",
        "Info",
        "Warning",
        "Warn",
        "Error",
        "Critical",
        "Fatal");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || LogMethodNames.Contains(memberAccess.Name.Identifier.ValueText) == false)
        {
            return;
        }

        if (IsLoggerReceiver(context, memberAccess.Expression) == false)
        {
            return;
        }

        IMethodSymbol? method = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol as IMethodSymbol;

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            string? description = DescribeBuiltMessage(context, argument.Expression);
            if (description == null)
            {
                continue;
            }

            if (IsMessageTemplateArgument(method, invocation, argument) == false)
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, argument.Expression.GetLocation(), description));
        }
    }

    /// <summary>
    /// Returns how the message was built, or null when the argument is an ordinary value. A
    /// constant interpolated string is left alone: it has no holes to turn into fields.
    /// </summary>
    private static string? DescribeBuiltMessage(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            bool hasHole = interpolated.Contents.Any(content => content is InterpolationSyntax);
            bool isConstant = context.SemanticModel.GetConstantValue(interpolated, context.CancellationToken).HasValue;

            return hasHole == true && isConstant == false ? InterpolationDescription : null;
        }

        if (expression is InvocationExpressionSyntax invocation
            && context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method
            && method.Name == FormatMethodName
            && method.ContainingType?.SpecialType == SpecialType.System_String)
        {
            return FormatDescription;
        }

        return null;
    }

    /// <summary>
    /// True when the argument lands on the template parameter rather than on the params array.
    /// <c>LogInformation("User {Name}", $"{first} {last}")</c> is odd but not this rule's problem.
    /// When the call does not bind — a stub, a typo elsewhere in the file — every candidate
    /// argument is treated as the template, which is what the shape almost always is.
    /// </summary>
    private static bool IsMessageTemplateArgument(IMethodSymbol? method, InvocationExpressionSyntax invocation, ArgumentSyntax argument)
    {
        if (method == null)
        {
            return true;
        }

        IParameterSymbol? parameter = ResolveParameter(method, invocation, argument);
        if (parameter == null)
        {
            return true;
        }

        return parameter.IsParams == false && parameter.Type.SpecialType == SpecialType.System_String;
    }

    private static IParameterSymbol? ResolveParameter(IMethodSymbol method, InvocationExpressionSyntax invocation, ArgumentSyntax argument)
    {
        string? argumentName = argument.NameColon?.Name.Identifier.ValueText;
        if (argumentName != null)
        {
            return method.Parameters.FirstOrDefault(parameter => parameter.Name == argumentName);
        }

        int index = invocation.ArgumentList.Arguments.IndexOf(argument);
        if (index < 0)
        {
            return null;
        }

        if (index < method.Parameters.Length)
        {
            return method.Parameters[index];
        }

        IParameterSymbol? last = method.Parameters.LastOrDefault();

        return last?.IsParams == true ? last : null;
    }

    /// <summary>
    /// Matches on the receiver's type name: anything containing "Logger", the bare "Log" that
    /// Serilog's static entry point uses, and whatever the consumer adds through configuration.
    /// </summary>
    private static bool IsLoggerReceiver(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver)
    {
        ITypeSymbol? receiverType = context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type;
        string? typeName = receiverType?.Name;
        if (typeName == null && context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is INamedTypeSymbol staticType)
        {
            // Static entry points such as Serilog's Log.Information: the receiver is a type, not a value.
            typeName = staticType.Name;
        }

        if (typeName == null)
        {
            return false;
        }

        if (typeName.IndexOf(LoggerTypeNameFragment, StringComparison.Ordinal) >= 0
            || typeName == SerilogStaticTypeName)
        {
            return true;
        }

        return ReadAdditionalLoggerTypes(context).Contains(typeName, StringComparer.Ordinal);
    }

    private static ImmutableArray<string> ReadAdditionalLoggerTypes(SyntaxNodeAnalysisContext context)
    {
        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Node.SyntaxTree);
        if (options.TryGetValue(AdditionalLoggerTypesOption, out string? configured) == false
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
