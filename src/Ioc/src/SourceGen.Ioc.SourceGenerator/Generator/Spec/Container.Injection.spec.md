# Constructor, Property, and Method Injection

## Overview

The container supports all injection patterns including constructor parameters, properties, synchronous methods, awaited async methods, and optional parameters, consistent with the registration generator.

## Injection Support

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

## Async Method Injection

When a registration contains one or more async inject methods, the container MUST switch that implementation path from synchronous `T` resolution to asynchronous `Task<T>` resolution.

### Resolution Rules

|Condition|Required behavior|
|:--------|:----------------|
|Registration contains one or more `InjectionMemberType.AsyncMethod` members|The container MUST generate an async resolver path that returns `Task<ImplementationType>`. Service-type aliases MUST project from that resolver as `Task<ServiceType>`.|
|Singleton or scoped async-init registration|The container MUST cache `Task<ImplementationType>` in a field.|
|Transient async-init registration|The container MUST create a new `Task<ImplementationType>` per resolution and MUST NOT cache it.|
|Multiple service-type aliases resolve the same implementation, key, and instance/factory identity|The container MUST share a single cached `Task<ImplementationType>` field. The deduplication key MUST be `(ImplementationType, Key, InstanceOrFactory)`. Service type is **not** part of the cache key.|
|`ThreadSafeStrategy.None`|Allowed. The container MAY assign the task field directly without synchronization.|
|`ThreadSafeStrategy.SemaphoreSlim`|Allowed. Singleton/scoped async-init services MUST use `WaitAsync()` / `Release()` around first initialization.|
|`ThreadSafeStrategy.Lock`, `ThreadSafeStrategy.SpinLock`, or `ThreadSafeStrategy.CompareExchange`|Async-incompatible for async-init services and MUST NOT be used for that resolver path.|
|`EagerResolveOptions` includes singleton and/or scoped services|Async-init services MUST be excluded from eager resolution. The container constructor/scope constructor MUST NOT pre-start those tasks.|
|Collection wrappers (`IEnumerable<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, `IList<T>`, `T[]`)|Async-init registrations MUST be excluded from collection resolvers. `IEnumerable<Task<T>>` is not supported.|

```mermaid
flowchart LR
    A[Task<IFoo> accessor] --> C[Shared Task<FooBar> cache\nkey=(ImplType, Key, InstanceOrFactory)]
    B[Task<IBar> accessor] --> C
    C --> D[Construct FooBar]
    D --> E[Assign properties]
    E --> F[Assign fields]
    F --> G[Call sync inject methods]
    G --> H[await async inject methods]
    H --> I[Return completed Task<FooBar>]
```

### Shared `Task<ImplType>` Field Across Aliases

Async-init services MUST follow the **same implementation-based field deduplication** as synchronous services. If one implementation is registered for multiple service types, all aliases MUST reuse the same cached `Task<ImplementationType>` field.

```csharp
#region Define:
using System.Threading.Tasks;

public interface IFoo { }
public interface IBar { }
public interface ILogger { }
public interface IAsyncInitializer
{
    Task InitializeAsync(object instance);
}

[IocRegister(ServiceTypes = [typeof(IFoo), typeof(IBar)], Lifetime = ServiceLifetime.Singleton)]
public sealed class FooBar : IFoo, IBar
{
    [IocInject]
    public ILogger Logger { get; set; } = default!;

    [IocInject]
    public void InitializeSync()
    {
    }

    [IocInject]
    public async Task InitializeAsync(IAsyncInitializer initializer)
    {
        await initializer.InitializeAsync(this);
    }
}

[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim)]
public partial class AppContainer
{
    public partial Task<IFoo> GetFooAsync();
    public partial Task<IBar> GetBarAsync();
}
#endregion

#region Generate:
partial class AppContainer
{
    private global::System.Threading.Tasks.Task<global::FooBar>? _fooBar;
    private readonly global::System.Threading.SemaphoreSlim _fooBarSemaphore = new(1, 1);

    public AppContainer(global::System.IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;

        // Async-init singleton is excluded from eager resolution.
        // _fooBar = GetFooBarAsync(); // MUST NOT be emitted
    }

    private async global::System.Threading.Tasks.Task<global::FooBar> GetFooBarAsync()
    {
        if(_fooBar is not null)
            return await _fooBar;

        await _fooBarSemaphore.WaitAsync();
        try
        {
            if(_fooBar is null)
            {
                _fooBar = CreateFooBarAsync();
            }
        }
        finally
        {
            _fooBarSemaphore.Release();
        }

        return await _fooBar;
    }

    private async global::System.Threading.Tasks.Task<global::FooBar> CreateFooBarAsync()
    {
        var instance = new global::FooBar
        {
            Logger = (global::ILogger)GetRequiredService(typeof(global::ILogger)),
        };

        instance.InitializeSync();
        await instance.InitializeAsync((global::IAsyncInitializer)GetRequiredService(typeof(global::IAsyncInitializer)));
        return instance;
    }

    public partial async global::System.Threading.Tasks.Task<global::IFoo> GetFooAsync() => await GetFooBarAsync();

    public partial async global::System.Threading.Tasks.Task<global::IBar> GetBarAsync() => await GetFooBarAsync();
}
#endregion
```

```csharp
// Invalid outcome (must not happen): aliases must not get separate task caches.
private global::System.Threading.Tasks.Task<global::FooBar>? _fooBar_IFoo;
private global::System.Threading.Tasks.Task<global::FooBar>? _fooBar_IBar;
```

## Disposal of Async-init Fields

Cached async-init singleton/scoped services use fields of type `Task<T>?`. Generated disposal code MUST unwrap the completed service instance before calling `DisposeServiceAsync` or `DisposeService`.

### Disposal Rules

|Disposal path|Field type|Required generated pattern|Forbidden pattern|
|:---|:---|:---|:---|
|`DisposeAsync`|`Task<T>?`|`if(_field is not null) await DisposeServiceAsync(await _field);`|`await DisposeServiceAsync(_field);`|
|`Dispose`|`Task<T>?`|`if(_field is not null) DisposeService(_field.ConfigureAwait(false).GetAwaiter().GetResult());`|`DisposeService(_field);`|

These rules apply regardless of the container's `ThreadSafeStrategy`. Disposal behavior depends on the cached field type (`Task<T>?`), not on the synchronization primitive used during first initialization.

### Example

```csharp
#region Generate:
partial class AppContainer : IAsyncDisposable, IDisposable
{
    private global::System.Threading.Tasks.Task<global::FooBar>? _fooBar;

    public async ValueTask DisposeAsync()
    {
        if(_fooBar is not null)
            await DisposeServiceAsync(await _fooBar);
    }

    public void Dispose()
    {
        if(_fooBar is not null)
            DisposeService(_fooBar.ConfigureAwait(false).GetAwaiter().GetResult());
    }
}
#endregion
```

```csharp
// Invalid outcome (must not happen): the disposal helpers operate on the resolved service instance, not Task<T>.
public async ValueTask DisposeAsync()
{
    if(_fooBar is not null)
        await DisposeServiceAsync(_fooBar);
}

public void Dispose()
{
    if(_fooBar is not null)
        DisposeService(_fooBar);
}
```

## See Also

- [Injection Registration](Register.Injection.spec.md)
- [Wrapper Types](Container.Collections.spec.md)
