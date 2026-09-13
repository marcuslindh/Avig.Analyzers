using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Avig.Analyzers;

/// <summary>
/// Flags a publicly reachable method or property that hands out a mutable collection —
/// <c>List&lt;T&gt;</c>, <c>Dictionary&lt;K,V&gt;</c>, <c>HashSet&lt;T&gt;</c> or an array.
/// </summary>
/// <remarks>
/// Two separate problems, one signature. The caller can add to and remove from a collection it
/// never owned, mutating state behind the declaring type's back with no way for it to notice.
/// And the concrete type is now part of the contract: switching the field to an array, an
/// ImmutableArray or a lazy sequence later is a binary-breaking change, even though nothing
/// about the meaning of the member changed.
///
/// Returning the read-only interface costs nothing — <c>List&lt;T&gt;</c> already implements
/// <c>IReadOnlyList&lt;T&gt;</c>, so the body stays as it is. <c>Task&lt;List&lt;T&gt;&gt;</c>
/// counts as much as a bare <c>List&lt;T&gt;</c>. Overrides and interface implementations are
/// exempt, since the type is decided elsewhere, and so is <c>byte[]</c>, which is the idiomatic
/// currency of hashes, streams and wire payloads.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableCollectionReturnAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "AVIG0022";

    public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        title: "Public member exposes a mutable collection",
        messageFormat: "'{0}' exposes '{1}' — return '{2}' instead",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A public member that returns List<T>, Dictionary<K,V>, HashSet<T> or an array lets callers mutate a collection they do not own, and freezes the concrete type into the contract so it cannot be changed without breaking them. Return the matching read-only interface; the existing body already satisfies it.",
        helpLinkUri: AvigRuleDocs.For(DiagnosticId));

    /// <summary>Carries the suggested replacement type to the code fix so it is decided in one place.</summary>
    internal const string ReplacementPropertyKey = "Replacement";

    private const string GenericCollectionsNamespace = "System.Collections.Generic";
    private const string ListTypeName = "List";
    private const string DictionaryTypeName = "Dictionary";
    private const string HashSetTypeName = "HashSet";
    private const string ReadOnlyListTypeName = "IReadOnlyList";
    private const string ReadOnlyDictionaryTypeName = "IReadOnlyDictionary";
    private const string ReadOnlyCollectionTypeName = "IReadOnlyCollection";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
        IMethodSymbol? method = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (method == null || IsFixableMember(method) == false)
        {
            return;
        }

        Report(context, declaration.ReturnType, method.Name);
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        PropertyDeclarationSyntax declaration = (PropertyDeclarationSyntax)context.Node;
        IPropertySymbol? property = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
        if (property == null || IsFixableMember(property) == false)
        {
            return;
        }

        if (property.GetMethod == null)
        {
            // Set-only: the collection travels inward, so nothing is being exposed.
            return;
        }

        Report(context, declaration.Type, property.Name);
    }

    private static void Report(SyntaxNodeAnalysisContext context, TypeSyntax declaredType, string memberName)
    {
        TypeSyntax offendingType = UnwrapTaskLike(context, declaredType);
        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(offendingType, context.CancellationToken).Type;

        string? replacement = GetReadOnlyEquivalent(type);
        if (replacement == null)
        {
            return;
        }

        ImmutableDictionary<string, string?> properties = ImmutableDictionary<string, string?>.Empty
            .Add(ReplacementPropertyKey, replacement);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            offendingType.GetLocation(),
            properties,
            memberName,
            type?.ToDisplayString() ?? offendingType.ToString(),
            replacement));
    }

    /// <summary>
    /// Returns the type argument of <c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c>, so an async
    /// member is judged on what it eventually hands over rather than on its wrapper.
    /// </summary>
    private static TypeSyntax UnwrapTaskLike(SyntaxNodeAnalysisContext context, TypeSyntax declaredType)
    {
        GenericNameSyntax? generic = declaredType switch
        {
            GenericNameSyntax name => name,
            QualifiedNameSyntax qualified => qualified.Right as GenericNameSyntax,
            _ => null,
        };

        if (generic == null || generic.TypeArgumentList.Arguments.Count != 1)
        {
            return declaredType;
        }

        ITypeSymbol? type = context.SemanticModel.GetTypeInfo(declaredType, context.CancellationToken).Type;

        return AvigSymbols.IsTaskLike(type) == true ? generic.TypeArgumentList.Arguments[0] : declaredType;
    }

    /// <summary>
    /// Returns the read-only interface that should stand in the signature, or null when the type
    /// is not one of the mutable collections this rule cares about.
    /// </summary>
    private static string? GetReadOnlyEquivalent(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol array)
        {
            // byte[] is the currency of hashes, streams and wire payloads; IReadOnlyList<byte>
            // would be a downgrade at every call site.
            return array.Rank == 1 && array.ElementType.SpecialType != SpecialType.System_Byte
                ? ReadOnlyListTypeName + "<" + array.ElementType.ToDisplayString() + ">"
                : null;
        }

        if (type is not INamedTypeSymbol named
            || named.IsGenericType == false
            || named.ContainingNamespace?.ToDisplayString() != GenericCollectionsNamespace)
        {
            return null;
        }

        string? readOnlyName = named.Name switch
        {
            ListTypeName => ReadOnlyListTypeName,
            DictionaryTypeName => ReadOnlyDictionaryTypeName,
            // IReadOnlySet<T> only exists from .NET 5, and a netstandard2.0 consumer has to be
            // able to apply the fix.
            HashSetTypeName => ReadOnlyCollectionTypeName,
            _ => null,
        };

        if (readOnlyName == null)
        {
            return null;
        }

        return readOnlyName + "<" + string.Join(", ", named.TypeArguments.Select(argument => argument.ToDisplayString())) + ">";
    }

    /// <summary>
    /// True for a member that is publicly reachable and whose type this code is free to change.
    /// An override or an interface implementation is neither: the signature is set elsewhere, and
    /// reporting it would only produce a diagnostic nobody can act on here.
    /// </summary>
    private static bool IsFixableMember(ISymbol member)
    {
        if (IsExternallyVisible(member) == false)
        {
            return false;
        }

        if (member.IsOverride == true)
        {
            return false;
        }

        return ImplementsInterfaceMember(member) == false;
    }

    private static bool IsExternallyVisible(ISymbol member)
    {
        if (IsVisibleAccessibility(member.DeclaredAccessibility) == false)
        {
            return false;
        }

        for (INamedTypeSymbol? containing = member.ContainingType; containing != null; containing = containing.ContainingType)
        {
            if (IsVisibleAccessibility(containing.DeclaredAccessibility) == false)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsVisibleAccessibility(Accessibility accessibility)
    {
        return accessibility == Accessibility.Public
            || accessibility == Accessibility.Protected
            || accessibility == Accessibility.ProtectedOrInternal;
    }

    private static bool ImplementsInterfaceMember(ISymbol member)
    {
        if (member.ContainingType?.TypeKind == TypeKind.Interface)
        {
            return false;
        }

        foreach (INamedTypeSymbol interfaceType in member.ContainingType?.AllInterfaces ?? ImmutableArray<INamedTypeSymbol>.Empty)
        {
            foreach (ISymbol interfaceMember in interfaceType.GetMembers(member.Name))
            {
                ISymbol? implementation = member.ContainingType?.FindImplementationForInterfaceMember(interfaceMember);
                if (SymbolEqualityComparer.Default.Equals(implementation, member) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
