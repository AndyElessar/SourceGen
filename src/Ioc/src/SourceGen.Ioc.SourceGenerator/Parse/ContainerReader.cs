using Microsoft.CodeAnalysis.Text;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>A type that contains the container class, for the generated partial declarations.</summary>
/// <param name="Keyword">The declaration keyword: <c>class</c>, <c>struct</c>, <c>record</c>, <c>record struct</c>, or <c>interface</c>.</param>
/// <param name="Name">The name of the type.</param>
internal sealed record ContainingType(string Keyword, string Name);

/// <summary>A class with <c>IocContainerAttribute</c>. It holds no position, so a move of code does not change it.</summary>
/// <param name="Type">The container class.</param>
/// <param name="Namespace">The namespace, or <see langword="null"/> for the global namespace.</param>
/// <param name="ContainingTypes">The containing types, from the outermost.</param>
/// <param name="Name">The name of the class.</param>
/// <param name="IntegrateServiceProvider">The <c>IntegrateServiceProvider</c> property.</param>
/// <param name="ExplicitOnly">The <c>ExplicitOnly</c> property.</param>
/// <param name="HasIncludeTags">Whether <c>IncludeTags</c> is not empty.</param>
internal sealed record ContainerModel(
    NamedTypeRef Type,
    string? Namespace,
    ImmutableEquatableArray<ContainingType> ContainingTypes,
    string Name,
    bool IntegrateServiceProvider,
    bool ExplicitOnly,
    bool HasIncludeTags)
{
    /// <summary>Gets the hint name of the generated file: the namespace, the containing types, and the name.</summary>
    public string HintName => string.Join(".", [.. Namespace is null ? [] : new[] { Namespace }, .. ContainingTypes.Select(static t => t.Name), Name]) + ".g.cs";
}

/// <summary>Where a container is declared, for the diagnostics of its full check.</summary>
/// <param name="Type">The container class.</param>
/// <param name="FilePath">The path of the syntax tree.</param>
/// <param name="Span">The span of the identifier of the class.</param>
internal sealed record ContainerSite(NamedTypeRef Type, string FilePath, TextSpan Span);

/// <summary>What one class with <c>IocContainerAttribute</c> gives.</summary>
/// <param name="Container">The container, or <see langword="null"/> when it is not valid.</param>
/// <param name="Site">The site, or <see langword="null"/> when it is not valid.</param>
/// <param name="Diagnostic">SGIOC107, when it is not valid.</param>
internal sealed record ContainerResult(ContainerModel? Container, ContainerSite? Site, DiagnosticInfo? Diagnostic);

/// <summary>Reads <c>IocContainerAttribute</c> (Container.Output.spec, "Container class").</summary>
internal static class ContainerReader
{
    /// <summary>The metadata name of <c>IocContainerAttribute</c>.</summary>
    public const string Attribute = "SourceGen.Ioc.IocContainerAttribute";

    /// <summary>Reads the container class.</summary>
    /// <param name="context">The context of the attribute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The container, or SGIOC107.</returns>
    public static ContainerResult Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        INamedTypeSymbol type = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        string? reason = type.Arity > 0 ? "it is generic"
            : ContainingTypes(type).Any(static t => t.Arity > 0) ? "it is nested in a generic type"
            : null;
        if(reason is not null || TypeRefFactory.Create(type, context.SemanticModel.Compilation) is not NamedTypeRef typeRef)
        {
            DiagnosticInfo? diagnostic = reason is null || attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not { } syntax
                ? null
                : DiagnosticInfo.Create(Descriptors.UnsupportedOutputClass.Id, syntax, type.ToDisplayString(), "container", reason);
            return new(null, null, diagnostic);
        }

        ContainerModel container = new(
            typeRef,
            type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
            ContainingTypes(type).Select(static t => new ContainingType(Keyword(t), t.Name)).ToImmutableEquatableArray(),
            type.Name,
            Named(attribute, "IntegrateServiceProvider") is not { Value: false },
            Named(attribute, "ExplicitOnly") is { Value: true },
            Named(attribute, "IncludeTags") is { Kind: TypedConstantKind.Array, Values.Length: > 0 });
        ClassDeclarationSyntax declaration = (ClassDeclarationSyntax)context.TargetNode;
        return new(container, new ContainerSite(typeRef, declaration.SyntaxTree.FilePath, declaration.Identifier.Span), null);
    }

    private static TypedConstant? Named(AttributeData attribute, string name)
    {
        foreach(KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if(argument.Key == name)
            {
                return argument.Value;
            }
        }

        return null;
    }

    /// <summary>The containing types, from the outermost.</summary>
    private static List<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol type)
    {
        List<INamedTypeSymbol> types = [];
        for(INamedTypeSymbol? outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            types.Add(outer);
        }

        types.Reverse();
        return types;
    }

    private static string Keyword(INamedTypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Interface } => "interface",
        { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Struct } => "struct",
        _ => "class",
    };
}
