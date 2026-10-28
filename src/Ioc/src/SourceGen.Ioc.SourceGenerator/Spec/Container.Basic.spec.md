# Basic Container Generation

## Overview

Basic container generation creates a `partial class` that implements multiple DI-related interfaces. The container provides high-performance dependency injection suitable for both standalone use and integration with `Microsoft.Extensions.DependencyInjection`.

## Generated Interfaces

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
|`IServiceProviderFactory<IServiceCollection>`|Build container from IServiceCollection (only when `IntegrateServiceProvider = true` AND DI package is referenced)|
|`IControllerActivator`|Generates MVC controller activation (only when container class declares this interface AND `Microsoft.AspNetCore.Mvc.Core` package is referenced)|
|`IComponentActivator`|Generates Blazor component activation (only when container class declares this interface AND `Microsoft.AspNetCore.Components` package is referenced)|
|`IComponentPropertyActivator`|Generates Blazor component property activation (only when container class declares this interface AND `Microsoft.AspNetCore.Components` package version ≥ 11 is referenced, which defines this type)|
|`IDisposable`|Synchronous disposal|
|`IAsyncDisposable`|Asynchronous disposal|

## Generated Extension Methods

The container generates generic instance methods matching the signatures from `ServiceProviderServiceExtensions` and `ServiceProviderKeyedServiceExtensions`. These are generated as instance methods on the container class, avoiding the overhead of the non-generic `GetService(Type)` path.

|Method|Return Type|Constraint|
|:---|:---|:---|
|`GetService<T>()`|`T?`|`where T : class`|
|`GetRequiredService<T>()`|`T`|`where T : notnull`|
|`GetServices<T>()`|`IEnumerable<T>`|None|
|`GetKeyedService<T>(object? serviceKey)`|`T?`|`where T : class`|
|`GetRequiredKeyedService<T>(object? serviceKey)`|`T`|`where T : notnull`|
|`GetKeyedServices<T>(object? serviceKey)`|`IEnumerable<T>`|None|

### Resolution strategy by mode

- **FrozenDictionary mode** (default): Methods directly query `_serviceResolvers` FrozenDictionary for O(1) lookup, bypassing the non-generic `GetService(Type)` overhead. On a miss they MUST fall back exactly like their non-generic counterparts (`GetFallbackServiceProvider()`, or its `IKeyedServiceProvider` for keyed methods, passing the original `serviceKey`), so `container.GetRequiredService<T>()` and `((IServiceProvider)container).GetRequiredService<T>()` behave the same. `Required` methods MUST throw `InvalidOperationException` only when neither the container nor the fallback provider resolves the service; `GetServices<T>` / `GetKeyedServices<T>` MUST return an empty sequence in that case. Without `IntegrateServiceProvider`, a miss returns `null` / empty or throws directly.
- **Switch mode** (`UseSwitchStatement = true`): Methods delegate to their non-generic counterparts (e.g., `GetService<T>()` calls `GetService(typeof(T)) as T`).

## Construction, Scopes, and Fallback Provider

|Rule|Required behavior|
|:---|:---|
|Constructor order|Constructors MUST initialize Lazy/Func wrapper fields **before** eager services, because eager services may inject those fields.|
|Resolver entries|Resolver entries and switch branches MUST call the service's resolver method (e.g. `GetMyService()`), never read an eager field directly: a factory or `IServiceProvider` call running during construction may resolve a service whose eager field is still unset.|
|Resolver shape|Synchronous singleton/scoped resolvers MUST be split into an expression-bodied fast path `GetX() => _x ?? CreateX();` and a slow-path `CreateX()` that holds creation, synchronization, and root delegation, so the fast path stays small enough to inline.|
|Singletons in scopes|A scope MUST NOT create a singleton. Lazily created singletons (non-eager, including async-init) MUST delegate to the root container through `_root` (see [Lifetime](Container.Lifetime.spec.md)).|
|Fallback scope|When `IntegrateServiceProvider = true`, fallback resolution MUST go through `GetFallbackServiceProvider()`. In a container scope it MUST lazily create one scope on the fallback provider (via its `IServiceScopeFactory`, if available) on the first fallback resolution, publish it with `Interlocked.CompareExchange`, and dispose a losing duplicate. Scopes that never use the fallback provider MUST NOT create one. The scope MUST dispose the fallback scope after its own scoped services. `IsService`/`IsKeyedService` MAY query the root fallback provider directly.|
|Fallback ownership|The provider built by `IServiceProviderFactory.CreateServiceProvider` MUST be disposed by the returned root container. A provider passed to the public constructor is owned by the caller and MUST NOT be disposed.|
|Scoped services in the root|A scoped service resolved directly from the root container is cached in the root. Both `Dispose` and `DisposeAsync` MUST dispose scoped services first (in scopes and in the root), then singletons.|
|Transient disposables|Transient services are NOT tracked. The container MUST NOT dispose them; the caller owns their lifetime.|
|`IAsyncDisposable`-only services|Synchronous `Dispose` MUST throw `InvalidOperationException` when it reaches a tracked service that implements only `IAsyncDisposable` (matching `Microsoft.Extensions.DependencyInjection`); `DisposeAsync` MUST be used instead. Disposing a losing `CompareExchange` instance during resolution MUST NOT throw.|
|Fully qualified names|Generated container code MUST reference every type with a `global::`-qualified name, so types declared in the container's namespace (e.g. `Lock`, `Task`, `Type`, `System`) cannot shadow them. The `using` directives are kept only for extension methods.|
|Awaits|Every `await` in generated code MUST use `ConfigureAwait(false)`.|

> **Limitation**: An imported module scope is created through the module's own `CreateScope()` and therefore opens its own fallback scope. Within one container scope, fallback scoped services resolved through an imported module are a different instance from those resolved through the importing container.

## Member Names

Generated service fields and resolver methods are named after short type names, so large containers stay small:

|Name part|Rule|
|---|---|
|Short type name|The simple type name with namespaces and containing types removed, also in generic arguments; non-identifier characters become a single `_` and trailing `_` is dropped: `global::A.Logger<global::A.Request, global::System.Collections.Generic.List<string>>` → `Logger_Request_List_string`.|
|Collisions|When different types of one container get the same short name, each of them MUST get `_` and the 8-digit hex FNV-1a hash of its fully qualified name appended (e.g. `Service_4b7ca1a9`). The hash MUST be deterministic (not `string.GetHashCode`).|
|Length|A short name longer than 80 characters MUST be truncated to 60 characters and get the hash appended.|
|Members|Resolver `Get{Name}()` (creation `Create{Name}()`, async `Get{Name}Async()` / `Create{Name}Async()`), field `_{name}` (synchronization `_{name}Lock` / `Semaphore` / `SpinLock`), keyed `…_{key}`, decorated service type `…_As_{ServiceName}`, wrapper fields `_lazy_{ServiceName}{field}` / `_func_{ServiceName}{field}`, collections `GetAll{Name}Array()`, `GetAllLazy_{Name}_Array()`, `GetAllFunc_{Name}_Array()`, `GetAllKvp_{Key}_{Value}_Array()` / `_Dictionary()`.|
|Reserved names|A resolver or field name MUST get `_Resolve` appended when it, or a member derived from it, equals a member the user declares on the container class or its base classes (including partial accessors), an imported module field, or a member of the generated container infrastructure (e.g. `GetService`, `CreateScope`, `_root`).|

Names are computed while grouping registrations and stored in the container entries, so the emitters never recompute them.

## Basic Container Example

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
// NOTE: type names are abbreviated for readability; generated code uses global::-qualified names.
// <auto-generated/>
#nullable enable annotations
#nullable disable warnings

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc;
using __IocResolver = global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<global::AppContainer, object>>;

partial class AppContainer : IIocContainer<global::AppContainer>, IServiceProvider, IKeyedServiceProvider,
    IServiceProviderIsService, IServiceProviderIsKeyedService, ISupportRequiredService,
    IServiceScopeFactory, IServiceScope, IDisposable, IAsyncDisposable, IServiceProviderFactory<IServiceCollection>
{
    private readonly IServiceProvider? _fallbackProvider;
    private IServiceScope? _fallbackScope; // created lazily in container scopes
    private readonly bool _isRootScope = true;
    private int _disposed;

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

        // Initialize eager singletons (Get methods resolve dependencies first and assign the fields)
        GetMyDependency();
        GetMyService();
    }

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        // Copy eager singleton references from parent
        _myDependency = parent._myDependency;
        _myService = parent._myService;
    }

    #endregion

    #region Service Resolution

    // Eager singleton - non-nullable field, no synchronization
    private global::MyDependency _myDependency = null!;
    private global::MyDependency GetMyDependency() => _myDependency ?? CreateMyDependency();

    private global::MyDependency CreateMyDependency()
    {
        var instance = new global::MyDependency();
        _myDependency = instance;
        return instance;
    }

    // Eager singleton - non-nullable field, no synchronization
    private global::MyService _myService = null!;
    private global::MyService GetMyService() => _myService ?? CreateMyService();

    private global::MyService CreateMyService()
    {
        // A registered dependency is resolved through its resolver directly
        var instance = new global::MyService(GetMyDependency());
        _myService = instance;
        return instance;
    }

    #endregion

    #region IServiceProvider

    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var resolver))
            return resolver(this);

        return GetFallbackServiceProvider()?.GetService(serviceType);
    }

    private IServiceProvider? GetFallbackServiceProvider()
    {
        if(_isRootScope || _fallbackProvider is null) return _fallbackProvider;

        return (_fallbackScope ?? CreateFallbackScope(_fallbackProvider))?.ServiceProvider ?? _fallbackProvider;
    }

    private IServiceScope? CreateFallbackScope(IServiceProvider fallbackProvider)
    {
        if(fallbackProvider.GetService(typeof(IServiceScopeFactory)) is not IServiceScopeFactory scopeFactory) return null;

        var scope = scopeFactory.CreateScope();
        var existing = Interlocked.CompareExchange(ref _fallbackScope, scope, null);
        if(existing is null) return scope;

        scope.Dispose();
        return existing;
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

    #region ServiceProvider Extensions

    public T? GetService<T>() where T : class
    {
        ThrowIfDisposed();
        return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), KeyedService.AnyKey), out var resolver)
            ? resolver(this) as T
            : GetFallbackServiceProvider()?.GetService(typeof(T)) as T;
    }

    public T GetRequiredService<T>() where T : notnull
    {
        ThrowIfDisposed();
        return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), KeyedService.AnyKey), out var resolver)
            ? (T)resolver(this)
            : (T)(GetFallbackServiceProvider()?.GetService(typeof(T)) ?? throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered."));
    }

    public IEnumerable<T> GetServices<T>() { /* ... */ }
    public T? GetKeyedService<T>(object? serviceKey) where T : class { /* ... */ }
    public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull { /* ... */ }
    public IEnumerable<T> GetKeyedServices<T>(object? serviceKey) { /* ... */ }

    #endregion

    #region IServiceProviderIsService

    public bool IsService(Type serviceType)
    {
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

    public static IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>> Resolvers => _serviceResolvers;

    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        // Built-in services
        __Resolver(typeof(IServiceProvider), static c => c),
        __Resolver(typeof(IServiceScopeFactory), static c => c),
        __Resolver(typeof(global::AppContainer), static c => c),
        // Eager singletons - resolver method calls (fields may still be unset while the constructor runs)
        __Resolver(typeof(global::MyDependency), static c => c.GetMyDependency()),
        __Resolver(typeof(global::IMyDependency), static c => c.GetMyDependency()),
        __Resolver(typeof(global::MyService), static c => c.GetMyService()),
        __Resolver(typeof(global::IMyService), static c => c.GetMyService()),
    ];

    // Entry helper (see Container.Performance.spec.md for __KeyedResolver and __CollectionResolvers<T>)
    private static __IocResolver __Resolver(global::System.Type serviceType, global::System.Func<global::AppContainer, object> resolver) =>
        new(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey), resolver);

    private static readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers = _localResolvers.ToFrozenDictionary();
    // WARNING: _localResolvers MUST be declared before _serviceResolvers because C# static field initializers execute in textual (declaration) order. If reversed, _localResolvers would be null when _serviceResolvers initializes.

    #endregion

    #region IServiceProviderFactory<IServiceCollection>

    /// <summary>
    /// Creates a new container builder (returns the same IServiceCollection).
    /// </summary>
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    // Provider built by CreateServiceProvider is owned (and disposed) by the returned container
    private object? _ownedFallbackProvider;

    /// <summary>
    /// Creates the service provider from the built IServiceCollection.
    /// </summary>
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        var fallbackProvider = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(containerBuilder);
        return new AppContainer(fallbackProvider) { _ownedFallbackProvider = fallbackProvider };
    }

    #endregion

    #region Disposal

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope)
        {
            // Scoped services first, then the fallback scope they may depend on
            DisposeService(_fallbackScope);
            return;
        }

        // Eager singletons - no SemaphoreSlim to dispose
        DisposeService(_myService);
        DisposeService(_myDependency);
        DisposeService(_ownedFallbackProvider);
    }

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Scoped services (none in this example) are disposed here, before the branch,
        // because the root caches scoped services resolved directly from it.

        if(!_isRootScope)
        {
            await DisposeServiceAsync(_fallbackScope).ConfigureAwait(false);
            return;
        }

        // Eager singletons - no SemaphoreSlim to dispose
        await DisposeServiceAsync(_myService).ConfigureAwait(false);
        await DisposeServiceAsync(_myDependency).ConfigureAwait(false);
        await DisposeServiceAsync(_ownedFallbackProvider).ConfigureAwait(false);
    }

    private static async ValueTask DisposeServiceAsync(object? service)
    {
        if(service is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if(service is IDisposable disposable) disposable.Dispose();
    }

    private static void DisposeService(object? service)
    {
        if(service is IDisposable disposable) disposable.Dispose();
        else if(service is IAsyncDisposable) throw new InvalidOperationException($"'{service.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
    }

    // Containers with async-init services also emit overloads for their Task<T>? fields:
    // DisposeServiceAsync<T>(Task<T>? task) and DisposeService<T>(Task<T>? task) (see Container.Injection)

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, GetType());
    }

    #endregion
}
#endregion
```

## See Also

- [Lifetime Management](Container.Lifetime.spec.md)
- [Keyed Services](Container.KeyedServices.spec.md)
- [Container Options](Container.Options.spec.md)
