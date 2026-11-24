using Microsoft.CodeAnalysis.Text;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Parse;

/// <summary>A type that contains the container class, for the generated partial declarations.</summary>
/// <param name="Keyword">The declaration keyword: <c>class</c>, <c>struct</c>, <c>record</c>, <c>record struct</c>, or <c>interface</c>.</param>
/// <param name="Name">The name of the type.</param>
internal sealed record ContainingType(string Keyword, string Name);

/// <summary>A partial member of the container class that the generator implements (Annotations.spec, "Partial accessor").</summary>
/// <param name="Name">The name of the member.</param>
/// <param name="IsProperty">Whether it is a property. Otherwise it is a method without parameters.</param>
/// <param name="Modifiers">The modifiers of the declaration, which the implementation part repeats.</param>
/// <param name="ReturnType">The return type as declared, with its nullable annotation.</param>
/// <param name="Type">The requested type.</param>
/// <param name="Key">The key that <c>[IocInject]</c> gives, or <see langword="null"/>. <c>InheritKey</c> counts as no key.</param>
/// <param name="HasGeneratedCode">
/// Whether the definition part already has <c>GeneratedCodeAttribute</c>. The attributes of the two parts merge, and the
/// attribute does not allow more than one.
/// </param>
/// <param name="IsOptional">
/// Whether the return type is a reference type annotated as nullable: an optional dependency. A nullable value type is
/// a different type and stays required (Annotations.spec, "Partial accessor").
/// </param>
internal sealed record PartialAccessor(string Name, bool IsProperty, string Modifiers, string ReturnType, TypeRef Type, KeyRef? Key, bool HasGeneratedCode, bool IsOptional);

/// <summary>A class with <c>IocContainerAttribute</c>. It holds no position, so a move of code does not change it.</summary>
/// <param name="Type">The container class.</param>
/// <param name="Namespace">The namespace, or <see langword="null"/> for the global namespace.</param>
/// <param name="ContainingTypes">The containing types, from the outermost.</param>
/// <param name="Name">The name of the class.</param>
/// <param name="IntegrateServiceProvider">The <c>IntegrateServiceProvider</c> property.</param>
/// <param name="ExplicitOnly">The <c>ExplicitOnly</c> property.</param>
/// <param name="HasIncludeTags">Whether <c>IncludeTags</c> is not empty.</param>
/// <param name="ThreadSafeStrategy">The value of <c>ThreadSafeStrategy</c>: 0 None, 1 Lock, 2 CompareExchange.</param>
/// <param name="EagerResolveOptions">The flags of <c>EagerResolveOptions</c>: 1 Singleton, 2 Scoped.</param>
/// <param name="Accessors">The partial accessors, in the order of <c>GetMembers()</c>.</param>
/// <param name="HasServiceProviderBuilder">
/// Whether the compilation references <c>Microsoft.Extensions.DependencyInjection</c>, which supplies
/// <c>BuildServiceProvider</c>. The container then has <c>ServiceProviderFactory</c> when it has a fallback provider.
/// </param>
internal sealed record ContainerModel(
    NamedTypeRef Type,
    string? Namespace,
    ImmutableEquatableArray<ContainingType> ContainingTypes,
    string Name,
    bool IntegrateServiceProvider,
    bool ExplicitOnly,
    bool HasIncludeTags,
    int ThreadSafeStrategy,
    int EagerResolveOptions,
    ImmutableEquatableArray<PartialAccessor> Accessors,
    bool HasServiceProviderBuilder)
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
/// <param name="Diagnostics">SGIOC107 when it is not valid, and the diagnostics of the keys of partial accessors.</param>
internal sealed record ContainerResult(ContainerModel? Container, ContainerSite? Site, ImmutableEquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>Reads <c>IocContainerAttribute</c> (Container.Output.spec, "Container class").</summary>
internal static class ContainerReader
{
    /// <summary>The metadata name of <c>IocContainerAttribute</c>.</summary>
    public const string Attribute = "SourceGen.Ioc.IocContainerAttribute";

    /// <summary>The type that supplies <c>BuildServiceProvider</c>, in the <c>Microsoft.Extensions.DependencyInjection</c> package.</summary>
    private const string ServiceProviderBuilder = "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions";

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
            return new(null, null, diagnostic is null ? [] : [diagnostic]);
        }

        List<DiagnosticInfo> diagnostics = [];
        ContainerModel container = new(
            typeRef,
            type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
            ContainingTypes(type).Select(static t => new ContainingType(Keyword(t), t.Name)).ToImmutableEquatableArray(),
            type.Name,
            Named(attribute, "IntegrateServiceProvider") is not { Value: false },
            Named(attribute, "ExplicitOnly") is { Value: true },
            Named(attribute, "IncludeTags") is { Kind: TypedConstantKind.Array, Values.Length: > 0 },
            Named(attribute, "ThreadSafeStrategy") is { Value: int strategy } ? strategy : 1,
            Named(attribute, "EagerResolveOptions") is { Value: int eager } ? eager : 1,
            Accessors(type, context.SemanticModel.Compilation, diagnostics, cancellationToken),
            context.SemanticModel.Compilation.GetTypeByMetadataName(ServiceProviderBuilder) is not null);
        ClassDeclarationSyntax declaration = (ClassDeclarationSyntax)context.TargetNode;
        return new(container, new ContainerSite(typeRef, declaration.SyntaxTree.FilePath, declaration.Identifier.Span), diagnostics.ToImmutableEquatableArray());
    }

    private static readonly SymbolDisplayFormat ReturnTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// The partial members without an implementation part that have the shape of a partial accessor. Another partial
    /// member is not implemented, and the compiler reports it (Annotations.spec, "Partial accessor"). An accessor with an
    /// invalid key is reported and not implemented.
    /// </summary>
    private static ImmutableEquatableArray<PartialAccessor> Accessors(INamedTypeSymbol type, Compilation compilation, List<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
    {
        List<PartialAccessor> accessors = [];
        foreach(ISymbol member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            (ITypeSymbol? returnType, bool isProperty, NullableAnnotation annotation) = member switch
            {
                IMethodSymbol { IsPartialDefinition: true, PartialImplementationPart: null, IsStatic: false, IsGenericMethod: false, Parameters.Length: 0, ReturnsVoid: false, RefKind: RefKind.None } method
                    => (method.ReturnType, false, method.ReturnNullableAnnotation),
                IPropertySymbol { IsPartialDefinition: true, PartialImplementationPart: null, IsStatic: false, IsIndexer: false, SetMethod: null, GetMethod: not null, RefKind: RefKind.None } property
                    => (property.Type, true, property.NullableAnnotation),
                _ => (null, false, NullableAnnotation.None),
            };
            if(returnType is null
                || TypeRefFactory.Create(returnType, compilation) is not { } typeRef
                || member.DeclaringSyntaxReferences is not [SyntaxReference reference, ..]
                || reference.GetSyntax(cancellationToken) is not MemberDeclarationSyntax declaration)
            {
                continue;
            }

            bool hasGeneratedCode = member.GetAttributes().Any(static a => a.AttributeClass is { Name: "GeneratedCodeAttribute" } c && c.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute");
            SiteKey key = SiteKeyReader.Read(member.GetAttributes(), member is IPropertySymbol accessorProperty ? accessorProperty.Type : ((IMethodSymbol)member).ReturnType, member.Name, declaration, compilation, cancellationToken);
            if(key.Errors.Count > 0)
            {
                diagnostics.AddRange(key.Errors);
                continue;
            }

            accessors.Add(new PartialAccessor(
                member.Name,
                isProperty,
                string.Join(" ", declaration.Modifiers.Select(static m => m.Text)),
                returnType.ToDisplayString(ReturnTypeFormat),
                typeRef,
                key.Mode == SiteKeyMode.Specified ? key.Key : null,
                hasGeneratedCode,
                OptionalDependency.IsNullableReference(returnType, annotation)));
        }

        return accessors.ToImmutableEquatableArray();
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
