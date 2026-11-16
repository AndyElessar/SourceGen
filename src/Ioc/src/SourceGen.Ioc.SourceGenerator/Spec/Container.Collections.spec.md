# Collection and Wrapper Types

## Overview

Support for collection resolution when multiple implementations are registered for a service type, and wrapper types like `Lazy<T>`, `Func<T>`, and `KeyValuePair<K, V>`.

## Feature 1: Collection Resolution

Support for collection resolution when multiple implementations are registered for a service type. Collection resolvers are generated and registered in `_localResolvers`.

|Non-keyed registrations of `T`|Collection resolver (`GetAllT_Array()` + `IEnumerable<T>`/`IReadOnlyCollection<T>`/`ICollection<T>`/`IReadOnlyList<T>`/`IList<T>`/`T[]` entries)|
|:---|:---|
|Two or more unique implementations|MUST be generated.|
|Exactly one|MUST be generated only when something requests `T` as a collection. The sources MUST match closed-generic discovery: a constructor, factory or decorator parameter, an injection member, or the return type of a container's partial accessor (including inside `Lazy`/`Func`/`Task` wrappers); an `IServiceProvider` invocation (`GetServices<T>()`, `GetServices(typeof(T))`, or `GetService`/`GetRequiredService` of a collection of `T`); or `[IocDiscover]` of a collection of `T`. Otherwise it MUST NOT be generated, to keep the resolver table small.|

> **Note**: Invocation discovery uses a single syntax provider (`TransformInvocation`) whose result feeds both closed-generic resolution (`ClosedGenericDependencies`) and collection resolver generation (`CollectionElementTypeName`). Keyed collection requests are not counted, because keyed collections resolve through `GetKeyedServices`. Without a generated collection resolver, `GetServices<T>()` resolves through the [single-service collection fallback](#single-service-collection-fallback), and a dependency of a collection type other than `IEnumerable<T>` resolves as `GetServices<T>().ToArray()` (or `GetKeyedServices<T>(key).ToArray()`) so that it converts to the requested type. Only invocations visible to the generator (in the compilation that declares the container) are detected; requests made elsewhere (another project, a framework) are served by the single-service collection fallback.

### Single-Service Collection Fallback

A request of `IEnumerable<T>` that no collection resolver serves (a single registration that the declaring compilation never requests as a collection, or any keyed collection, which never gets a collection resolver) MUST resolve as a one-element `T[]` of the single-service resolver of `T` under the same key, before the fallback provider is asked. The element is the same instance that `GetService<T>()` / `GetKeyedService<T>(key)` returns, so the result neither depends on the fallback provider (empty `IEnumerable<T>` from an `MS.DI` provider that lacks the registration) nor creates a second instance there.

|Rule|Required behavior|
|:---|:---|
|Order|The generated collection resolver (the fast path) MUST win. The fallback runs only on a miss and before the fallback provider is asked alone.|
|Entry points|`GetService(typeof(IEnumerable<T>))`, `GetKeyedService(typeof(IEnumerable<T>), key)` (and so `GetRequiredService` / `GetRequiredKeyedService`), the generated `GetServices<T>()` / `GetKeyedServices<T>(key)`, which keyed `IEnumerable<T>` dependencies call, and the generated `GetService<T>` / `GetRequiredService<T>` / `GetKeyedService<T>` / `GetRequiredKeyedService<T>` of `T = IEnumerable<TElement>` (through the non-generic helper), so generic and non-generic calls agree. Other collection types (`IReadOnlyList<T>`, `T[]`, …) are not served.|
|Keys|A `null` key means the non-keyed registration, like `GetKeyedService`. `KeyedService.AnyKey` MUST NOT be served (in `MS.DI` it requests all keyed registrations) and goes to the fallback provider. Keys match like the single-service lookup (`ServiceIdentifier` equality).|
|Excluded elements|`Task<T>` elements (async-init services, which collections exclude, see [Injection](Container.Injection.spec.md)) and value types MUST NOT be wrapped.|
|AOT|The generic methods create `T[]` directly. The non-generic path creates the array with `Array.CreateInstance` for reference element types only, with `IL3050` suppressed by `UnconditionalSuppressMessage`, like `MS.DI`'s runtime resolver.|
|Switch mode|`UseSwitchStatement = true` has no dictionary: the element's resolver is searched in `_localResolvers`. `GetKeyedService(typeof(IEnumerable<T>), null)`, which the keyed switch does not dispatch, first looks for the exact `IEnumerable<T>` entry there, so a generated collection resolver wins over the single-service resolver. The generic methods delegate to the non-generic ones.|
|`IsService`|`IsService(typeof(IEnumerable<T>))` / `IsKeyedService(typeof(IEnumerable<T>), key)` MUST return `true` when this fallback resolves the request (`__IsCollectionService`), so frameworks that classify parameters with `IServiceProviderIsService` (e.g. minimal APIs) treat it as a service. `MS.DI` returns `true` for every closed `IEnumerable<T>`; the container returns `true` only for what it resolves itself and otherwise asks the fallback provider, because without a fallback provider an `IEnumerable<T>` of an unknown `T` resolves as `null`.|

### Merging Fallback Services

With `IntegrateServiceProvider = true`, an `IEnumerable<T>` that the container resolves (a generated collection resolver, or the single-service collection fallback above) MUST also contain the fallback provider's `IEnumerable<T>` (keyed with the same key), like `MS.DI` returns every registration of `T`. For example, an `IHostedService` registered in the container joins the hosted services the host registered in the `IServiceCollection`.

|Rule|Required behavior|
|:---|:---|
|Order|The fallback provider's services come first, then the container's. The container wins single-service resolution (it is asked before the fallback provider), which matches `MS.DI` only when its registrations come after the fallback's, and `MS.DI` lists a collection in registration order, so the instance `GetService<T>()` returns is the collection's last element.|
|Duplicates|A service registered in both (e.g. the fallback provider is built from an `IServiceCollection` that also contains the Register output of the container's registrations) appears twice, with different instances. This is a misconfiguration: there is no cheap and correct identity to remove duplicates by (implementation type is not one, as `MS.DI` allows the same implementation twice). It is not detected.|
|Cost|Without a fallback provider (`_fallbackProvider` is `null`), or when it returns an empty `ICollection`, the container's array MUST be returned as is, without allocation. Otherwise the result is a new array. In a scope the request uses (and so creates) the fallback scope.|
|Scope of the merge|Only `IEnumerable<T>` requests through the `IServiceProvider` APIs merge: the `IEnumerable<T>` entry of `__CollectionResolvers<T>` (also used by imported modules' resolvers), its switch branch, and the single-service collection fallback. Other collection types (`IReadOnlyList<T>`, `T[]`, …) are not served by `MS.DI` and stay container-only. `KeyValuePair<K, V>` collections and dictionaries are not merged. Dependencies of container services (and partial accessors) that call a generated array resolver directly (`GetAllT_Array()`) do not merge; keyed collection dependencies of any collection type, which call `GetKeyedServices<T>(key)` (`.ToArray()` for types other than `IEnumerable<T>`), do. Imported modules' collection entries merge according to the module's own fallback provider and fallback scope.|
|`AnyKey`|Not served by the single-service collection fallback. Because non-keyed entries use `KeyedService.AnyKey` as their key, `GetKeyedServices<T>(KeyedService.AnyKey)` still hits a non-keyed generated collection resolver (existing behavior, which differs from `MS.DI`'s "all keyed services").|

Generated members (`__ResolveCollection`, `__FindCollectionElementResolver`, `__IsCollectionService`, `__GetFallbackServices` and `__MergeFallbackServices` are reserved names, see [Basic](Container.Basic.spec.md#member-names)):

```csharp
public object? GetService(global::System.Type serviceType)
{
    ThrowIfDisposed();

    if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var resolver))
        return resolver(this);

    return __ResolveCollection(serviceType, null) ?? GetFallbackServiceProvider()?.GetService(serviceType);
}

private static Func<AppContainer, object>? __FindCollectionElementResolver(Type serviceType, object? serviceKey)
{
    if(serviceKey == KeyedService.AnyKey || !serviceType.IsConstructedGenericType || serviceType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
        return null;

    var elementType = serviceType.GenericTypeArguments[0];
    if(elementType.IsValueType || (elementType.IsConstructedGenericType && elementType.GetGenericTypeDefinition() == typeof(Task<>)))
        return null;

    var elementId = new ServiceIdentifier(elementType, serviceKey ?? KeyedService.AnyKey);
    return _serviceResolvers.TryGetValue(elementId, out var resolver) ? resolver : null; // switch mode: last match in _localResolvers
}

// Used by IsService / IsKeyedService
private static bool __IsCollectionService(Type serviceType, object? serviceKey) => __FindCollectionElementResolver(serviceType, serviceKey) is not null;

[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Arrays are only created for reference element types.")]
private object? __ResolveCollection(Type serviceType, object? serviceKey)
{
    if(__FindCollectionElementResolver(serviceType, serviceKey) is not { } resolver)
        return null;

    var elementType = serviceType.GenericTypeArguments[0];
    var service = resolver(this);
    // Without IntegrateServiceProvider: a one-element array of service
    List<object?>? fallbackServices = null;
    if(__GetFallbackServices(serviceType, serviceKey) is IEnumerable enumerable && enumerable is not ICollection { Count: 0 })
    {
        foreach(var item in enumerable)
            (fallbackServices ??= []).Add(item);
    }

    var count = fallbackServices?.Count ?? 0;
    var services = Array.CreateInstance(elementType, count + 1);
    for(var i = 0; i < count; i++)
        services.SetValue(fallbackServices![i], i);

    services.SetValue(service, count);
    return services;
}

// Used by GetServices<T>() (null key) and GetKeyedServices<T>(serviceKey) on a miss
private T[]? __ResolveCollection<T>(object? serviceKey) =>
    serviceKey != KeyedService.AnyKey
    && !typeof(T).IsValueType
    && !(typeof(T).IsConstructedGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Task<>))
    && _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), serviceKey ?? KeyedService.AnyKey), out var resolver)
        ? __MergeFallbackServices<T>([(T)resolver(this)], serviceKey) // without IntegrateServiceProvider: [(T)resolver(this)]
        : null;

// IntegrateServiceProvider only
private object? __GetFallbackServices(Type serviceType, object? serviceKey) =>
    serviceKey is null
        ? GetFallbackServiceProvider()?.GetService(serviceType)
        : (GetFallbackServiceProvider() as IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);

private T[] __MergeFallbackServices<T>(T[] services, object? serviceKey) =>
    __GetFallbackServices(typeof(IEnumerable<T>), serviceKey) is IEnumerable<T> fallbackServices && fallbackServices is not ICollection<T> { Count: 0 }
        ? [.. fallbackServices, .. services]
        : services;

// The IEnumerable<T> entry of the generated collection resolvers merges too (IntegrateServiceProvider only)
private static __IocResolver[] __CollectionResolvers<T>(Func<AppContainer, object> resolver) =>
[
    __Resolver(typeof(IEnumerable<T>), c => c.__MergeFallbackServices((T[])resolver(c), null)),
    __Resolver(typeof(IReadOnlyCollection<T>), resolver),
    // ...ICollection<T>, IReadOnlyList<T>, IList<T>, T[]
];
```

### Switch-Mode Collection Branches

`UseSwitchStatement = true` MUST dispatch every generated collection entry that the dictionary mode registers in `_localResolvers`, in `GetService` and in `IsService`: the six types of `__CollectionResolvers<T>` for each collection resolver and `Lazy<T>` / `Func<T>` collection resolver (`IEnumerable<T>` merging the fallback services), and the `KeyValuePair<K, V>` collection and dictionary types:

```csharp
if(serviceType == typeof(IEnumerable<IPlugin>)) return __MergeFallbackServices(GetAllIPluginArray(), null);
if(serviceType == typeof(IReadOnlyCollection<IPlugin>) || serviceType == typeof(ICollection<IPlugin>) || serviceType == typeof(IReadOnlyList<IPlugin>) || serviceType == typeof(IList<IPlugin>) || serviceType == typeof(IPlugin[])) return GetAllIPluginArray();
```

> **Note**: Multiple keyed registrations of `T` under one key still resolve as the last one (one-element array); collecting all of them is not supported yet.

### Supported Collection Types

|Type|Resolution Method|Notes|
|:---|:---|:---|
|`IEnumerable<T>`|Returns collection expression|Standard MS.DI pattern|
|`IReadOnlyCollection<T>`|Returns array|Supports `Count` property|
|`IReadOnlyList<T>`|Returns array|Supports indexer access|
|`T[]`|Returns array|Native array type|

> **Note**: Collection resolution only generates for service types with multiple distinct implementations (excluding self-registrations), or with a single implementation that a dependency or an `IServiceProvider` invocation requests as a collection. Other `IEnumerable<T>` requests use the [single-service collection fallback](#single-service-collection-fallback).

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

    // Eager singletons (the default EagerResolveOptions)
    private global::Plugin1 _plugin1 = null!;
    private global::Plugin1 GetPlugin1() => _plugin1 ?? CreatePlugin1();

    private global::Plugin1 CreatePlugin1()
    {
        var instance = new global::Plugin1();

        _plugin1 = instance;
        return instance;
    }

    private global::Plugin2 _plugin2 = null!;
    private global::Plugin2 GetPlugin2() => _plugin2 ?? CreatePlugin2();

    private global::Plugin2 CreatePlugin2()
    {
        var instance = new global::Plugin2();

        _plugin2 = instance;
        return instance;
    }

    // One array resolver serves IEnumerable<T>, IReadOnlyCollection<T>, ICollection<T>, IReadOnlyList<T>, IList<T> and T[]
    private global::IPlugin[] GetAllIPluginArray() =>
        [
            GetPlugin1(),
            GetPlugin2(),
        ];

    #endregion

    // Registered in _localResolvers
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        // ...built-in services...
        __Resolver(typeof(global::Plugin1), static c => c.GetPlugin1()),
        // A single IPlugin resolves the last registration
        __Resolver(typeof(global::IPlugin), static c => c.GetPlugin2()),
        __Resolver(typeof(global::Plugin2), static c => c.GetPlugin2()),
        ..__CollectionResolvers<global::IPlugin>(static c => c.GetAllIPluginArray()),
    ];

    private static __IocResolver[] __CollectionResolvers<T>(Func<global::AppContainer, object> resolver) =>
    [
        __Resolver(typeof(IEnumerable<T>), resolver),
        __Resolver(typeof(IReadOnlyCollection<T>), resolver),
        __Resolver(typeof(ICollection<T>), resolver),
        __Resolver(typeof(IReadOnlyList<T>), resolver),
        __Resolver(typeof(IList<T>), resolver),
        __Resolver(typeof(T[]), resolver),
    ];
}
#endregion
```

## Feature 2: Wrapper Type Resolution

When a dependency uses a wrapper type (`Lazy<T>`, `Func<T>`, `KeyValuePair<K, V>`), the container generates wrapper resolution that delegates to the container's own resolver methods, or to the instance expression of an `Instance` registration, which has no resolver method. Dependencies are scanned like the Register output (see [Register Injection](Register.Injection.spec.md#wrapper-type-resolution)): factory or constructor parameters, injection members, and decorators' constructor parameters and injection members.

### Supported Wrapper Types

| Wrapper Type | Generated Code | Notes |
| :--- | :--- | :--- |
| `Lazy<T>` | `_lazy_{T}{field}` (one readonly field per resolver) | Created in the container and scope constructors; registered in `_localResolvers` |
| `Func<T>` | `_func_{T}{field}` (one readonly field per resolver) | Created in the container and scope constructors; registered in `_localResolvers` |
| `KeyValuePair<K, V>` | `new KeyValuePair<K, V>(key, GetMyService())` | Key from service key or `default` |

`Lazy<T>` and `Func<T>` wrapper fields MUST be created once per resolver of `T`, so the keyed, factory and instance registrations of one implementation each get their own field; the field name is the wrapper prefix, the short name of `T` and the resolver's field name (`_lazy_IMyService_myService`, `_lazy_IMyService_myService__keyed_`). `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` resolvers (`GetAllLazy_T_Array()` / `GetAllFunc_T_Array()`) MUST contain only the non-keyed registrations of `T`, like `IEnumerable<T>`.

A `Lazy<T>` of a transient registration MUST NOT be shared: no field is generated, and every consumer, collection resolver call and container resolution creates a new `Lazy<T>` (`new Lazy<T>(() => GetMyService(), ...)`), so each gets its own instance, like the Register output where the wrapper inherits the transient lifetime. `Func<T>` fields stay shared, because each invocation calls the resolver.

`Func<T1, ..., TReturn>` with input parameters is built inline per dependency and MUST behave like the Register output (see [Register Injection](Register.Injection.spec.md)): each call constructs a new implementation instance from the arguments, also for singleton and scoped registrations, then wraps it in the registration's decorators (locals `decorated` / `decorated_{n}`, so they cannot clash with the enclosing resolver's locals). The arguments are only matched to the implementation. A keyed dependency uses the keyed registration, whose `[ServiceKey]` parameters receive its key; for a non-keyed registration they receive `default(T)`.

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

    // Consumer is an eager singleton (the default EagerResolveOptions)
    private global::Consumer _consumer = null!;
    private global::Consumer GetConsumer() => _consumer ?? CreateConsumer();

    private global::Consumer CreateConsumer()
    {
        var instance = new global::Consumer(_lazy_IMyService_myService, _func_IMyService_myService);

        _consumer = instance;
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

3. **Dictionary resolver method** — Builds a `Dictionary<K, V>` from the same entries:

   ```csharp
   private Dictionary<string, IHandler> GetAllKvp_string_IHandler_Dictionary() =>
       new Dictionary<string, IHandler>()
       {
           ["h1"] = GetHandler1__h1_(),
           ["h2"] = GetHandler2__h2_(),
       };
   ```

4. **`_localResolvers` entries** — `IReadOnlyList<KVP>`, `IList<KVP>` and `KVP[]` use the array resolver; `IEnumerable<KVP>`, `IReadOnlyCollection<KVP>`, `ICollection<KVP>` and the dictionary interfaces use the dictionary resolver:

   ```csharp
   __Resolver(typeof(IEnumerable<KeyValuePair<string, IHandler>>), static c => c.GetAllKvp_string_IHandler_Dictionary()),
   __Resolver(typeof(IReadOnlyList<KeyValuePair<string, IHandler>>), static c => c.GetAllKvp_string_IHandler_Array()),
   __Resolver(typeof(KeyValuePair<string, IHandler>[]), static c => c.GetAllKvp_string_IHandler_Array()),
   __Resolver(typeof(IReadOnlyDictionary<string, IHandler>), static c => c.GetAllKvp_string_IHandler_Dictionary()),
   __Resolver(typeof(IDictionary<string, IHandler>), static c => c.GetAllKvp_string_IHandler_Dictionary()),
   __Resolver(typeof(Dictionary<string, IHandler>), static c => c.GetAllKvp_string_IHandler_Dictionary()),
   ```

## See Also

- [KeyValuePair Registration](Register.KeyValuePair.spec.md)
- [Injection](Container.Injection.spec.md)
