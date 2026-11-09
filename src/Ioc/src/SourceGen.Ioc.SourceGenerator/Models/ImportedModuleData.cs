namespace SourceGen.Ioc;

/// <summary>
/// Represents a module container imported with [IocImportModule].
/// </summary>
/// <param name="Type">The module container type.</param>
/// <param name="AcceptsFallbackProvider">Whether the module has the generated constructor taking a fallback <see cref="IServiceProvider"/>.</param>
/// <param name="SharesFallbackScope">Whether the module implements <c>IIocFallbackScopeOwner</c>, so its scopes can share the fallback scope of the importing container scope.</param>
internal readonly record struct ImportedModuleData(
    TypeData Type,
    bool AcceptsFallbackProvider,
    bool SharesFallbackScope);
