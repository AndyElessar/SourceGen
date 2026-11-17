# Module Import and Composition

## Overview

Import registrations from other containers marked with `IocImportModuleAttribute`. By default, the container uses `FrozenDictionary` to combine services from all sources.

## FrozenDictionary Mode

The `IIocContainer.Resolvers` property from imported modules is used to build the combined dictionary at runtime:

```csharp
#region Define:
// In SharedModule assembly
[IocContainer]
public partial class SharedModule1;

[IocContainer]
public partial class SharedModule2;

// In main application
public interface ILocalService1;
public interface ILocalService2;

[IocRegister<ILocalService1>(ServiceLifetime.Scoped)]
public class LocalService1 : ILocalService1;

[IocRegister<ILocalService2>(ServiceLifetime.Scoped)]
public class LocalService2 : ILocalService2;

[IocImportModule<SharedModule1>]
[IocImportModule<SharedModule2>]
[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer : IIocContainer<global::AppContainer>, IServiceProvider, /* ... */
{
    private readonly global::SharedModule1 _sharedModule1;
    private readonly global::SharedModule2 _sharedModule2;

    public AppContainer() : this((IServiceProvider?)null) { }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
        _sharedModule1 = new global::SharedModule1(fallbackProvider);
        _sharedModule2 = new global::SharedModule2(fallbackProvider);
    }

    private AppContainer(AppContainer parent, IIocFallbackScopeOwner? fallbackScopeOwner)
    {
        _fallbackProvider = parent._fallbackProvider;
        _fallbackScopeOwner = fallbackScopeOwner;
        _isRootScope = false;
        // Create scopes for imported modules (so their scoped services are properly isolated),
        // which use the fallback scope of this scope
        _sharedModule1 = (global::SharedModule1)((IIocFallbackScopeOwner)parent._sharedModule1).CreateScope(this).ServiceProvider;
        _sharedModule2 = (global::SharedModule2)((IIocFallbackScopeOwner)parent._sharedModule2).CreateScope(this).ServiceProvider;
    }

    // Local services array with container-typed resolvers
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        __Resolver(typeof(IServiceProvider), static c => c),
        __Resolver(typeof(IServiceScopeFactory), static c => c),
        __Resolver(typeof(global::AppContainer), static c => c),
        __Resolver(typeof(global::LocalService1), static c => c.GetLocalService1()),
        __Resolver(typeof(global::ILocalService1), static c => c.GetLocalService1()),
        __Resolver(typeof(global::LocalService2), static c => c.GetLocalService2()),
        __Resolver(typeof(global::ILocalService2), static c => c.GetLocalService2()),
    ];

    // Sources in import order, then the container's own (see Collections of Imported Modules)
    private static readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers = __CombineResolvers(
    [
        __ImportModuleResolvers(global::SharedModule1.Resolvers, static c => c._sharedModule1),
        __ImportModuleResolvers(global::SharedModule2.Resolvers, static c => c._sharedModule2),
        _localResolvers,
    ]);
    // WARNING: _localResolvers MUST be declared before _serviceResolvers because C# static field initializers execute in textual (declaration) order. This initializer references imported module Resolvers and _localResolvers; if reversed, _localResolvers would be null when _serviceResolvers initializes.

    #region IIocContainer

    public static IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>> Resolvers => _serviceResolvers;

    #endregion

    #region IServiceProvider

    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, ServiceIdentifier.NonKeyed), out var resolver))
            return resolver(this);

        // IEnumerable<T> without collection resolver, then the fallback provider (see Container.Collections)
        return __ResolveCollection(serviceType, null) ?? GetFallbackServiceProvider()?.GetService(serviceType);
    }

    #endregion

    #region IKeyedServiceProvider

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? ServiceIdentifier.NonKeyed;

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, key), out var resolver))
            return resolver(this);

        return __ResolveCollection(serviceType, serviceKey) ?? (GetFallbackServiceProvider() is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null);
    }

    #endregion
}
#endregion
```

## Fallback Scope of Imported Modules

Within one container scope, a fallback scoped service (e.g. a `DbContext` registered in the `IServiceCollection`) MUST be the same instance whether it is resolved through an imported module or through the importing container. A container scope therefore creates the scope of each imported module through `IIocFallbackScopeOwner.CreateScope(this)`: the module scope resolves fallback services through `GetFallbackServiceProvider()` of the importing scope, which creates the single fallback scope on first use and disposes it after the module scopes. Nested imports chain the owners, so every module scope uses the fallback scope of the outermost container scope.

What the generator emits for an imported module depends on the members the module has:

|Module|Root construction|Scope creation|
|:---|:---|:---|
|Declared in the current assembly|`[IocContainer]` decides: `new Module(fallbackProvider)` when `IntegrateServiceProvider = true`, otherwise `new Module()`|`CreateScope(this)` when `IntegrateServiceProvider = true`, otherwise `CreateScope()`|
|From a referenced assembly or project|`new Module(fallbackProvider)` when it has a public constructor taking a single `IServiceProvider`, otherwise `new Module()`|`CreateScope(this)` when it implements `IIocFallbackScopeOwner`, otherwise `CreateScope()`|

- The importing container passes the fallback provider and shares its fallback scope only when its own `IntegrateServiceProvider = true`.
- A module built by an earlier generator version does not implement `IIocFallbackScopeOwner`: its scopes keep opening their own fallback scope, until the module is rebuilt.
- Generated members of a module declared in the current assembly are not visible to the generator, which is why `[IocContainer]` decides for it; for a referenced module the attribute may be missing from metadata (`[Conditional("SOURCEGEN")]`), which is why its members decide.

## Collections of Imported Modules

With `IntegrateServiceProvider = true`, an `IEnumerable<T>` of an imported module's services resolved through the importing container (its `GetService`, `GetServices<T>()`, and dependencies that call them) MUST [merge the fallback services](Container.Collections.spec.md#merging-fallback-services) exactly once, with the importing container's fallback provider and, in a scope, the importing scope's fallback scope, whatever the module's own settings: a module with `IntegrateServiceProvider = false` (created with `new Module()`) and a module that integrates the fallback provider give the same result. The module MUST be built with the same SourceGen.Ioc version as the importer: a module built before `ServiceIdentifier.NonKeyed` keys its non-keyed entries with `KeyedService.AnyKey`, so the importer would miss its non-keyed services and treat its non-keyed collections as `AnyKey` collections (see [Keyed Services](Container.KeyedServices.spec.md#non-keyed-key-and-keyedserviceanykey)).

The importer therefore does not call the module's merging resolver. The `IEnumerable<T>` entry of every generated collection (`__EnumerableResolver<T>`, used by `__CollectionResolvers<T>`, `__KeyedCollectionResolvers<T>`, the `KeyedService.AnyKey` collection, and the collections with explicitly registered collection types) is the `Resolve` method of an `IocCollectionResolver<TContainer>` (runtime library, `[EditorBrowsable(Never)]`), which carries the collection's array resolver, which never merges. `__ImportModuleResolvers<TModule>` wraps the module's `Resolvers` once, when `_serviceResolvers` is built: an entry whose delegate target is an `IocCollectionResolver<TModule>` resolves its array resolver with the module instance and, with `IntegrateServiceProvider = true`, merges it with the non-generic `__MergeFallbackServices(Type, Array, object?)` of the importer, under the same key (`null` for non-keyed entries, keyed with the entry's key, including `KeyedService.AnyKey`). Keyed collections of a module therefore merge once too. The wrapped entry is again an `IocCollectionResolver<TContainer>` of the importer, so nested imports merge only at the outermost importer. Every other entry, including an explicit registration of `IEnumerable<T>` (see [Explicit Collection Registrations](Container.Collections.spec.md#explicit-collection-registrations)) and an entry of a module that does not come from this generator, resolves with the module instance of the importing container (scope), as before. The pairing does not depend on a `T[]` entry, so a module whose `T[]` is registered explicitly still merges once.

```csharp
// IntegrateServiceProvider = true
private static IEnumerable<__IocResolver> __ImportModuleResolvers<TModule>(IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<TModule, object>>> resolvers, Func<AppContainer, TModule> module)
{
    foreach(var entry in resolvers)
    {
        var resolver = entry.Value;
        if(resolver.Target is IocCollectionResolver<TModule> collection)
        {
            var arrayResolver = collection.ArrayResolver;
            Func<AppContainer, object> array = c => arrayResolver(module(c));
            var serviceType = entry.Key.ServiceType;
            var serviceKey = entry.Key.Key == ServiceIdentifier.NonKeyed ? null : entry.Key.Key;
            // A single service combined by a nested importer (see Combining Collections) keeps resolving that service
            Func<AppContainer, object> resolve = __IsEnumerableType(serviceType)
                ? c => c.__MergeFallbackServices(serviceType, (Array)array(c), serviceKey)
                : c => resolver(module(c));
            yield return new(entry.Key, new IocCollectionResolver<AppContainer>(array, resolve).Resolve);
            // without IntegrateServiceProvider: new IocCollectionResolver<AppContainer>(array, c => resolver(module(c))).Resolve
        }
        else
        {
            yield return new(entry.Key, c => resolver(module(c)));
        }
    }
}
```

- Without `IntegrateServiceProvider`, the importer uses the same helper, so the collections it re-exports stay recognizable for an outer importer; it never merges.
- Other collection types of the module (`IReadOnlyList<T>`, `T[]`, …) resolved with `GetService` stay container-only and last-wins: the importer's own entry, if any, otherwise the module's. Only `IEnumerable<T>` is combined (see [Combining Collections](#combining-collections)).
- The services of a module inject their own collections inside the module, through the module's generated code: they merge according to the module's `IntegrateServiceProvider`, with the module's fallback scope, which is the importing scope's when the module shares it (see [Fallback Scope of Imported Modules](#fallback-scope-of-imported-modules)). A module without `IntegrateServiceProvider` never asks the fallback provider, for collections or for single services.

### Combining Collections

When the importer and its imported modules register the same service type, an `IEnumerable<T>` resolved through the importer (`GetService`, `GetServices<T>()`, `GetKeyedServices<T>(key)`, including `KeyedService.AnyKey`) MUST contain the services of all of them, like `MS.DI` returns every registration of a service type; otherwise a module's handlers silently disappear as soon as the importer registers one of its own. The order is that of an `IServiceCollection` to which the modules' registrations are added before the importer's:

1. the fallback provider's services of the same key (with `IntegrateServiceProvider = true`), merged once;
2. the services of each imported module, in import order (a nested importer contributes its own combined collection);
3. the importer's own services.

A single service (`GetService<T>()`, `GetKeyedService<T>(key)`) stays last-wins: the importer's registration wins over the modules', and a later module's over an earlier one's.

`__CombineResolvers` builds `_serviceResolvers` once, at static initialization, from one dictionary per source (each module's wrapped entries, then `_localResolvers`), last-wins within each source (`_localResolvers` lists duplicates on purpose, e.g. an explicit registration after a generated entry). For each `IEnumerable<T>` identifier (any key, including `KeyedService.AnyKey`):

|Sources|Entry|
|:---|:---|
|Any source registers `IEnumerable<T>` explicitly (an entry that is not an `IocCollectionResolver`)|The last explicit registration; nothing is combined, like `MS.DI`, where an exact `IEnumerable<T>` registration wins|
|Two or more parts|A combined `IocCollectionResolver<TContainer>`: its array resolver concatenates the parts, in source order, into an array of `T` (`__CombineCollection`, `Array.CreateInstance`), and its resolver merges the fallback services once (with `IntegrateServiceProvider = true`). An outer importer therefore treats it as one part|
|One part|Unchanged|

A part of a source is the array resolver of its `IEnumerable<T>` entry, or, when the source has no such entry, its single-service resolver of `T` under the same key (not for `KeyedService.AnyKey`, value types, `Task<T>`, `IServiceProvider` / `IServiceScopeFactory`, which every container registers as itself, or the collection types a container generates: `IEnumerable<…>`, `IReadOnlyCollection<…>`, `ICollection<…>`, `IReadOnlyList<…>`, `IList<…>` and arrays). Without these exclusions `GetServices<IServiceProvider>()` would return each module instance next to the importer, while `MS.DI` has a single `IServiceProvider`; an explicit registration of one of these collection types in several sources is therefore last-wins. Collections of value type elements are not combined (last-wins), as `Array.CreateInstance` is only AOT-safe for reference element types.

When no source has an `IEnumerable<T>` entry but two or more register a single `T` under the same key, the `T` entry becomes an `IocCollectionResolver<TContainer>` whose resolver is the last-wins service and whose array resolver is the combined collection. `__ResolveCollection` (the single-service collection fallback, see [Single-Service Collection Fallback](Container.Collections.spec.md#single-service-collection-fallback)) returns that array instead of a one-element array, and an outer importer uses it as the source's part. The type `IEnumerable<T>` is never created at runtime (no `MakeGenericType`).

Injected collections MUST contain the same services as `GetServices<T>()` of the same scope (see [Injected Collections](Container.Collections.spec.md#injected-collections)). In a container with imported modules, every collection dependency (constructor, member, decorator and factory parameters, partial accessors, and collections of `Lazy<T>` / `Func<T>` elements) therefore resolves through `GetServices<T>()` / `GetKeyedServices<T>(key)` (`.ToArray()` for the other collection types) instead of the container's own array resolvers. `KeyValuePair` collections and dictionaries are unchanged. Containers without imported modules generate the same code as before.

- A module reached twice (imported directly and through another module, or by two modules) contributes its services twice; the generator does not deduplicate diamond imports.
- A collection of `Lazy<T>` / `Func<T>` combines the `Lazy<T>` / `Func<T>` entries of the sources, which a module generates only for the wrappers its own services request.

## Services of Imported Modules

Constructor dependencies and partial accessors of a service the container does not register itself resolve through `GetService` / `GetRequiredService`, which look it up in `_serviceResolvers` and therefore find the services of imported modules, also when `IntegrateServiceProvider = false` (see [Partial Accessors](Container.PartialAccessors.spec.md#resolution-rules)).

## See Also

- [Import Module Registration](Register.ImportModule.spec.md)
- [Service Resolution Strategy](Container.Options.spec.md)
- [Partial Accessors](Container.PartialAccessors.spec.md)
