# Keyed Service Support

## Overview

Full support for keyed services with various key types. Each keyed registration gets its own field and resolver, named after the implementation type and the key. `GetKeyedService` looks up `(serviceType, key)` in the `_serviceResolvers` dictionary.

## Keyed Service Resolution

```csharp
#region Define:
public interface ICache;

[IocRegister<ICache>(ServiceLifetime.Singleton, Key = "memory")]
public class MemoryCache : ICache;

[IocRegister<ICache>(ServiceLifetime.Singleton, Key = "redis")]
public class RedisCache : ICache;

[IocRegister<ICache>(ServiceLifetime.Singleton, Key = CacheType.Distributed, KeyType = KeyType.Value)]
public class DistributedCache : ICache;

public enum CacheType { Memory, Distributed }

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Keyed members are named after the implementation type and the key (eager singletons by default)
    private global::MemoryCache _memoryCache__memory_ = null!;
    private global::MemoryCache GetMemoryCache__memory_() => _memoryCache__memory_ ?? CreateMemoryCache__memory_();

    private global::RedisCache _redisCache__redis_ = null!;
    private global::RedisCache GetRedisCache__redis_() => _redisCache__redis_ ?? CreateRedisCache__redis_();

    private global::DistributedCache _distributedCache_CacheType_Distributed = null!;
    private global::DistributedCache GetDistributedCache_CacheType_Distributed() => _distributedCache_CacheType_Distributed ?? CreateDistributedCache_CacheType_Distributed();

    // ...CreateXxx methods...

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        // ...built-in services...
        __KeyedResolver(typeof(global::MemoryCache), "memory", static c => c.GetMemoryCache__memory_()),
        __KeyedResolver(typeof(global::ICache), "memory", static c => c.GetMemoryCache__memory_()),
        __KeyedResolver(typeof(global::RedisCache), "redis", static c => c.GetRedisCache__redis_()),
        __KeyedResolver(typeof(global::ICache), "redis", static c => c.GetRedisCache__redis_()),
        __KeyedResolver(typeof(global::DistributedCache), global::CacheType.Distributed, static c => c.GetDistributedCache_CacheType_Distributed()),
        __KeyedResolver(typeof(global::ICache), global::CacheType.Distributed, static c => c.GetDistributedCache_CacheType_Distributed()),
    ];

    public object? GetKeyedService(global::System.Type serviceType, object? serviceKey)
    {
        ThrowIfDisposed();

        var key = serviceKey ?? global::SourceGen.Ioc.ServiceIdentifier.NonKeyed;

        if(_serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key), out var resolver))
            return resolver(this);

        // IEnumerable<T> with a keyed registration of T under this key: the fallback's keyed services, then that registration (see Container.Collections)
        return __ResolveCollection(serviceType, serviceKey) ?? (GetFallbackServiceProvider() is global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null);
    }

    public bool IsKeyedService(global::System.Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? global::SourceGen.Ioc.ServiceIdentifier.NonKeyed;

        if(_serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key))) return true;

        // IEnumerable<T> that __ResolveCollection resolves
        if(__IsCollectionService(serviceType, serviceKey)) return true;

        return _fallbackProvider is global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService isKeyed && isKeyed.IsKeyedService(serviceType, serviceKey);
    }
}
#endregion
```

## Non-Keyed Key and `KeyedService.AnyKey`

Non-keyed entries are stored under `ServiceIdentifier.NonKeyed`, a sentinel of the runtime library, and a `null` key (`GetKeyedService(type, null)`) means non-keyed, like `MS.DI`. `KeyedService.AnyKey` is not the non-keyed key: it is a request for the services registered under any key, as in `MS.DI`. Lookups of non-keyed services compare and hash the sentinel by reference, so the hot path costs the same as before.

Verified `MS.DI` behavior (`Microsoft.Extensions.DependencyInjection` 10.0 and 11.0, pinned by `KeyedMultiCollectionTests` in `SourceGen.Ioc.TestAot`):

|Request|`MS.DI` result|Container|
|:---|:---|:---|
|`GetKeyedServices<T>(KeyedService.AnyKey)` / `GetKeyedService(typeof(IEnumerable<T>), KeyedService.AnyKey)`|Every keyed registration of `T`, under any key, in registration order. Non-keyed registrations (including a `null` key) and registrations made with `KeyedService.AnyKey` as their key are excluded|The same, through the `KeyedService.AnyKey` collection resolver (see [Keyed Collections](#keyed-collections)), after the fallback provider's `AnyKey` collection when `IntegrateServiceProvider = true`|
|`GetKeyedServices<T>("k")`|The registrations under `"k"` only (not the `KeyedService.AnyKey` registrations)|The same|
|`GetKeyedService<T>(KeyedService.AnyKey)` (single service), or another collection type (`IReadOnlyList<T>`, …) with `AnyKey`|Throws `InvalidOperationException`|Not served by the container: asks the fallback provider (`null` without one)|
|`IsKeyedService(typeof(IEnumerable<T>), KeyedService.AnyKey)`|`true` for every closed `IEnumerable<T>`|`true` when the container has an `AnyKey` collection of `T`, otherwise asks the fallback provider|

This changes earlier behavior: `GetKeyedService<T>(KeyedService.AnyKey)` and `GetKeyedServices<T>(KeyedService.AnyKey)` no longer return the non-keyed services, and `IIocContainer<T>.Resolvers` keys non-keyed entries with `ServiceIdentifier.NonKeyed`. Containers and imported modules MUST be generated with the same SourceGen.Ioc version, otherwise the importer looks up a module's non-keyed entries with a different key and misses them.

## Keyed Collections

Keyed collection resolvers are generated by registration count, without demand detection, like non-keyed collections of several implementations: demand detection only sees requests in the compilation that declares the container, so requests from other assemblies would be missed.

|Keyed registrations of `T`|Generated|
|:---|:---|
|Two or more unique registrations under a key `k` (async-init registrations excluded)|A keyed collection resolver `GetAll{T}Array_{key}()` MUST be generated, with the entries `IEnumerable<T>`, `IReadOnlyCollection<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IList<T>` and `T[]` under `k` (`__KeyedCollectionResolvers<T>`). Elements are in registration order, like non-keyed collections|
|Exactly one registration under `k`|No resolver: `GetKeyedServices<T>(k)` and `IEnumerable<T>` under `k` resolve through the [single-service collection fallback](Container.Collections.spec.md#single-service-collection-fallback). Other collection types under `k` are not served|
|At least one keyed registration (any key but `KeyedService.AnyKey` itself)|A `KeyedService.AnyKey` collection of all keyed registrations of `T` in registration order MUST be registered as `IEnumerable<T>` under `KeyedService.AnyKey` only (`__EnumerableResolver<T>`). Two or more unique registrations get the array method `GetAll{T}Array_AnyKey()`; a single registration (every keyed implementation type, and most keyed service types) is created inline (`static c => new T[] { c.GetT_key_() }`), so it generates no method. It is not generated when `IEnumerable<T>` is registered explicitly under `KeyedService.AnyKey`. Without it, `GetKeyedServices<T>(KeyedService.AnyKey)` would miss the container's services|

`{key}` is the key's safe identifier (`"a"` → `_a_`, `Kind.First` → `TestNamespace_Kind_First`, a `KeyType.Csharp` expression → its identifier). When two keys of `T` get the same identifier (`"x-y"` and `"x.y"`), or the name is taken by another generated or user member, the FNV-1a hash of the key expression is appended (`GetAllIHandlerArray__x_y__8610c19c`), then `_Resolve` while it is still taken, so names are deterministic.

|Request|Resolution|
|:---|:---|
|`GetKeyedServices<T>(k)`, `GetKeyedService(typeof(IEnumerable<T>), k)` (generic and non-generic)|The keyed collection resolver, after the fallback provider's keyed services under `k` ([merged](Container.Collections.spec.md#merging-fallback-services) when `IntegrateServiceProvider = true`); without a resolver, the single-service collection fallback|
|`IReadOnlyCollection<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IList<T>`, `T[]` under `k`|The keyed collection resolver, not merged (not served by `MS.DI`)|
|Keyed collection dependency (`[IocInject(Key = "k")] IEnumerable<T>` and the other collection types, keyed constructor or method parameters)|With a keyed collection resolver: `__MergeFallbackServices(GetAll{T}Array_{key}(), k)` (`GetAll{T}Array_{key}()` without `IntegrateServiceProvider`), which converts to every collection type. Under `KeyedService.AnyKey` (any expression of it, e.g. `[IocInject("KeyedService.AnyKey", KeyType = KeyType.Csharp)]`), the same with `GetAll{T}Array_AnyKey()`. Otherwise (including an `AnyKey` collection of a single registration, which has no array method) `GetKeyedServices<T>(k)` (`.ToArray()` for types other than `IEnumerable<T>`)|
|A collection type registered explicitly under `k` (e.g. `[IocRegisterFor<HandlerList>(ServiceTypes = [typeof(IEnumerable<IHandler>)], Key = "k")]`)|The explicit registration, in every mode and as a dependency: the keyed collection resolver leaves that type out (see [Explicit Collection Registrations](Container.Collections.spec.md#explicit-collection-registrations))|
|`IsKeyedService` of any of these entries|`true`|
|Switch mode (`UseSwitchStatement = true`)|`GetKeyedService` dispatches the same entries in its keyed switch. `IsKeyedService` searches the keyed entries of `_localResolvers`, so it also reports keyed single services, which it missed before|

Keyed `Lazy<T>` / `Func<T>` collections (`[IocInject(Key = "k")] IEnumerable<Lazy<T>>`) are unchanged: wrapper collections contain only non-keyed registrations and keyed `Lazy<T>` / `Func<T>` are not registered as keyed entries, so they resolve through `GetKeyedServices<Lazy<T>>(k)` (the fallback provider; `MS.DI` registers no `Lazy<T>`). `KeyValuePair<K, V>` collections and dictionaries are non-keyed collections of the keyed registrations and are unchanged.

```csharp
#region Define:
public interface IHandler;

[IocRegister<IHandler>(ServiceLifetime.Singleton, Key = "a")]
public class FirstHandler : IHandler;

[IocRegister<IHandler>(ServiceLifetime.Singleton, Key = "b")]
public class SingleHandler : IHandler;

[IocRegister<IHandler>(ServiceLifetime.Scoped, Key = "a")]
public class SecondHandler : IHandler;

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // "a" has two registrations; "b" has one and gets no keyed collection resolver
    private global::IHandler[] GetAllIHandlerArray__a_() =>
        [
            GetFirstHandler__a_(),
            GetSecondHandler__a_(),
        ];

    // All keyed registrations of IHandler, for KeyedService.AnyKey
    private global::IHandler[] GetAllIHandlerArray_AnyKey() =>
        [
            GetFirstHandler__a_(),
            GetSingleHandler__b_(),
            GetSecondHandler__a_(),
        ];

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        // ...
        ..__KeyedCollectionResolvers<global::IHandler>("a", static c => c.GetAllIHandlerArray__a_()),
        __EnumerableResolver<global::IHandler>(KeyedService.AnyKey, static c => c.GetAllIHandlerArray_AnyKey()),
        // A single registration needs no array method
        __EnumerableResolver<global::FirstHandler>(KeyedService.AnyKey, static c => new global::FirstHandler[] { c.GetFirstHandler__a_() }),
        // ...SingleHandler, SecondHandler
    ];

    // The IEnumerable<T> entry of every generated collection: merges the fallback services under the same key, and keeps
    // the array resolver for importing containers (see Container.ImportModule)
    private static __IocResolver __EnumerableResolver<T>(object key, Func<global::AppContainer, object> resolver)
    {
        var serviceKey = key == ServiceIdentifier.NonKeyed ? null : key;
        return new(new ServiceIdentifier(typeof(IEnumerable<T>), key), new IocCollectionResolver<global::AppContainer>(resolver, c => c.__MergeFallbackServices((T[])resolver(c), serviceKey)).Resolve);
        // without IntegrateServiceProvider: new IocCollectionResolver<global::AppContainer>(resolver, resolver).Resolve
    }

    private static __IocResolver[] __KeyedCollectionResolvers<T>(object key, Func<global::AppContainer, object> resolver) =>
    [
        __EnumerableResolver<T>(key, resolver),
        __KeyedResolver(typeof(IReadOnlyCollection<T>), key, resolver),
        // ...ICollection<T>, IReadOnlyList<T>, IList<T>, T[]
    ];
}
#endregion

#region Usage:
container.GetKeyedServices<IHandler>("a");                  // [fallback "a" services..., FirstHandler, SecondHandler]
container.GetKeyedServices<IHandler>("b");                  // [fallback "b" services..., SingleHandler] (single-service collection fallback)
container.GetKeyedServices<IHandler>(KeyedService.AnyKey);  // [fallback AnyKey services..., FirstHandler, SingleHandler, SecondHandler]
container.GetKeyedService<IHandler>(KeyedService.AnyKey);   // not served by the container: the fallback provider (MS.DI throws)
#endregion
```

## See Also

- [Basic Registration](Register.Basic.spec.md)
- [KeyValuePair Registration](Register.KeyValuePair.spec.md)
- [Dictionary Resolution](Container.Collections.spec.md)
