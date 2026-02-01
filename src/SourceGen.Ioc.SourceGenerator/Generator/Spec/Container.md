# Container Source Generator

This source generator automatically generates a compile-time IoC container based on `Ioc*` attributes. The generated container provides high-performance dependency injection without runtime reflection.

## Overview

The container generator creates a `partial class` that implements multiple DI-related interfaces, providing a complete dependency injection solution that can work standalone or integrate with `Microsoft.Extensions.DependencyInjection`.

## Supported Attributes

### Container Attribute

- `IocContainerAttribute` - Trigger generator to generate an IoC Container on a partial class

### Related Attributes

The container uses registrations from the following attributes. See [Registration.md](Registration.md) for details:

- **Registration**: `IocRegisterAttribute`, `IocRegisterForAttribute`
- **Defaults**: `IocRegisterDefaultsAttribute`
- **Module Import**: `IocImportModuleAttribute`
- **Discovery**: `IocDiscoverAttribute`
- **Injection**: `IocInjectAttribute`, `InjectAttribute`
- **Generic Factory**: `IocGenericFactoryAttribute`

## Generated Interfaces

The generated container implements the following interfaces:

|Interface|Purpose|
|:---|:---|
|`IIocContainer<TContainer>`|SourceGen.Ioc generic container interface, exposes service registry with container-typed resolvers|
|`IServiceProvider`|Standard .NET service provider|
|`IKeyedServiceProvider`|Keyed service resolution (.NET 8+)|
|`IServiceProviderIsService`|Service availability checking|
|`IServiceProviderIsKeyedService`|Keyed service availability checking|
|`ISupportRequiredService`|Required service resolution|
|`IServiceScopeFactory`|Create service scopes|
|`IServiceScope`|Represents a service scope (container is its own scope)|
|`IServiceProviderFactory<IServiceCollection>`|Build container from IServiceCollection (only when `ResolveIServiceCollection = true` AND DI package is referenced)|
|`IDisposable`|Synchronous disposal|
|`IAsyncDisposable`|Asynchronous disposal|

## Container Attribute Options

```csharp
[IocContainer(
    ResolveIServiceCollection = true,  // Allow fallback to IServiceCollection and implement IServiceProviderFactory
    ExplicitOnly = false,              // Only register explicitly marked services
    IncludeTags = ["Tag1", "Tag2"],    // Only include services with specified tags
    UseSwitchStatement = false,        // Use FrozenDictionary by default; set true to use switch statement
    ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim,  // Thread safety strategy for singleton/scoped resolution
    EagerResolveOptions = EagerResolveOptions.Singleton  // Eager resolution for singleton/scoped services
)]
public partial class MyContainer;
```

|Property|Default|Description|
|:---|:---|:---|
|`ResolveIServiceCollection`|`true`|When true, unknown dependencies fallback to external IServiceProvider and implement `IServiceProviderFactory<IServiceCollection>`|
|`ExplicitOnly`|`false`|When true, only register services explicitly marked on the container class|
|`IncludeTags`|`[]`|When non-empty, only include services that have at least one matching tag. Services without tags are excluded.|
|`UseSwitchStatement`|`false`|When true, use cascading `if`/`switch` statements instead of `FrozenDictionary`. Only beneficial for small service counts (≤ 50). **Note**: When there are imported modules (`IocImportModuleAttribute`), `FrozenDictionary` is always used regardless of this setting, because combining services from multiple sources requires dictionary-based lookup.|
|`ThreadSafeStrategy`|`SemaphoreSlim`|Thread safety strategy for singleton and scoped service resolution. See [ThreadSafeStrategy](#threadsafestrategy) for details.|
|`EagerResolveOptions`|`Singleton`|Controls which service lifetimes should be eagerly resolved during container/scope construction. See [EagerResolveOptions](#eagerresolveoptions) for details.|

> **Priority**: `ExplicitOnly` takes precedence over `IncludeTags`. When `ExplicitOnly = true`, `IncludeTags` is ignored.
>
> **Priority**: `IocImportModule` takes precedence over `UseSwitchStatement`. When there are imported modules, `UseSwitchStatement` is ignored and `FrozenDictionary` is always used.

### ThreadSafeStrategy

The `ThreadSafeStrategy` enum controls how the container ensures thread-safe initialization of singleton and scoped services. This only affects services with `Singleton` or `Scoped` lifetime; `Transient` services always create new instances without caching.

|Strategy|Description|Use Case|
|:---|:---|:---|
|`None`|No thread safety. Direct field assignment without synchronization.|Single-threaded applications or when external synchronization is guaranteed. **Warning**: May create multiple instances in multi-threaded scenarios.|
|`Lock`|Uses `lock` statement with double-checked locking pattern.|General-purpose thread safety with balanced performance.|
|`SemaphoreSlim`|Uses `SemaphoreSlim` with double-checked locking pattern. **(Default)**|Recommended for most scenarios. Slightly lower contention than `Lock`.|
|`SpinLock`|Uses `SpinLock` with double-checked locking pattern.|High-performance scenarios with very short initialization times. Not recommended for I/O-bound initialization.|

#### Generated Code Examples

##### ThreadSafeStrategy.None

No synchronization primitives. Simple but not thread-safe:

```csharp
private global::MyService? _myService;
private global::MyService GetMyService()
{
    if (_myService is not null) return _myService;

    var instance = new global::MyService();
    _myService = instance;
    return instance;
}
```

##### ThreadSafeStrategy.Lock

Uses `Lock` class with double-checked locking:

```csharp
private global::MyService? _myService;
private readonly Lock _myServiceLock = new();
private global::MyService GetMyService()
{
    if (_myService is not null) return _myService;

    lock (_myServiceLock)
    {
        if (_myService is not null) return _myService;

        var instance = new global::MyService();
        _myService = instance;
        return instance;
    }
}
```

##### ThreadSafeStrategy.SemaphoreSlim (Default)

Uses `SemaphoreSlim` with double-checked locking:

```csharp
private global::MyService? _myService;
private readonly SemaphoreSlim _myServiceSemaphore = new(1, 1);
private global::MyService GetMyService()
{
    if (_myService is not null) return _myService;

    _myServiceSemaphore.Wait();
    try
    {
        if (_myService is not null) return _myService;

        var instance = new global::MyService();
        _myService = instance;
        return instance;
    }
    finally
    {
        _myServiceSemaphore.Release();
    }
}
```

##### ThreadSafeStrategy.SpinLock

Uses `SpinLock` with double-checked locking. Note that `SpinLock` is a struct and must not be readonly:

```csharp
private global::MyService? _myService;
private SpinLock _myServiceSpinLock = new(false);
private global::MyService GetMyService()
{
    if (_myService is not null) return _myService;

    bool lockTaken = false;
    try
    {
        _myServiceSpinLock.Enter(ref lockTaken);
        if (_myService is not null) return _myService;

        var instance = new global::MyService();
        _myService = instance;
        return instance;
    }
    finally
    {
        if (lockTaken) _myServiceSpinLock.Exit();
    }
}
```

#### Field Generation by Strategy

Each strategy generates different synchronization fields:

|Strategy|Instance Field|Synchronization Field|Disposed|
|:---|:---|:---|:---|
|`None`|`private T? _field;`|None|N/A|
|`Lock`|`private T? _field;`|`private readonly Lock _fieldLock = new();`|No (no unmanaged resources)|
|`SemaphoreSlim`|`private T? _field;`|`private readonly SemaphoreSlim _fieldSemaphore = new(1, 1);`|**Yes** (in `Dispose`/`DisposeAsync`)|
|`SpinLock`|`private T? _field;`|`private SpinLock _fieldSpinLock = new(false);` (not readonly)|No (value type)|

> **Note**: Eager services use non-nullable fields (`private T _field;`) without synchronization fields, as they are initialized in the constructor.

#### Scope Constructor Behavior

When creating a child scope, synchronization fields are NOT copied from parent. Each scope has its own synchronization primitives for scoped services:

```csharp
private AppContainer(AppContainer parent)
{
    _fallbackProvider = parent._fallbackProvider;
    _isRootScope = false;

    // Copy singleton instance references from parent (both eager and lazy)
    _eagerSingletonService = parent._eagerSingletonService;
    _lazySingletonService = parent._lazySingletonService;

    // Singleton synchronization fields are NOT copied (parent already initialized)
    // Scoped fields are fresh for each scope (both instance and sync fields)

    // Initialize eager scoped services
    _eagerScopedService = GetEagerScopedService();

    _serviceResolvers = parent._serviceResolvers;
}
```

### EagerResolveOptions

The `EagerResolveOptions` enum controls which service lifetimes should be eagerly resolved during container/scope construction. Eager services are initialized immediately when the container or scope is created, rather than on first access.

|Option|Value|Description|
|:---|:---|:---|
|`None`|`0`|Do not eagerly resolve any services. All services are lazily resolved on first access.|
|`Singleton`|`1`|Eagerly resolve all singleton services when the root container is created. **(Default)**|
|`Scoped`|`2`|Eagerly resolve all scoped services when a scope is created.|
|`SingletonAndScoped`|`3`|Eagerly resolve both singleton and scoped services.|

> **Note**: `Transient` services are not supported for eager resolution, as they create a new instance on every access by design.

#### Eager vs Lazy Resolution

|Aspect|Eager Resolution|Lazy Resolution|
|:---|:---|:---|
|**Initialization**|In constructor|On first access|
|**Field Type**|Non-nullable (`private T _field;`)|Nullable (`private T? _field;`)|
|**Synchronization Field**|None|Required (based on `ThreadSafeStrategy`)|
|**Get Method**|Generated (used by constructor)|Generated (used by resolver and constructor)|
|**Resolver in `_localServices`**|Direct field access (`c => c._field`)|Method call (`c => c.GetXxx()`)|
|**Startup Time**|Longer (services initialized upfront)|Shorter (services initialized on demand)|
|**First Access Time**|Instant (already initialized)|May have delay (initialization occurs)|

#### Instance and Factory Registrations

- **Instance Registration**: Instance registrations are inherently eager as they directly return a pre-existing static instance. They are not affected by `EagerResolveOptions`.
- **Factory Registration**: Factory-based Singleton/Scoped registrations respect `EagerResolveOptions`. When eager, the factory is invoked during construction and the result is cached in the field.

#### Dependency Order Handling

Eager services may depend on other services (eager or lazy). The constructor initializes eager services by calling their `Get` methods, which automatically handles dependency resolution:

```csharp
// In constructor - Get methods handle dependencies recursively
_eagerServiceA = GetEagerServiceA();  // May call GetDependencyB() internally
_eagerServiceB = GetEagerServiceB();  // Already resolved if called by ServiceA
```

#### Generated Code Examples

##### EagerResolveOptions.Singleton (Default)

Singleton services are initialized in the root container constructor:

```csharp
#region Define:
public interface IEagerService;
public interface ILazyService;

[IocRegister<IEagerService>(ServiceLifetime.Singleton)]
internal class EagerService : IEagerService;

[IocRegister<ILazyService>(ServiceLifetime.Transient)]
internal class LazyService : ILazyService;

[IocContainer(EagerResolveOptions = EagerResolveOptions.Singleton)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Eager singleton - non-nullable field, no synchronization
    private global::EagerService _eagerService;
    private global::EagerService GetEagerService()
    {
        // First call from constructor creates instance, subsequent calls return cached
        if (_eagerService is not null) return _eagerService;

        var instance = new global::EagerService();
        _eagerService = instance;
        return instance;
    }

    // Transient - no field, always creates new instance
    private global::LazyService GetLazyService()
    {
        return new global::LazyService();
    }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;

        // Initialize eager singletons by calling Get methods
        _eagerService = GetEagerService();

        _serviceResolvers = _localServices.ToFrozenDictionary();
    }

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
    [
        // Eager service - direct field access (field is guaranteed initialized after constructor)
        new(new ServiceIdentifier(typeof(global::EagerService), KeyedService.AnyKey), static c => c._eagerService),
        new(new ServiceIdentifier(typeof(global::IEagerService), KeyedService.AnyKey), static c => c._eagerService),
        // Transient service - method call
        new(new ServiceIdentifier(typeof(global::LazyService), KeyedService.AnyKey), static c => c.GetLazyService()),
        new(new ServiceIdentifier(typeof(global::ILazyService), KeyedService.AnyKey), static c => c.GetLazyService()),
    ];
}
#endregion
```

##### EagerResolveOptions.Scoped

Scoped services are initialized in the scope constructor:

```csharp
#region Define:
public interface IScopedService;

[IocRegister<IScopedService>(ServiceLifetime.Scoped)]
internal class ScopedService : IScopedService;

[IocContainer(EagerResolveOptions = EagerResolveOptions.Scoped)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Eager scoped - non-nullable field, no synchronization
    private global::ScopedService _scopedService;
    private global::ScopedService GetScopedService()
    {
        // First call from constructor creates instance, subsequent calls return cached
        if (_scopedService is not null) return _scopedService;

        var instance = new global::ScopedService();
        _scopedService = instance;
        return instance;
    }

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;

        // Initialize eager scoped services by calling Get methods
        _scopedService = GetScopedService();

        _serviceResolvers = parent._serviceResolvers;
    }

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
    [
        // Eager scoped service - direct field access (field is guaranteed initialized after constructor)
        new(new ServiceIdentifier(typeof(global::ScopedService), KeyedService.AnyKey), static c => c._scopedService),
        new(new ServiceIdentifier(typeof(global::IScopedService), KeyedService.AnyKey), static c => c._scopedService),
    ];
}
#endregion
```

##### EagerResolveOptions.None

All services use lazy resolution with synchronization:

```csharp
#region Define:
[IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Lazy singleton - nullable field with synchronization
    private global::MySingleton? _mySingleton;
    private readonly SemaphoreSlim _mySingletonSemaphore = new(1, 1);
    private global::MySingleton GetMySingleton()
    {
        if (_mySingleton is not null) return _mySingleton;

        _mySingletonSemaphore.Wait();
        try
        {
            if (_mySingleton is not null) return _mySingleton;

            var instance = new global::MySingleton();
            _mySingleton = instance;
            return instance;
        }
        finally
        {
            _mySingletonSemaphore.Release();
        }
    }

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
    [
        // Lazy service - method call (not direct field access)
        new(new ServiceIdentifier(typeof(global::MySingleton), KeyedService.AnyKey), static c => c.GetMySingleton()),
    ];
}
#endregion
```

## Features

### 1. Basic Container Generation

Generate a container implementing all required interfaces with eager singleton resolution (default behavior):

```csharp
#region Define:
public interface IMyDependency;

[IocRegister<IMyDependency>(ServiceLifetime.Singleton)]
internal class MyDependency : IMyDependency;

public interface IMyService;

[IocRegister<IMyService>(ServiceLifetime.Singleton)]
internal class MyService(IMyDependency myDependency) : IMyService
{
    private readonly IMyDependency _myDependency = myDependency;
}

// Default: EagerResolveOptions = EagerResolveOptions.Singleton
[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
// <auto-generated/>
#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc;

partial class AppContainer : IIocContainer<global::AppContainer>, IServiceProvider, IKeyedServiceProvider,
    IServiceProviderIsService, IServiceProviderIsKeyedService, ISupportRequiredService,
    IServiceScopeFactory, IServiceScope, IDisposable, IAsyncDisposable, IServiceProviderFactory<IServiceCollection>
{
    private readonly IServiceProvider? _fallbackProvider;
    private readonly bool _isRootScope = true;
    private int _disposed;

    private readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers;

    #region Constructors

    /// <summary>
    /// Creates a new standalone container without external service provider fallback.
    /// </summary>
    public AppContainer() : this((IServiceProvider?)null) { }

    /// <summary>
    /// Creates a new container with optional fallback to external service provider.
    /// </summary>
    /// <param name="fallbackProvider">Optional external service provider for unknown dependencies.</param>
    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;

        // Initialize eager singletons (calls Get methods to handle dependencies)
        _myDependency = GetMyDependency();
        _myService = GetMyService();

        _serviceResolvers = _localServices.ToFrozenDictionary();
    }

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        // Copy eager singleton references from parent
        _myDependency = parent._myDependency;
        _myService = parent._myService;
        _serviceResolvers = parent._serviceResolvers;
    }

    #endregion

    #region Service Resolution

    // Eager singleton - non-nullable field, no synchronization
    private global::MyDependency _myDependency;
    private global::MyDependency GetMyDependency()
    {
        if (_myDependency is not null) return _myDependency;

        var instance = new global::MyDependency();
        _myDependency = instance;
        return instance;
    }

    // Eager singleton - non-nullable field, no synchronization
    private global::MyService _myService;
    private global::MyService GetMyService()
    {
        if (_myService is not null) return _myService;

        var instance = new global::MyService((global::IMyDependency)GetRequiredService(typeof(global::IMyDependency)));
        _myService = instance;
        return instance;
    }

    #endregion

    #region IServiceProvider

    public object? GetService(Type serviceType)
    {
        if(serviceType == typeof(IServiceProvider)) return this;
        if(serviceType == typeof(IServiceScopeFactory)) return this;
        if(serviceType == typeof(AppContainer)) return this;

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

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        ThrowIfDisposed();
        return GetKeyedService(serviceType, serviceKey) ?? throw new InvalidOperationException($"No service for type '{serviceType}' with key '{serviceKey}' has been registered.");
    }

    #endregion

    #region ISupportRequiredService

    public object GetRequiredService(Type serviceType)
    {
        ThrowIfDisposed();
        return GetService(serviceType) ?? throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
    }

    #endregion

    #region IServiceProviderIsService

    public bool IsService(Type serviceType)
    {
        if(serviceType == typeof(IServiceProvider)) return true;
        if(serviceType == typeof(IServiceScopeFactory)) return true;
        if(serviceType == typeof(AppContainer)) return true;

        if(_serviceResolvers.ContainsKey(new ServiceIdentifier(serviceType, KeyedService.AnyKey))) return true;

        return _fallbackProvider is IServiceProviderIsService isService && isService.IsService(serviceType);
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        var key = serviceKey ?? KeyedService.AnyKey;

        if(_serviceResolvers.ContainsKey(new ServiceIdentifier(serviceType, key))) return true;

        return _fallbackProvider is IServiceProviderIsKeyedService isKeyed && isKeyed.IsKeyedService(serviceType, serviceKey);
    }

    #endregion

    #region IServiceScopeFactory

    public IServiceScope CreateScope()
    {
        ThrowIfDisposed();
        return new AppContainer(this);
    }

    public AsyncServiceScope CreateAsyncScope() => new(CreateScope());

    IServiceProvider IServiceScope.ServiceProvider => this;

    #endregion

    #region IIocContainer

    public IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>> Resolvers => _serviceResolvers;

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
    [
        // Eager singletons - direct field access (not method calls)
        new(new ServiceIdentifier(typeof(global::MyDependency), KeyedService.AnyKey), static c => c._myDependency),
        new(new ServiceIdentifier(typeof(global::IMyDependency), KeyedService.AnyKey), static c => c._myDependency),
        new(new ServiceIdentifier(typeof(global::MyService), KeyedService.AnyKey), static c => c._myService),
        new(new ServiceIdentifier(typeof(global::IMyService), KeyedService.AnyKey), static c => c._myService),
    ];

    #endregion

    #region IServiceProviderFactory<IServiceCollection>

    /// <summary>
    /// Creates a new container builder (returns the same IServiceCollection).
    /// </summary>
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    /// <summary>
    /// Creates the service provider from the built IServiceCollection.
    /// </summary>
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        var fallbackProvider = containerBuilder.BuildServiceProvider();
        return new AppContainer(fallbackProvider);
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope)
        {
            return;
        }

        // Eager singletons - no SemaphoreSlim to dispose
        DisposeService(_myService);
        DisposeService(_myDependency);
    }

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope)
        {
            return;
        }

        // Eager singletons - no SemaphoreSlim to dispose
        await DisposeServiceAsync(_myService);
        await DisposeServiceAsync(_myDependency);
    }

    private static async ValueTask DisposeServiceAsync(object? service)
    {
        if(service is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else if(service is IDisposable disposable) disposable.Dispose();
    }

    private static void DisposeService(object? service)
    {
        if(service is IDisposable disposable) disposable.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed != 0) throw new ObjectDisposedException(GetType().Name);
    }

    #endregion
}
#endregion
```

### 2. Service Lifetime Management

The container supports all three service lifetimes:

|Lifetime|Storage|Scope Behavior|
|:---|:---|:---|
|Singleton|Root container field|Shared across all scopes|
|Scoped|Scope-local field|New instance per scope|
|Transient|None|New instance per resolution|

**Special Cases for Factory and Instance Registrations**:

|Registration Type|Lifetime|Has Field|Disposed by Container|
|:---|:---|:---|:---|
|Factory|Singleton/Scoped|Yes|Yes|
|Factory|Transient|No|No|
|Instance|Any|No|No|

> **Note**: Instance registrations are pre-existing static instances managed externally. They don't need field caching (the instance already exists) and should NOT be disposed by the container.

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

### 3. Keyed Service Support

Full support for keyed services with various key types.

> **Note**: The example below shows `UseSwitchStatement = true` style for clarity. By default, keyed services are also resolved via `FrozenDictionary` using the `ServiceIdentifier` key.

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
    // Note: In actual generated code, fields are placed above their resolver methods.
    // This example focuses on the GetKeyedService implementation.
    private MemoryCache? _memoryCache;
    private RedisCache? _redisCache;
    private DistributedCache? _distributedCache;

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceType == typeof(ICache))
        {
            return serviceKey switch
            {
                "memory" => GetMemoryCache(),
                "redis" => GetRedisCache(),
                CacheType.Distributed => GetDistributedCache(),
                _ => _fallbackProvider is IKeyedServiceProvider keyed
                    ? keyed.GetKeyedService(serviceType, serviceKey)
                    : null
            };
        }

        return _fallbackProvider is IKeyedServiceProvider keyed2
            ? keyed2.GetKeyedService(serviceType, serviceKey)
            : null;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceType == typeof(ICache))
        {
            return serviceKey is "memory" or "redis" or CacheType.Distributed;
        }

        return _fallbackProvider is IServiceProviderIsKeyedService isKeyed
            && isKeyed.IsKeyedService(serviceType, serviceKey);
    }
}
#endregion
```

### 4. Constructor, Property, and Method Injection

Support for all injection patterns consistent with Registration generator:

```csharp
#region Define:
[IocRegister<IMyService>(ServiceLifetime.Scoped)]
public class MyService : IMyService
{
    private readonly IDependency _dep;

    // Constructor injection
    public MyService(IDependency dep, [FromKeyedServices("special")] ISpecial special)
    {
        _dep = dep;
    }

    // Property injection
    [IocInject]
    public ILogger Logger { get; set; } = default!;

    [IocInject(Key = "config")]
    public IConfiguration? Config { get; set; }

    // Method injection
    [IocInject]
    public void Initialize(IInitializer init, [ServiceKey] object? key)
    {
        // ...
    }
}

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Field and lock for complex initialization (property/method injection)
    private global::MyService? _myService;
    private readonly Lock _myServiceLock = new();
    private global::MyService GetMyService()
    {
        if(_myService is not null) return _myService;

        lock(_myServiceLock)
        {
            if(_myService is not null) return _myService;

            var instance = new global::MyService((global::IDependency)GetRequiredService(typeof(global::IDependency)), (global::ISpecial)GetRequiredKeyedService(typeof(global::ISpecial), "special"))
            {
                Logger = (global::ILogger)GetRequiredService(typeof(global::ILogger)),
                Config = GetKeyedService(typeof(global::IConfiguration), "config") as global::IConfiguration,
            };
            instance.Initialize((global::IInitializer)GetRequiredService(typeof(global::IInitializer)), null);

            _myService = instance;
            return instance;
        }
    }
}
#endregion
```

### 5. Decorator Pattern Support

Decorators are resolved in the correct order. When decorators are present, the field type is the service type (interface) rather than the implementation type.

> **Note**: The `GetService` snippet below shows `UseSwitchStatement = true` style. The decorator resolution logic (`GetHandler`) remains the same regardless of the resolution strategy.

```csharp
#region Define:
public interface IHandler;

[IocRegister<IHandler>(ServiceLifetime.Scoped, Decorators = [typeof(LoggingDecorator), typeof(CachingDecorator)])]
public class Handler : IHandler;

public class LoggingDecorator(IHandler inner, ILogger logger) : IHandler;
public class CachingDecorator(IHandler inner, ICache cache) : IHandler;

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    public object? GetService(Type serviceType)
    {
        if(serviceType == typeof(Handler)) return GetHandler();
        if(serviceType == typeof(IHandler)) return GetHandler();
        // ...
    }

    #region Service Resolution

    // Field and lock are placed directly above the resolver method for better readability
    // Field type is IHandler (service type) when decorators are present
    private global::IHandler? _handler;
    private readonly Lock _handlerLock = new();
    private global::IHandler GetHandler()
    {
        if(_handler is not null) return _handler;

        lock(_handlerLock)
        {
            if(_handler is not null) return _handler;

            // Create the inner implementation
            global::IHandler instance = new global::Handler();

            // Apply decorators in order
            // Order in attribute: [LoggingDecorator, CachingDecorator]
            // Wrapping order: CachingDecorator(LoggingDecorator(Handler))
            instance = new global::LoggingDecorator(instance, (global::ILogger)GetRequiredService(typeof(global::ILogger)));
            instance = new global::CachingDecorator(instance, (global::ICache)GetRequiredService(typeof(global::ICache)));

            _handler = instance;
            return instance;
        }
    }

    #endregion
}
#endregion
```

### 6. Module Import

Import registrations from other containers marked with `IocImportModuleAttribute`. By default, the container uses `FrozenDictionary` to combine services from all sources.

#### FrozenDictionary

The `IIocContainer.Services` property from imported modules is used to build the combined dictionary at runtime:

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
    private readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers;

    public AppContainer() : this((IServiceProvider?)null) { }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
        _sharedModule1 = new global::SharedModule1(fallbackProvider);
        _sharedModule2 = new global::SharedModule2(fallbackProvider);

        // Build FrozenDictionary from local services and imported modules
        // Module resolvers are wrapped to pass the correct module instance
        // Local services take precedence (added last, will override imported)
        _serviceResolvers = _sharedModule1.Resolvers.Select(static kvp => 
                new KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>(kvp.Key, c => kvp.Value(c._sharedModule1)))
            .Concat(_sharedModule2.Resolvers.Select(static kvp => 
                new KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>(kvp.Key, c => kvp.Value(c._sharedModule2))))
            .Concat(_localServices)
            .ToFrozenDictionary();
    }

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        // Create scopes for imported modules (so their scoped services are properly isolated)
        _sharedModule1 = (global::SharedModule1)parent._sharedModule1.CreateScope().ServiceProvider;
        _sharedModule2 = (global::SharedModule2)parent._sharedModule2.CreateScope().ServiceProvider;
        _serviceResolvers = parent._serviceResolvers;
    }

    // Local services array with container-typed resolvers
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
    [
        new(new ServiceIdentifier(typeof(global::ILocalService1), KeyedService.AnyKey), static c => c.GetLocalService1()),
        new(new ServiceIdentifier(typeof(global::ILocalService2), KeyedService.AnyKey), static c => c.GetLocalService2()),
        // ... more local services
    ];

    #region IIocContainer

    public IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>> Resolvers => _serviceResolvers;

    #endregion

    #region IServiceProvider

    public object? GetService(Type serviceType)
    {
        if(serviceType == typeof(IServiceProvider)) return this;
        if(serviceType == typeof(IServiceScopeFactory)) return this;
        if(serviceType == typeof(AppContainer)) return this;

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

#### UseSwitchStatement = true

When `UseSwitchStatement` is set to `true`, the container uses cascading `if` statements instead of `FrozenDictionary` for service resolution. This may provide better JIT optimization for small service counts (≤ 50).

> [!NOTE]
> When there are imported modules (`IocImportModuleAttribute`), `UseSwitchStatement` is **ignored** and `FrozenDictionary` is always used, because combining services from multiple sources requires dictionary-based lookup.

```csharp
#region Define:
public interface ILocalService { }

[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILocalService)])]
public class LocalService : ILocalService { }

[IocContainer(UseSwitchStatement = true)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // No _serviceResolvers field when UseSwitchStatement = true

    public object? GetService(Type serviceType)
    {
        if(serviceType == typeof(IServiceProvider)) return this;
        if(serviceType == typeof(IServiceScopeFactory)) return this;
        if(serviceType == typeof(AppContainer)) return this;

        // Cascading if statements instead of dictionary lookup
        if(serviceType == typeof(global::LocalService)) return GetLocalService();
        if(serviceType == typeof(global::ILocalService)) return GetLocalService();

        return _fallbackProvider?.GetService(serviceType);
    }

    public bool IsService(Type serviceType)
    {
        if(serviceType == typeof(IServiceProvider)) return true;
        if(serviceType == typeof(IServiceScopeFactory)) return true;
        if(serviceType == typeof(AppContainer)) return true;

        if(serviceType == typeof(global::LocalService)) return true;
        if(serviceType == typeof(global::ILocalService)) return true;

        return _fallbackProvider is IServiceProviderIsService isService && isService.IsService(serviceType);
    }

    #region IIocContainer

    // Resolvers property returns _localServices when UseSwitchStatement = true
    public IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>> Resolvers => _localServices;

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
    [
        new(new ServiceIdentifier(typeof(global::LocalService), KeyedService.AnyKey), static c => c.GetLocalService()),
        new(new ServiceIdentifier(typeof(global::ILocalService), KeyedService.AnyKey), static c => c.GetLocalService()),
    ];

    #endregion
}
#endregion
```

### 7. Factory and Instance Registration

Support for custom factory methods and static instances:

- **Factory Registration**: Uses field caching for Singleton/Scoped lifetimes to ensure the same instance is returned. Transient factories create a new instance each call.
- **Instance Registration**: Directly returns the pre-existing static instance without field caching. Instance registrations are externally managed and not disposed by the container.

```csharp
#region Define:
public interface IConnection;

public static class ConnectionFactory
{
    public static IConnection Create(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfig>();
        return new Connection(config.ConnectionString);
    }

    public static readonly IConnection Default = new Connection("default");
}

[IocRegisterFor<IConnection>(ServiceLifetime.Singleton, Factory = nameof(ConnectionFactory.Create))]
[IocRegisterFor<IConnection>(ServiceLifetime.Singleton, Key = "default", Instance = nameof(ConnectionFactory.Default))]
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
        // Copy only factory-based singleton fields from parent
        // Instance registrations don't have fields, no need to copy
        _connection = parent._connection;
        _serviceResolvers = parent._serviceResolvers;
    }

    #region Service Resolution

    // Factory registration (Singleton) - uses field for caching
    private global::IConnection? _connection;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private global::IConnection GetConnection()
    {
        if (_connection is not null) return _connection;

        _connectionSemaphore.Wait();
        try
        {
            if (_connection is not null) return _connection;

            var instance = (global::IConnection)global::ConnectionFactory.Create(this);
            _connection = instance;
            return instance;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    // Instance registration - directly returns the static instance, no field needed
    // The instance is externally managed and will NOT be disposed by the container
    private global::IConnection GetDefaultConnection() => global::ConnectionFactory.Default;

    #endregion

    #region Disposal

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope) return;

        // Only dispose factory-created instances
        // Instance registrations are NOT disposed (externally managed)
        DisposeService(_connection);
    }

    #endregion
}
#endregion
```

### 8. Open Generic Support

Handle open generic registrations with closed generic resolution.

> **Note**: The `GetService` snippet below shows `UseSwitchStatement = true` style for clarity.

```csharp
#region Define:
public interface IRepository<T>;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
public class Repository<T> : IRepository<T>;

// Discovered via GetService<IRepository<User>>() call or [IocDiscover]
[IocDiscover<IRepository<User>>]
[IocDiscover<IRepository<Order>>]
[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    #region Service Resolution

    private global::Repository<global::User>? _repositoryUser;
    private readonly SemaphoreSlim _repositoryUserSemaphore = new(1, 1);
    private global::Repository<global::User> GetRepositoryUser()
    {
        if (_repositoryUser is not null) return _repositoryUser;

        _repositoryUserSemaphore.Wait();
        try
        {
            if (_repositoryUser is not null) return _repositoryUser;

            var instance = new global::Repository<global::User>();
            _repositoryUser = instance;
            return instance;
        }
        finally
        {
            _repositoryUserSemaphore.Release();
        }
    }

    private global::Repository<global::Order>? _repositoryOrder;
    private readonly SemaphoreSlim _repositoryOrderSemaphore = new(1, 1);
    private global::Repository<global::Order> GetRepositoryOrder()
    {
        if (_repositoryOrder is not null) return _repositoryOrder;

        _repositoryOrderSemaphore.Wait();
        try
        {
            if (_repositoryOrder is not null) return _repositoryOrder;

            var instance = new global::Repository<global::Order>();
            _repositoryOrder = instance;
            return instance;
        }
        finally
        {
            _repositoryOrderSemaphore.Release();
        }
    }

    #endregion

    public object? GetService(Type serviceType)
    {
        // Closed generic resolution
        if(serviceType == typeof(global::IRepository<global::User>)) return GetRepositoryUser();
        if(serviceType == typeof(global::IRepository<global::Order>)) return GetRepositoryOrder();
        if(serviceType == typeof(global::Repository<global::User>)) return GetRepositoryUser();
        if(serviceType == typeof(global::Repository<global::Order>)) return GetRepositoryOrder();
        // ...
    }
}
#endregion
```

### 9. Collection Resolution

Support for collection resolution when multiple implementations are registered for a service type. Collection resolvers are generated and registered in `_localServices`.

**Supported Collection Types**:

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

    // Registered in _localServices
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localServices =
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

## Configuration Options Behavior

### ResolveIServiceCollection = false

When disabled, the container operates in standalone mode without fallback to external `IServiceProvider`.

> **Note**: The `GetService` snippet below shows `UseSwitchStatement = true` style for clarity.

```csharp
#region Define:
[IocContainer(ResolveIServiceCollection = false)]
public partial class StandaloneContainer;
#endregion

#region Generate:
partial class StandaloneContainer
{
    // No fallback provider field

    #region Constructors

    public StandaloneContainer()
    {
        _serviceResolvers = _localServices.ToFrozenDictionary();
    }

    #endregion

    public object? GetService(Type serviceType)
    {
        if(serviceType == typeof(IServiceProvider)) return this;
        if(serviceType == typeof(IServiceScopeFactory)) return this;
        if(serviceType == typeof(StandaloneContainer)) return this;

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var resolver))
            return resolver(this);

        return null;
    }
}
#endregion
```

**Analyzer Diagnostic**: When a dependency cannot be resolved and `ResolveIServiceCollection = false`, report error `SGIOC018: Unable to resolve service '{ServiceType}' for container '{ContainerType}'.`

### ExplicitOnly = true

Only register services explicitly marked on the container class:

```csharp
#region Define:
// These are NOT registered (not on container class)
[IocRegister<IService1>]
public class Service1 : IService1;

// This IS registered (on container class)
[IocRegisterFor<IService2, Service2>(ServiceLifetime.Singleton)]
[IocContainer(ExplicitOnly = true)]
public partial class ExplicitContainer;
#endregion
```

### IncludeTags

When `IncludeTags` is non-empty, the container only includes services that have at least one matching tag. Services without any tags or with non-matching tags are excluded.

> **Note**: `ExplicitOnly` takes precedence over `IncludeTags`. When `ExplicitOnly = true`, `IncludeTags` is ignored.

```csharp
#region Define:
// This IS registered (has matching tag "Feature1")
[IocRegister<IService1>(ServiceLifetime.Singleton, Tags = ["Feature1"])]
public class Service1 : IService1;

// This IS registered (has matching tag "Feature2")
[IocRegister<IService2>(ServiceLifetime.Singleton, Tags = ["Feature2", "Feature3"])]
public class Service2 : IService2;

// This is NOT registered (no matching tag)
[IocRegister<IService3>(ServiceLifetime.Singleton, Tags = ["Feature3"])]
public class Service3 : IService3;

// This is NOT registered (no tags defined)
[IocRegister<IService4>(ServiceLifetime.Singleton)]
public class Service4 : IService4;

[IocContainer(IncludeTags = ["Feature1", "Feature2"])]
public partial class FeatureContainer;
#endregion
```

**Use Cases**:

1. **Feature Flags**: Include/exclude features at compile time based on build configurations
2. **Module Separation**: Create specialized containers for different deployment scenarios
3. **Testing**: Create test containers with only specific tagged services

### IServiceProviderFactory Implementation

When `ResolveIServiceCollection = true` **AND** the `Microsoft.Extensions.DependencyInjection` package is referenced, the container implements `IServiceProviderFactory<IServiceCollection>` to integrate with ASP.NET Core and other hosts:

```csharp
#region Define:
[IocContainer(ResolveIServiceCollection = true)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer : IServiceProviderFactory<IServiceCollection>, /* ... other interfaces */
{
    private readonly IServiceProvider? _fallbackProvider;

    /// <summary>
    /// Creates a new container builder (returns the same IServiceCollection).
    /// </summary>
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    /// <summary>
    /// Creates the service provider from the built IServiceCollection.
    /// </summary>
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        // Build the fallback provider from IServiceCollection
        var fallbackProvider = containerBuilder.BuildServiceProvider();
        return new AppContainer(fallbackProvider);
    }
}
#endregion
```

**Usage with ASP.NET Core:**

```csharp
var builder = WebApplication.CreateBuilder(args);

// Use the generated container as the service provider factory
builder.Host.UseServiceProviderFactory(new AppContainer());

var app = builder.Build();
```

**Usage with Generic Host:**

```csharp
var host = Host.CreateDefaultBuilder(args)
    .UseServiceProviderFactory(new AppContainer())
    .Build();
```

## Thread Safety

Service resolution is thread-safe by default using `SemaphoreSlim` with double-checked locking pattern. The strategy can be configured via `ThreadSafeStrategy` property on `[IocContainer]` attribute.

See [ThreadSafeStrategy](#threadsafestrategy) for all available strategies and their generated code examples.

**Default (SemaphoreSlim)**:

```csharp
private global::MyService? _myService;
private readonly SemaphoreSlim _myServiceSemaphore = new(1, 1);
private global::MyService GetMyService()
{
    if (_myService is not null) return _myService;

    _myServiceSemaphore.Wait();
    try
    {
        if (_myService is not null) return _myService;

        var instance = new global::MyService(/* ... */);
        _myService = instance;
        return instance;
    }
    finally
    {
        _myServiceSemaphore.Release();
    }
}
```

For complex initialization (property/method injection or decorators), the same strategy is used with additional initialization steps inside the synchronized block:

```csharp
private global::MyService? _myService;
private readonly SemaphoreSlim _myServiceSemaphore = new(1, 1);
private global::MyService GetMyService()
{
    if (_myService is not null) return _myService;

    _myServiceSemaphore.Wait();
    try
    {
        if (_myService is not null) return _myService;

        var instance = new global::MyService(/* ... */)
        {
            Property = (global::IDependency)GetRequiredService(typeof(global::IDependency)),
        };
        instance.Initialize(/* ... */);

        _myService = instance;
        return instance;
    }
    finally
    {
        _myServiceSemaphore.Release();
    }
}
```

## Service Resolution Strategy

When resolving services, the container follows this order:

1. **Built-in services**: `IServiceProvider`, `IServiceScopeFactory`, container type itself
2. **Local services**: Services registered in this container
3. **Imported modules**: Services from `IocImportModuleAttribute`
4. **Fallback provider**: External `IServiceProvider` (if `ResolveIServiceCollection = true`)

For duplicate registrations (same service type and key):

- **Local wins**: Local registrations override imported ones
- **Last wins**: For multiple local registrations, the last one wins (consistent with MS.DI)

## Disposal Order

Services are disposed in reverse registration order:

1. Scoped services (when scope is disposed)
2. Singleton services (when root container is disposed)
3. SemaphoreSlim synchronization primitives (when `ThreadSafeStrategy.SemaphoreSlim` is used)
4. Imported module containers

> **Note**: When using `ThreadSafeStrategy.SemaphoreSlim`, the generated `SemaphoreSlim` fields are disposed along with each service to prevent resource leaks.

```csharp
public void Dispose()
{
    if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

    if(!_isRootScope)
    {
        DisposeService(_scopedService2);
        _scopedService2Semaphore.Dispose();  // SemaphoreSlim disposed with service
        DisposeService(_scopedService1);
        _scopedService1Semaphore.Dispose();
        _sharedModule.Dispose();
        return;
    }

    DisposeService(_singletonService2);
    _singletonService2Semaphore.Dispose();  // SemaphoreSlim disposed with service
    DisposeService(_singletonService1);
    _singletonService1Semaphore.Dispose();
    _sharedModule.Dispose();
}

public async ValueTask DisposeAsync()
{
    if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

    if(!_isRootScope)
    {
        await DisposeServiceAsync(_scopedService2);
        _scopedService2Semaphore.Dispose();  // SemaphoreSlim disposed with service
        await DisposeServiceAsync(_scopedService1);
        _scopedService1Semaphore.Dispose();
        await _sharedModule.DisposeAsync();
        return;
    }

    await DisposeServiceAsync(_singletonService2);
    _singletonService2Semaphore.Dispose();  // SemaphoreSlim disposed with service
    await DisposeServiceAsync(_singletonService1);
    _singletonService1Semaphore.Dispose();
    await _sharedModule.DisposeAsync();
}

private static async ValueTask DisposeServiceAsync(object? service)
{
    if(service is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
    else if(service is IDisposable disposable) disposable.Dispose();
}

private static void DisposeService(object? service)
{
    if(service is IDisposable disposable) disposable.Dispose();
}
```

## Performance Optimization

### Service Resolution Key Structure

All service lookups use `ServiceIdentifier` as the key:

- For non-keyed services: `Key = KeyedService.AnyKey`
- For keyed services: `Key = actual key value`

This unified key structure allows both keyed and non-keyed services to be stored in the same dictionary.

### Resolution Strategy Comparison

|Strategy|Default|When to Use|
|:---|:---|:---|
|`FrozenDictionary`|✓|Most cases, O(1) lookup. **Always used when there are imported modules.**|
|`UseSwitchStatement`||Small service counts (≤ 50), may have better JIT optimization. **Ignored when there are imported modules.**|

See [Basic Container Generation](#1-basic-container-generation) for FrozenDictionary examples and [Keyed Service Support](#3-keyed-service-support) for switch statement examples.

## Implementation Requirements

### Output File Naming

The generated container source file is named: `{ClassName}.Container.g.cs`

For example, a container class `AppContainer` will generate `AppContainer.Container.g.cs`.

### Pipeline Design

```csharp
// In IocSourceGenerator.Initialize()

// ========== IocContainerAttribute provider ==========
var containerProvider = context.SyntaxProvider
    .ForAttributeWithMetadataName(
        Constants.IocContainerAttributeFullName,
        predicate: static (node, _) => node is ClassDeclarationSyntax,
        transform: static (ctx, ct) => TransformContainer(ctx, ct))
    .Where(static m => m is not null)
    .Select(static (m, _) => m!);

// Combine container with existing serviceRegistrations and group them
var containerWithGroups = containerProvider
    .Combine(serviceRegistrations)
    .Select(static (source, _) => GroupRegistrationsForContainer(source.Left, source.Right));

// Combine with compilation info and MSBuild properties
var containerWithCompilationInfo = containerWithGroups
    .Combine(compilationInfoProvider)
    .Combine(msbuildPropertiesProvider);

// Generate Container output (separate from Registration output)
context.RegisterSourceOutput(containerWithCompilationInfo, static (ctx, source) =>
{
    var ((containerWithGroups, compilationInfo), msbuildProps) = source;
    GenerateContainerOutput(in ctx, containerWithGroups, compilationInfo.AssemblyName, msbuildProps, compilationInfo.HasDIPackage);
});
```

### Generation Logic

#### Service Filtering for ExplicitOnly Mode

When `ExplicitOnly = true`, only include registrations that are:

1. Directly marked on the container class via `[IocRegisterFor]`
2. Included via `[IocRegisterDefaults]` on the container class
3. Imported via `[IocImportModule]` on the container class

#### Service Filtering for IncludeTags Mode

When `IncludeTags` is non-empty (and `ExplicitOnly = false`), apply tag filtering:

1. Include services where `Tags` has at least one element in common with `IncludeTags`
2. Exclude services with empty `Tags` array
3. Exclude services where `Tags` has no intersection with `IncludeTags`
4. Services from imported modules via `[IocImportModule]` are NOT filtered by `IncludeTags` (they are included as-is)

**Filtering Priority**:

```markdown
ExplicitOnly = true  →  Only explicit registrations (IncludeTags ignored)
ExplicitOnly = false AND IncludeTags non-empty  →  Tag filtering applied
ExplicitOnly = false AND IncludeTags empty  →  All registrations included
```

### Analyzer Diagnostics

|ID|Severity|Message|Trigger|
|:---|:---|:---|:---|
|`SGIOC018`|Error|Unable to resolve service '{ServiceType}' for container '{ContainerType}'|Unresolvable dependency when `ResolveIServiceCollection = false`|
|`SGIOC019`|Error|Container class '{ClassName}' must be declared as partial and cannot be static|Missing `partial` keyword or has `static` modifier|
|`SGIOC020`|Warning|Container '{ContainerType}' specifies UseSwitchStatement = true but has imported modules|`UseSwitchStatement = true` with `[IocImportModule]`|
