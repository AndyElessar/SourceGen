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

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        // Create scopes for imported modules (so their scoped services are properly isolated)
        _sharedModule1 = (global::SharedModule1)parent._sharedModule1.CreateScope().ServiceProvider;
        _sharedModule2 = (global::SharedModule2)parent._sharedModule2.CreateScope().ServiceProvider;
    }

    // Local services array with container-typed resolvers
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        new(new ServiceIdentifier(typeof(IServiceProvider), KeyedService.AnyKey), static c => c),
        new(new ServiceIdentifier(typeof(IServiceScopeFactory), KeyedService.AnyKey), static c => c),
        new(new ServiceIdentifier(typeof(global::AppContainer), KeyedService.AnyKey), static c => c),
        new(new ServiceIdentifier(typeof(global::ILocalService1), KeyedService.AnyKey), static c => c.GetLocalService1()),
        new(new ServiceIdentifier(typeof(global::ILocalService2), KeyedService.AnyKey), static c => c.GetLocalService2()),
        // ... more local services
    ];

    private static readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers = 
        global::SharedModule1.Resolvers.Select(static kvp => 
                new KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>(kvp.Key, c => kvp.Value(c._sharedModule1)))
            .Concat(global::SharedModule2.Resolvers.Select(static kvp => 
                new KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>(kvp.Key, c => kvp.Value(c._sharedModule2))))
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

        return _fallbackProvider?.GetService(serviceType);
    }

    #endregion

    #region IKeyedServiceProvider

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? KeyedService.AnyKey;

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, key), out var resolver))
            return resolver(this);

        return _fallbackProvider is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;
    }

    #endregion
}
#endregion
```

## See Also

- [Import Module Registration](Register.ImportModule.spec.md)
- [Service Resolution Strategy](Container.Options.spec.md)
