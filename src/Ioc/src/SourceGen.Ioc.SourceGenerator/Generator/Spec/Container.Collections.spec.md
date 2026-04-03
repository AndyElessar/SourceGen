# Collection and Wrapper Types

## Overview

Support for collection resolution when multiple implementations are registered for a service type, and wrapper types like `Lazy<T>`, `Func<T>`, and `KeyValuePair<K, V>`.

## Feature 1: Collection Resolution

Support for collection resolution when multiple implementations are registered for a service type. Collection resolvers are generated and registered in `_localResolvers`.

### Supported Collection Types

|Type|Resolution Method|Notes|
|:---|:---|:---|
|`IEnumerable<T>`|Returns collection expression|Standard MS.DI pattern|
|`IReadOnlyCollection<T>`|Returns array|Supports `Count` property|
|`IReadOnlyList<T>`|Returns array|Supports indexer access|
|`T[]`|Returns array|Native array type|

> **Note**: Collection resolution only generates for service types with multiple distinct implementations (excluding self-registrations).

```csharp
#region Define:
public interface IPlugin;

[IocRegister<IPlugin>(ServiceLifetime.Singleton)]
public class Plugin1 : IPlugin;

[IocRegister<IPlugin>(ServiceLifetime.Singleton)]
public class Plugin2 : IPlugin;

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    #region Service Resolution

    private global::Plugin1? _plugin1;
    private readonly SemaphoreSlim _plugin1Semaphore = new(1, 1);
    private global::Plugin1 GetPlugin1()
    {
        if (_plugin1 is not null) return _plugin1;

        _plugin1Semaphore.Wait();
        try
        {
            if (_plugin1 is not null) return _plugin1;

            var instance = new global::Plugin1();
            _plugin1 = instance;
            return instance;
        }
        finally
        {
            _plugin1Semaphore.Release();
        }
    }

    private global::Plugin2? _plugin2;
    private readonly SemaphoreSlim _plugin2Semaphore = new(1, 1);
    private global::Plugin2 GetPlugin2()
    {
        if (_plugin2 is not null) return _plugin2;

        _plugin2Semaphore.Wait();
        try
        {
            if (_plugin2 is not null) return _plugin2;

            var instance = new global::Plugin2();
            _plugin2 = instance;
            return instance;
        }
        finally
        {
            _plugin2Semaphore.Release();
        }
    }

    // Collection resolver for IEnumerable<IPlugin>
    private global::System.Collections.Generic.IEnumerable<global::IPlugin> GetAllIPlugin() =>
    [
        GetPlugin1(),
        GetPlugin2(),
    ];

    // Array resolver for IReadOnlyCollection<T>, IReadOnlyList<T>, T[]
    private global::IPlugin[] GetAllIPluginArray() =>
    [
        GetPlugin1(),
        GetPlugin2(),
    ];

    #endregion

    // Registered in _localResolvers
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        new(new ServiceIdentifier(typeof(global::Plugin1), KeyedService.AnyKey), static c => c.GetPlugin1()),
        new(new ServiceIdentifier(typeof(global::IPlugin), KeyedService.AnyKey), static c => c.GetPlugin1()),
        new(new ServiceIdentifier(typeof(global::Plugin2), KeyedService.AnyKey), static c => c.GetPlugin2()),
        new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<global::IPlugin>), KeyedService.AnyKey), static c => c.GetAllIPlugin()),
        new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyCollection<global::IPlugin>), KeyedService.AnyKey), static c => c.GetAllIPluginArray()),
        new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyList<global::IPlugin>), KeyedService.AnyKey), static c => c.GetAllIPluginArray()),
        new(new ServiceIdentifier(typeof(global::IPlugin[]), KeyedService.AnyKey), static c => c.GetAllIPluginArray()),
    ];
}
#endregion
```

## Feature 2: Wrapper Type Resolution

When a constructor parameter or injected member uses a wrapper type (`Lazy<T>`, `Func<T>`, `KeyValuePair<K, V>`), the container generates wrapper resolution that delegates to the container's own resolver methods.

### Supported Wrapper Types

| Wrapper Type | Generated Code | Notes |
| :--- | :--- | :--- |
| `Lazy<T>` | `GetLazy_T_Impl()` (dedicated resolver method) | Standalone resolver registered in `_localResolvers` |
| `Func<T>` | `GetFunc_T_Impl()` (dedicated resolver method) | Standalone resolver registered in `_localResolvers` |
| `KeyValuePair<K, V>` | `new KeyValuePair<K, V>(key, GetMyService())` | Key from service key or `default` |
| `IDictionary<K, V>` | `GetServices<KVP<K, V>>().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` | Collection-based dictionary resolution |
| `IReadOnlyDictionary<K, V>` | `GetServices<KVP<K, V>>().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` | Collection-based dictionary resolution |
| `Dictionary<K, V>` | `GetServices<KVP<K, V>>().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` | Collection-based dictionary resolution |

> **Note**: Nested wrappers are supported up to 2 levels. Inner types are still constructed **inline** (no dedicated resolver).
>
> - `Lazy<Func<T>>` → `new Lazy<Func<T>>(() => new Func<T>(() => GetMyService()))`
> - `Func<Lazy<T>>` → `new Func<Lazy<T>>(() => new Lazy<T>(() => GetMyService()))`
> - `Lazy<IEnumerable<T>>` → `new Lazy<IEnumerable<T>>(() => GetServices<T>())`
> - `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` — Resolved via `GetServices<Lazy<T>>()` which uses the wrapper resolver methods
>
> Non-collection outer wrappers (`Lazy<T>`, `Func<T>`) are recursively resolved to arbitrary depth. Collection outer wrappers (`IEnumerable<T>`, etc.) support at most **1 level of inner wrapping** (2 levels total); deeper nesting (e.g., `IEnumerable<Lazy<Func<T>>>`) falls back to `IServiceProvider` resolution via `GetRequiredService(typeof(...))`.
>
> `ValueTask<T>` is **not** a recognized wrapper type in any context. Only `Task<T>` is supported for async-init wrapping. When used as a partial accessor return type: if the target service uses async-init, `SGIOC029` is reported; otherwise `SGIOC021` is reported.

```csharp
#region Define:
public interface IMyService;

[IocRegister<IMyService>(ServiceLifetime.Singleton)]
public class MyService : IMyService;

[IocRegister(Lifetime = ServiceLifetime.Singleton)]
public class Consumer(Lazy<IMyService> lazyService, Func<IMyService> factory);

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Standalone wrapper resolver methods
    private global::System.Lazy<global::IMyService> GetLazy_IMyService_MyService()
        => new global::System.Lazy<global::IMyService>(() => GetMyService());

    private global::System.Func<global::IMyService> GetFunc_IMyService_MyService()
        => new global::System.Func<global::IMyService>(() => GetMyService());

    private global::Consumer GetConsumer()
    {
        // Direct Lazy/Func resolved via wrapper resolver methods
        var instance = new global::Consumer(
            GetLazy_IMyService_MyService(),
            GetFunc_IMyService_MyService());
        return instance;
    }
}
#endregion
```

### KeyValuePair Collection Resolvers

When consumer dependencies include `KeyValuePair<K, V>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`, or `IEnumerable<KeyValuePair<K, V>>`, the container generates dedicated KVP resolver methods and `_localResolvers` entries so that `GetServices<KeyValuePair<K, V>>()` returns all matching keyed service entries.

**Generated components**:

1. **Individual KVP resolver methods** — One per keyed service matching the `(K, V)` pair:

   ```csharp
   private KeyValuePair<string, IHandler> GetKvp_string_IHandler__h1_()
       => new KeyValuePair<string, IHandler>("h1", GetHandler1__h1_());
   ```

2. **Array resolver method** — Collects all KVP entries:

   ```csharp
   private KeyValuePair<string, IHandler>[] GetAllKvp_string_IHandler_Array()
       => [GetKvp_string_IHandler__h1_(), GetKvp_string_IHandler__h2_()];
   ```

3. **`_localResolvers` entries** — For `IEnumerable<KVP>`, `IReadOnlyCollection<KVP>`, `IReadOnlyList<KVP>`, and `KVP[]`:

   ```csharp
   new(new ServiceIdentifier(typeof(IEnumerable<KeyValuePair<string, IHandler>>), KeyedService.AnyKey),
       static c => c.GetAllKvp_string_IHandler_Array()),
   ```

## See Also

- [KeyValuePair Registration](Register.KeyValuePair.spec.md)
- [Injection](Container.Injection.spec.md)
