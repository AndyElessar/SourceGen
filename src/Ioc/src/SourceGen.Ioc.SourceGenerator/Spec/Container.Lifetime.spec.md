# Service Lifetime Management

## Overview

The container supports all three service lifetimes: Singleton (shared across all scopes), Scoped (new per scope), and Transient (new every time). Understanding lifetime management is key to proper container configuration.

## Supported Lifetimes

|Lifetime|Storage|Scope Behavior|
|:---|:---|:---|
|Singleton|Root container field|Shared across all scopes. Scopes copy eager instances and MUST delegate lazily created singletons to the root via `_root`.|
|Scoped|Scope-local field|New instance per scope|
|Transient|None|New instance per resolution. Not tracked: the container never disposes transient services (the caller owns them).|

> **Scoped services resolved from the root**: The root container caches them in its own scoped fields and disposes them when the root is disposed, before singletons.

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
public interface ISingleton;
public interface IScoped;
public interface ITransient;

[IocRegister<ISingleton>(ServiceLifetime.Singleton)]
public class SingletonService : ISingleton;

[IocRegister<IScoped>(ServiceLifetime.Scoped)]
public class ScopedService : IScoped;

[IocRegister<ITransient>(ServiceLifetime.Transient)]
public class TransientService : ITransient;

// Lazy singletons (the default EagerResolveOptions.Singleton creates them in the root constructor)
[IocContainer(EagerResolveOptions = EagerResolveOptions.None, ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Root container reference (null in the root); emitted only when some singleton is created lazily
    private readonly AppContainer? _root;

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        _root = parent._root ?? parent;
        // Copy singleton references from parent (fast path when already created)
        _singletonService = parent._singletonService;
        // _scopedService is NOT copied - each scope has its own
    }

    #region Service Resolution

    // Singleton - stored in root, shared across scopes
    private global::SingletonService? _singletonService;
    private readonly SemaphoreSlim _singletonServiceSemaphore = new(1, 1);
    private global::SingletonService GetSingletonService() => _singletonService ?? CreateSingletonService();

    private global::SingletonService CreateSingletonService()
    {
        // Scopes never create singletons; the root creates, caches, and disposes them
        if(_root is not null) return _root.GetSingletonService();

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
    private global::ScopedService GetScopedService() => _scopedService ?? CreateScopedService();

    private global::ScopedService CreateScopedService()
    {
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
