namespace SourceGen.Ioc.TestAot.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected: both containers register the same services

/// <summary>Service with two non-keyed registrations and a keyed one, injected as collections.</summary>
public interface IInjectedPlugin;

internal sealed class FirstInjectedPlugin : IInjectedPlugin;

internal sealed class SecondInjectedPlugin : IInjectedPlugin;

internal sealed class KeyedInjectedPlugin : IInjectedPlugin;

/// <summary>Consumer of every collection shape of <see cref="IInjectedPlugin"/>.</summary>
public sealed class InjectedPluginConsumer(
    IEnumerable<IInjectedPlugin> enumerable,
    IReadOnlyCollection<IInjectedPlugin> readOnlyCollection,
    ICollection<IInjectedPlugin> collection,
    IReadOnlyList<IInjectedPlugin> readOnlyList,
    IList<IInjectedPlugin> list,
    IInjectedPlugin[] array,
    Lazy<IEnumerable<IInjectedPlugin>> lazyEnumerable,
    Func<IEnumerable<IInjectedPlugin>> enumerableFactory,
    IEnumerable<Lazy<IInjectedPlugin>> lazyPlugins,
    Func<IInjectedPlugin> pluginFactory,
    [IocInject(Key = "keyed")] IEnumerable<IInjectedPlugin> keyedEnumerable,
    [IocInject(Key = "keyed")] IInjectedPlugin[] keyedArray)
{
    public IEnumerable<IInjectedPlugin> Enumerable { get; } = enumerable;
    public IReadOnlyCollection<IInjectedPlugin> ReadOnlyCollection { get; } = readOnlyCollection;
    public ICollection<IInjectedPlugin> Collection { get; } = collection;
    public IReadOnlyList<IInjectedPlugin> ReadOnlyList { get; } = readOnlyList;
    public IList<IInjectedPlugin> List { get; } = list;
    public IInjectedPlugin[] Array { get; } = array;
    public Lazy<IEnumerable<IInjectedPlugin>> LazyEnumerable { get; } = lazyEnumerable;
    public Func<IEnumerable<IInjectedPlugin>> EnumerableFactory { get; } = enumerableFactory;
    public IEnumerable<Lazy<IInjectedPlugin>> LazyPlugins { get; } = lazyPlugins;
    public Func<IInjectedPlugin> PluginFactory { get; } = pluginFactory;
    public IEnumerable<IInjectedPlugin> KeyedEnumerable { get; } = keyedEnumerable;
    public IInjectedPlugin[] KeyedArray { get; } = keyedArray;

    [IocInject]
    public IEnumerable<IInjectedPlugin> InjectedProperty { get; set; } = null!;
}

/// <summary>Container integrating the fallback provider, whose collection dependencies merge the fallback services.</summary>
[IocRegisterFor<FirstInjectedPlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInjectedPlugin)])]
[IocRegisterFor<SecondInjectedPlugin>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IInjectedPlugin)])]
[IocRegisterFor<KeyedInjectedPlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInjectedPlugin)], Key = "keyed")]
[IocRegisterFor<InjectedPluginConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class InjectedCollectionContainer
{
    public partial IEnumerable<IInjectedPlugin> GetPlugins();

    public partial IInjectedPlugin[] PluginArray { get; }
}

/// <summary>The switch-statement counterpart of <see cref="InjectedCollectionContainer"/>.</summary>
[IocRegisterFor<FirstInjectedPlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInjectedPlugin)])]
[IocRegisterFor<SecondInjectedPlugin>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IInjectedPlugin)])]
[IocRegisterFor<KeyedInjectedPlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInjectedPlugin)], Key = "keyed")]
[IocRegisterFor<InjectedPluginConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true, UseSwitchStatement = true)]
public sealed partial class InjectedCollectionSwitchContainer
{
    public partial IEnumerable<IInjectedPlugin> GetPlugins();

    public partial IInjectedPlugin[] PluginArray { get; }
}

/// <summary>Service with two registrations in <see cref="IntegratedPluginModule"/>.</summary>
public interface IIntegratedModulePlugin;

internal sealed class FirstIntegratedModulePlugin : IIntegratedModulePlugin;

internal sealed class SecondIntegratedModulePlugin : IIntegratedModulePlugin;

/// <summary>Module integrating the fallback provider, imported by <see cref="PluginModuleImporter"/>.</summary>
[IocRegisterFor<FirstIntegratedModulePlugin>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IIntegratedModulePlugin)])]
[IocRegisterFor<SecondIntegratedModulePlugin>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IIntegratedModulePlugin)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class IntegratedPluginModule;

/// <summary>Consumer of the collections of the imported modules' services.</summary>
public sealed class ModulePluginConsumer(
    IEnumerable<INonIntegratedModulePlugin> nonIntegrated,
    IReadOnlyList<IIntegratedModulePlugin> integrated)
{
    public IEnumerable<INonIntegratedModulePlugin> NonIntegrated { get; } = nonIntegrated;
    public IReadOnlyList<IIntegratedModulePlugin> Integrated { get; } = integrated;
}

/// <summary>
/// Container importing a module without fallback integration (from another assembly) and one with it: their collections
/// merge the fallback services once, through this container.
/// </summary>
[IocImportModule<NonIntegratedPluginModule>]
[IocImportModule<IntegratedPluginModule>]
[IocRegisterFor<ModulePluginConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class PluginModuleImporter;

/// <summary>Module integrating the fallback provider that imports <see cref="IntegratedPluginModule"/>.</summary>
[IocImportModule<IntegratedPluginModule>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class NestedPluginModule;

/// <summary>Container importing <see cref="IntegratedPluginModule"/> through <see cref="NestedPluginModule"/>.</summary>
[IocImportModule<NestedPluginModule>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class NestedPluginModuleImporter;
