namespace SourceGen.Ioc;

/// <summary>
/// Holds cached IoC attribute type symbols for efficient comparison in analyzers.
/// </summary>
internal sealed class IocAttributeSymbols
{
    public INamedTypeSymbol? IocContainerAttribute { get; }
    public INamedTypeSymbol? IocRegisterAttribute { get; }
    public INamedTypeSymbol? IocRegisterAttribute_T1 { get; }
    public INamedTypeSymbol? IocRegisterForAttribute { get; }
    public INamedTypeSymbol? IocRegisterForAttribute_T1 { get; }
    public INamedTypeSymbol? IocRegisterDefaultsAttribute { get; }
    public INamedTypeSymbol? IocRegisterDefaultsAttribute_T1 { get; }
    public INamedTypeSymbol? IocImportModuleAttribute { get; }
    public INamedTypeSymbol? IocImportModuleAttribute_T1 { get; }

    public IocAttributeSymbols(Compilation compilation)
    {
        IocContainerAttribute = compilation.GetTypeByMetadataName(Constants.IocContainerAttributeFullName);
        IocRegisterAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterAttributeFullName);
        IocRegisterAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterAttributeFullName_T1);
        IocRegisterForAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterForAttributeFullName);
        IocRegisterForAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterForAttributeFullName_T1);
        IocRegisterDefaultsAttribute = compilation.GetTypeByMetadataName(Constants.IocRegisterDefaultsAttributeFullName);
        IocRegisterDefaultsAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocRegisterDefaultsAttributeFullName_T1);
        IocImportModuleAttribute = compilation.GetTypeByMetadataName(Constants.IocImportModuleAttributeFullName);
        IocImportModuleAttribute_T1 = compilation.GetTypeByMetadataName(Constants.IocImportModuleAttributeFullName_T1);
    }

    /// <summary>
    /// Checks if any IoC registration attribute is available in the compilation.
    /// </summary>
    public bool HasAnyRegistrationAttribute =>
        IocRegisterAttribute is not null
        || IocRegisterAttribute_T1 is not null
        || IocRegisterForAttribute is not null
        || IocRegisterForAttribute_T1 is not null;

    /// <summary>
    /// Checks if the IocContainerAttribute is available.
    /// </summary>
    public bool HasContainerAttribute => IocContainerAttribute is not null;
}