namespace SourceGen.Ioc.TestAot.Container;

/// <summary>A service with several registrations, for collections.</summary>
public interface IPlugin;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
public sealed class FirstPlugin : IPlugin;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IPlugin)])]
public sealed class SecondPlugin : IPlugin;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)], Key = "extra")]
public sealed class ExtraPlugin : IPlugin;

public static class StoreAliases
{
    /// <summary>A symbol key that is equal at run time to the registered value key <c>"a"</c> of <see cref="Store"/>.</summary>
    public static readonly string A = "a";
}

/// <summary>A keyed collection whose key only this dependency site asks for, equal at run time to a registered key.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class StoreAliasConsumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(StoreAliases.A))] IReadOnlyList<IStore> stores)
{
    public IReadOnlyList<IStore> Stores => stores;
}

/// <summary>A keyed collection of wrappers whose key is equal at run time to a registered key.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class StoreWrapperAliasConsumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(StoreAliases.A))] IEnumerable<Lazy<IStore>> stores)
{
    public IEnumerable<Lazy<IStore>> Stores => stores;
}

/// <summary>Collections whose element is the service itself: each collection type, a key, and all keyed registrations.</summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class PluginHost(
    IEnumerable<IPlugin> all,
    IReadOnlyList<IPlugin> list,
    IPlugin[] array,
    [IocInject(Key = "extra")] IList<IPlugin> extra,
    [IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] IReadOnlyCollection<IPlugin> keyed)
{
    public IEnumerable<IPlugin> All => all;

    public IReadOnlyList<IPlugin> List => list;

    public IPlugin[] Array => array;

    public IList<IPlugin> Extra => extra;

    public IReadOnlyCollection<IPlugin> Keyed => keyed;
}
