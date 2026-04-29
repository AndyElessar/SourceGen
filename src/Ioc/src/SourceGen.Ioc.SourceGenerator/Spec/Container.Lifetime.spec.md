# Service Lifetime Management

## Overview

The container supports all three service lifetimes: Singleton (shared across all scopes), Scoped (new per scope), and Transient (new every time). Understanding lifetime management is key to proper container configuration.

## Supported Lifetimes

|Lifetime|Storage|Scope Behavior|
|:---|:---|:---|
|Singleton|Root container field|Shared across all scopes|
|Scoped|Scope-local field|New instance per scope|
|Transient|None|New instance per resolution|

### Special Cases for Factory and Instance Registrations

|Registration Type|Lifetime|Has Field|Disposed by Container|
|:---|:---|:---|:---|
|Factory|Singleton/Scoped|Yes|Yes|
|Factory|Transient|No|No|
|Instance|Any|No|No|

> **Note**: Instance registrations are pre-existing static instances managed externally. They don't need field caching (the instance already exists) and should NOT be disposed by the container.

## Example

```csharp
#region Define:
[IocRegister<ISingleton>(ServiceLifetime.Singleton)]
public class SingletonService : ISingleton;

[IocRegister<IScoped>(ServiceLifetime.Scoped)]
public class ScopedService : IScoped;

[IocRegister<ITransient>(ServiceLifetime.Transient)]
public class TransientService : ITransient;

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        // Copy only singleton references from parent
        _singletonService = parent._singletonService;
        // _scopedService is NOT copied - each scope has its own
        _serviceResolvers = parent._serviceResolvers;
    }

    #region Service Resolution

    // Singleton - stored in root, shared across scopes
    private global::SingletonService? _singletonService;
    private readonly SemaphoreSlim _singletonServiceSemaphore = new(1, 1);
    private global::SingletonService GetSingletonService()
    {
        if (_singletonService is not null) return _singletonService;

        _singletonServiceSemaphore.Wait();
        try
        {
            if (_singletonService is not null) return _singletonService;

            var instance = new global::SingletonService();
            _singletonService = instance;
            return instance;
        }
        finally
        {
            _singletonServiceSemaphore.Release();
        }
    }

    // Scoped - stored in each scope instance
    private global::ScopedService? _scopedService;
    private readonly SemaphoreSlim _scopedServiceSemaphore = new(1, 1);
    private global::ScopedService GetScopedService()
    {
        if (_scopedService is not null) return _scopedService;

        _scopedServiceSemaphore.Wait();
        try
        {
            if (_scopedService is not null) return _scopedService;

            var instance = new global::ScopedService();
            _scopedService = instance;
            return instance;
        }
        finally
        {
            _scopedServiceSemaphore.Release();
        }
    }

    // Transient - no storage, created every time
    private global::TransientService GetTransientService()
    {
        return new global::TransientService();
    }

    #endregion
}
#endregion
```

## See Also

- [Basic Container Generation](Container.Basic.spec.md)
- [Thread Safety](Container.ThreadSafety.spec.md)
- [Eager Resolution Options](Container.Options.spec.md)
