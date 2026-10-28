# Collection and Wrapper Types

## Overview

Support for collection resolution when multiple implementations are registered for a service type, and wrapper types like `Lazy<T>`, `Func<T>`, and `KeyValuePair<K, V>`.

## Feature 1: Collection Resolution

Support for collection resolution when multiple implementations are registered for a service type. Collection resolvers are generated and registered in `_localResolvers`.

|Non-keyed registrations of `T`|Collection resolver (`GetAllT_Array()` + `IEnumerable<T>`/`IReadOnlyCollection<T>`/`ICollection<T>`/`IReadOnlyList<T>`/`IList<T>`/`T[]` entries)|
|:---|:---|
|Two or more unique implementations|MUST be generated.|
|Exactly one|MUST be generated only when something requests `T` as a collection. The sources MUST match closed-generic discovery: a constructor, factory or decorator parameter, or an injection member (including inside `Lazy`/`Func`/`Task` wrappers); an `IServiceProvider` invocation (`GetServices<T>()`, `GetServices(typeof(T))`, or `GetService`/`GetRequiredService` of a collection of `T`); or `[IocDiscover]` of a collection of `T`. Otherwise it MUST NOT be generated, to keep the resolver table small.|

> **Note**: Invocation discovery uses a single syntax provider (`TransformInvocation`) whose result feeds both closed-generic resolution (`ClosedGenericDependencies`) and collection resolver generation (`CollectionElementTypeName`). Keyed collection requests are not counted, because keyed collections resolve through `GetKeyedServices`. Without a generated collection resolver, `GetServices<T>()` falls back to the fallback provider. Only invocations visible to the generator (in the compilation that declares the container) are detected.

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
        __Resolver(typeof(global::Plugin1), static c => c.GetPlugin1()),
        __Resolver(typeof(global::IPlugin), static c => c.GetPlugin1()),
        __Resolver(typeof(global::Plugin2), static c => c.GetPlugin2()),
        // IEnumerable<T>, IReadOnlyCollection<T>, ICollection<T>, IReadOnlyList<T>, IList<T> and T[]
        ..__CollectionResolvers<global::IPlugin>(static c => c.GetAllIPluginArray()),
    ];
}
#endregion
```

## Feature 2: Wrapper Type Resolution

When a constructor parameter or injected member uses a wrapper type (`Lazy<T>`, `Func<T>`, `KeyValuePair<K, V>`), the container generates wrapper resolution that delegates to the container's own resolver methods.

### Supported Wrapper Types

| Wrapper Type | Generated Code | Notes |
| :--- | :--- | :--- |
| `Lazy<T>` | `_lazy_{T}{field}` (one readonly field per resolver) | Created in the container and scope constructors; registered in `_localResolvers` |
| `Func<T>` | `_func_{T}{field}` (one readonly field per resolver) | Created in the container and scope constructors; registered in `_localResolvers` |
| `KeyValuePair<K, V>` | `new KeyValuePair<K, V>(key, GetMyService())` | Key from service key or `default` |

`Lazy<T>` and `Func<T>` wrapper fields MUST be created once per resolver of `T`, so the keyed, factory and instance registrations of one implementation each get their own field; the field name is the wrapper prefix, the short name of `T` and the resolver's field name (`_lazy_IMyService_myService`, `_lazy_IMyService_myService__keyed_`). `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` resolvers (`GetAllLazy_T_Array()` / `GetAllFunc_T_Array()`) MUST contain only the non-keyed registrations of `T`, like `IEnumerable<T>`.

A `Lazy<T>` of a transient registration MUST NOT be shared: no field is generated, and every consumer, collection resolver call and container resolution creates a new `Lazy<T>` (`new Lazy<T>(() => GetMyService(), ...)`), so each gets its own instance, like the Register output where the wrapper inherits the transient lifetime. `Func<T>` fields stay shared, because each invocation calls the resolver.

| `IDictionary<K, V>` | `GetServices<KVP<K, V>>().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` | Collection-based dictionary resolution |
| `IReadOnlyDictionary<K, V>` | `GetServices<KVP<K, V>>().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` | Collection-based dictionary resolution |
| `Dictionary<K, V>` | `GetServices<KVP<K, V>>().ToDictionary(kvp => kvp.Key, kvp => kvp.Value)` | Collection-based dictionary resolution |

> **Note**: Nested wrapper behavior is shape-dependent. For non-collection outer wrappers, inner wrappers are recursively constructed **inline** (no dedicated resolver). For collection outer wrappers, wrapper resolution relies on standalone wrapper registrations and has stricter nesting behavior.
>
> - `Lazy<Func<T>>` → `new Lazy<Func<T>>(() => new Func<T>(() => GetMyService()))`
> - `Func<Lazy<T>>` → `new Func<Lazy<T>>(() => new Lazy<T>(() => GetMyService()))`
> - `Lazy<IEnumerable<T>>` → `new Lazy<IEnumerable<T>>(() => GetServices<T>())`
> - `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` (non-keyed, parameterless `Func<T>`) — MUST call the generated wrapper array resolver directly (`GetAllLazy_T_Array()` / `GetAllFunc_T_Array()`) when one exists, so the switch-statement container (which has no wrapper dictionary entries) resolves them too. Otherwise they fall back to `GetServices<Lazy<T>>()`.
>
> **Design rationale (inline nested wrappers):**
>
> - Pragmatic choice: inline construction for nested wrappers is deliberate.
> - Rare usage: directly resolving nested wrappers from the container (for example, `container.GetService<Lazy<Func<T>>>()`) is extremely rare; the primary use case is constructor injection, which inline construction fully covers.
> - Implementation simplicity: this avoids extending field scanning, naming conventions, and scoped container infrastructure for nested wrapper shapes.
> - No instance-sharing requirement: nested wrappers typically do not require cross-consumer instance sharing; each consumer holding its own inline-constructed instance is semantically correct.
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
    // Wrapper fields, created in the container and scope constructors:
    // _lazy_IMyService_myService = new global::System.Lazy<global::IMyService>(() => GetMyService(), global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    // _func_IMyService_myService = new global::System.Func<global::IMyService>(() => GetMyService());
    private readonly global::System.Lazy<global::IMyService> _lazy_IMyService_myService;
    private readonly global::System.Func<global::IMyService> _func_IMyService_myService;

    private global::Consumer GetConsumer()
    {
        var instance = new global::Consumer(_lazy_IMyService_myService, _func_IMyService_myService);
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
   __Resolver(typeof(IEnumerable<KeyValuePair<string, IHandler>>), static c => c.GetAllKvp_string_IHandler_Array()),
   ```

## See Also

- [KeyValuePair Registration](Register.KeyValuePair.spec.md)
- [Injection](Container.Injection.spec.md)
