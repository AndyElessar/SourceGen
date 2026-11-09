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
        global::SharedModule1.Resolvers.Select(static kvp => new KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>(kvp.Key, c => kvp.Value(c._sharedModule1)))
        .Concat(global::SharedModule2.Resolvers.Select(static kvp => new KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>(kvp.Key, c => kvp.Value(c._sharedModule2))))
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

        return GetFallbackServiceProvider()?.GetService(serviceType);
    }

    #endregion

    #region IKeyedServiceProvider

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? KeyedService.AnyKey;

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, key), out var resolver))
            return resolver(this);

        return GetFallbackServiceProvider() is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;
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

## Services of Imported Modules

Constructor dependencies and partial accessors of a service the container does not register itself resolve through `GetService` / `GetRequiredService`, which look it up in `_serviceResolvers` and therefore find the services of imported modules, also when `IntegrateServiceProvider = false` (see [Partial Accessors](Container.PartialAccessors.spec.md#resolution-rules)).

## See Also

- [Import Module Registration](Register.ImportModule.spec.md)
- [Service Resolution Strategy](Container.Options.spec.md)
- [Partial Accessors](Container.PartialAccessors.spec.md)
