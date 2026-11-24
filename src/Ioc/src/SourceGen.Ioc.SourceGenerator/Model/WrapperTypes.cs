namespace SourceGen.Ioc.Model;

/// <summary>The wrapper types of the grammar (Analysis.Model.spec, "Grammar of wrapper types").</summary>
internal static class WrapperTypes
{
    private static readonly HashSet<string> Definitions =
    [
        "System.Lazy`1",
        "System.Threading.Tasks.Task`1",
        "System.Collections.Generic.KeyValuePair`2",
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IReadOnlyCollection`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IDictionary`2",
        "System.Collections.Generic.IReadOnlyDictionary`2",
        "System.Collections.Generic.Dictionary`2",
        .. Enumerable.Range(1, 17).Select(static n => $"System.Func`{n}"),
    ];

    /// <summary>Gets a value indicating whether <paramref name="type"/> is a wrapper type, by its original definition only.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it is a wrapper type. A one-dimensional array is a collection.</returns>
    public static bool IsWrapper(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => array.Rank == 1,
        INamedTypeSymbol { ContainingType: null, ContainingNamespace: { } ns } named => Definitions.Contains($"{ns.ToDisplayString()}.{named.MetadataName}"),
        _ => false,
    };

    /// <summary>Gets a value indicating whether <paramref name="type"/> is a wrapper type, by its original definition only.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it is a wrapper type. A one-dimensional array is a collection.</returns>
    public static bool IsWrapper(TypeRef type) => type switch
    {
        ArrayTypeRef array => array.Rank == 1,
        NamedTypeRef named => Definitions.Contains(named.MetadataName),
        _ => false,
    };
}

/// <summary>The built-in services (Analysis.Model.spec, "Built-in services").</summary>
internal static class BuiltInServices
{
    /// <summary>The metadata name of <c>IServiceProvider</c>.</summary>
    public const string ServiceProvider = "System.IServiceProvider";

    private static readonly HashSet<string> Names =
    [
        ServiceProvider,
        "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        "Microsoft.Extensions.DependencyInjection.IServiceProviderIsService",
        "Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService",
    ];

    /// <summary>Gets a value indicating whether <paramref name="type"/> is a built-in service type.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it is a built-in service type.</returns>
    public static bool IsBuiltIn(ITypeSymbol type) => type is INamedTypeSymbol { ContainingType: null, Arity: 0 } && Names.Contains(type.ToDisplayString());

    /// <summary>
    /// Gets a value indicating whether the built-in service with <paramref name="metadataName"/> is the root in the
    /// container: <c>IServiceScopeFactory</c>, <c>IServiceProviderIsService</c>, <c>IServiceProviderIsKeyedService</c>.
    /// </summary>
    /// <param name="metadataName">The full metadata name of a non-generic type.</param>
    /// <returns>Whether the container resolves it to the root.</returns>
    public static bool IsRootService(string metadataName) => metadataName != ServiceProvider && Names.Contains(metadataName);

    /// <summary>Gets a value indicating whether <paramref name="type"/> is <c>IServiceProvider</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>Whether it is <c>IServiceProvider</c>.</returns>
    public static bool IsServiceProvider(TypeRef type) => type is NamedTypeRef { MetadataName: ServiceProvider, Arguments: null };
}
