namespace SourceGen.Ioc.SourceGenerator.Models;

internal static class Constants
{
    public const string IocRegisterAttributeFullName = "SourceGen.Ioc.IocRegisterAttribute";
    public const string IocRegisterAttributeFullName_T1 = "SourceGen.Ioc.IocRegisterAttribute`1";
    public const string IocRegisterAttributeFullName_T2 = "SourceGen.Ioc.IocRegisterAttribute`2";
    public const string IocRegisterAttributeFullName_T3 = "SourceGen.Ioc.IocRegisterAttribute`3";
    public const string IocRegisterAttributeFullName_T4 = "SourceGen.Ioc.IocRegisterAttribute`4";
    public const string IocRegisterForAttributeFullName = "SourceGen.Ioc.IocRegisterForAttribute";
    public const string IocRegisterForAttributeFullName_T1 = "SourceGen.Ioc.IocRegisterForAttribute`1";
    public const string IocRegisterDefaultsAttributeFullName = "SourceGen.Ioc.IocRegisterDefaultsAttribute";
    public const string IocRegisterDefaultsAttributeFullName_T1 = "SourceGen.Ioc.IocRegisterDefaultsAttribute`1";
    public const string IocImportModuleAttributeFullName = "SourceGen.Ioc.IocImportModuleAttribute";
    public const string IocImportModuleAttributeFullName_T1 = "SourceGen.Ioc.IocImportModuleAttribute`1";
    public const string IocDiscoverAttributeFullName = "SourceGen.Ioc.IocDiscoverAttribute";
    public const string IocDiscoverAttributeFullName_T1 = "SourceGen.Ioc.IocDiscoverAttribute`1";
    public const string IocInjectAttributeFullName = "SourceGen.Ioc.IocInjectAttribute";
    public const string IocGenericFactoryAttributeFullName = "SourceGen.Ioc.IocGenericFactoryAttribute";

    /// <summary>
    /// The MSBuild property name for the root namespace.
    /// </summary>
    public const string RootNamespaceProperty = "build_property.RootNamespace";

    /// <summary>
    /// The MSBuild property name for customizing the generated method name.
    /// </summary>
    /// <remarks>
    /// Usage in .csproj:
    /// <code>
    /// &lt;PropertyGroup&gt;
    ///     &lt;SourceGenIocName&gt;CustomName&lt;/SourceGenIocName&gt;
    /// &lt;/PropertyGroup&gt;
    /// &lt;ItemGroup&gt;
    ///     &lt;CompilerVisibleProperty Include="SourceGenIocName" /&gt;
    /// &lt;/ItemGroup&gt;
    /// </code>
    /// </remarks>
    public const string SourceGenIocNameProperty = "build_property.SourceGenIocName";

    public const string Category_Usage = "Usage";
    public const string Category_Design = "Design";

    extension(ServiceLifetime lifetime)
    {
        public string Name =>
            lifetime switch
            {
                ServiceLifetime.Singleton => "Singleton",
                ServiceLifetime.Scoped => "Scoped",
                ServiceLifetime.Transient => "Transient",
                _ => lifetime.ToString()
            };
    }
}
