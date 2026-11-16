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

    private static readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers =
        // Without IntegrateServiceProvider: Resolvers.Select(static kvp => new KeyValuePair<…>(kvp.Key, c => kvp.Value(c._sharedModule1)))
        __ImportModuleResolvers(global::SharedModule1.Resolvers, static c => c._sharedModule1) // see Collections of Imported Modules
        .Concat(__ImportModuleResolvers(global::SharedModule2.Resolvers, static c => c._sharedModule2))
        .Concat(_localResolvers)
        .ToFrozenDictionary();
    // WARNING: _localResolvers MUST be declared before _serviceResolvers because C# static field initializers execute in textual (declaration) order. This initializer references imported module Resolvers and _localResolvers; if reversed, _localResolvers would be null when _serviceResolvers initializes.

    #region IIocContainer

    public static IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>> Resolvers => _serviceResolvers;

    #endregion

    #region IServiceProvider

    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var resolver))
            return resolver(this);

        // IEnumerable<T> without collection resolver, then the fallback provider (see Container.Collections)
        return __ResolveCollection(serviceType, null) ?? GetFallbackServiceProvider()?.GetService(serviceType);
    }

    #endregion

    #region IKeyedServiceProvider

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? KeyedService.AnyKey;

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

With `IntegrateServiceProvider = true`, an `IEnumerable<T>` of an imported module's services resolved through the importing container (its `GetService`, `GetServices<T>()`, and dependencies that call them) MUST [merge the fallback services](Container.Collections.spec.md#merging-fallback-services) exactly once, with the importing container's fallback provider and, in a scope, the importing scope's fallback scope, whatever the module's own settings: a module with `IntegrateServiceProvider = false` (created with `new Module()`), a module built by an earlier generator version, and a module that integrates the fallback provider all give the same result.

The importer therefore does not call the module's `IEnumerable<T>` entry, which merges according to the module's settings. `__ImportModuleResolvers<TModule>` wraps the module's `Resolvers` once, when `_serviceResolvers` is built: a non-keyed `IEnumerable<T>` entry of a reference type `T` resolves the module's `T[]` entry, which never merges (`__CollectionResolvers<T>` registers both with the same array resolver), and merges it with the non-generic `__MergeFallbackServices(Type, Array, object?)` of the importer. Every other entry resolves with the module instance of the importing container (scope), as before. An `IEnumerable<T>` entry without a matching `T[]` entry (a module that does not come from this generator), and value types (`KeyValuePair<K, V>` collections, which are not merged), keep the module's entry. Nested imports wrap at each level, so the outermost importer merges.

```csharp
// IntegrateServiceProvider = true
private static readonly FrozenDictionary<ServiceIdentifier, Func<AppContainer, object>> _serviceResolvers =
    __ImportModuleResolvers(global::SharedModule1.Resolvers, static c => c._sharedModule1)
    .Concat(__ImportModuleResolvers(global::SharedModule2.Resolvers, static c => c._sharedModule2))
    .Concat(_localResolvers)
    .ToFrozenDictionary();

private static IEnumerable<__IocResolver> __ImportModuleResolvers<TModule>(IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<TModule, object>>> resolvers, Func<AppContainer, TModule> module)
{
    var arrayResolvers = new Dictionary<Type, Func<TModule, object>>();
    foreach(var entry in resolvers)
    {
        if(entry.Key.ServiceType.IsArray && entry.Key.Key == KeyedService.AnyKey)
            arrayResolvers[entry.Key.ServiceType.GetElementType()!] = entry.Value;
    }

    foreach(var entry in resolvers)
    {
        var serviceType = entry.Key.ServiceType;
        var resolver = entry.Value;
        if(entry.Key.Key == KeyedService.AnyKey
            && serviceType.IsConstructedGenericType
            && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            && !serviceType.GenericTypeArguments[0].IsValueType
            && arrayResolvers.TryGetValue(serviceType.GenericTypeArguments[0], out var arrayResolver))
        {
            yield return new(entry.Key, c => c.__MergeFallbackServices(serviceType, (Array)arrayResolver(module(c)), null));
        }
        else
        {
            yield return new(entry.Key, c => resolver(module(c)));
        }
    }
}
```

- Without `IntegrateServiceProvider`, the importer keeps `Module.Resolvers.Select(…)` and generates no helper.
- Other collection types of the module (`IReadOnlyList<T>`, `T[]`, …) stay container-only, like the importer's own.
- The services of a module inject their own collections inside the module, through the module's generated code: they merge according to the module's `IntegrateServiceProvider`, with the module's fallback scope, which is the importing scope's when the module shares it (see [Fallback Scope of Imported Modules](#fallback-scope-of-imported-modules)). A module without `IntegrateServiceProvider` never asks the fallback provider, for collections or for single services.

## Services of Imported Modules

Constructor dependencies and partial accessors of a service the container does not register itself resolve through `GetService` / `GetRequiredService`, which look it up in `_serviceResolvers` and therefore find the services of imported modules, also when `IntegrateServiceProvider = false` (see [Partial Accessors](Container.PartialAccessors.spec.md#resolution-rules)).

## See Also

- [Import Module Registration](Register.ImportModule.spec.md)
- [Service Resolution Strategy](Container.Options.spec.md)
- [Partial Accessors](Container.PartialAccessors.spec.md)
