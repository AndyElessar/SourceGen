# Performance and Advanced Topics

## Overview

Performance optimization strategies, disposal order, eager resolution patterns, and generated code efficiency.

## Disposal Order (LIFO)

Singleton and scoped services are disposed in LIFO (Last In, First Out) order when the container or scope is disposed. Services are disposed in the reverse order they were created/resolved.

### Disposal Field Registration

Only services with `Singleton` or `Scoped` lifetime that implement `IDisposable` or `IAsyncDisposable` are tracked for disposal. Service fields are collected during code generation and stored in the order they are declared.

### Example

```csharp
#region Define:
public interface IConnection : IDisposable;
public interface ILogger : IDisposable;

[IocRegister<IConnection>(ServiceLifetime.Singleton)]
public class Connection : IConnection
{
    public void Dispose() => Console.WriteLine("Connection disposed");
}

[IocRegister<ILogger>(ServiceLifetime.Singleton)]
public class Logger : ILogger
{
    public void Dispose() => Console.WriteLine("Logger disposed");
}

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer : IDisposable, IAsyncDisposable
{
    private int _disposed;
    private Connection? _connection;
    private Logger? _logger;

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // LIFO order: newest services are disposed first
        DisposeService(_logger);
        DisposeService(_connection);

        _logger = null;
        _connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // LIFO order
        await DisposeServiceAsync(_logger);
        await DisposeServiceAsync(_connection);

        _logger = null;
        _connection = null;
    }

    private static void DisposeService(object? obj)
    {
        (obj as IDisposable)?.Dispose();
    }

    private static async ValueTask DisposeServiceAsync(object? obj)
    {
        switch(obj)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
#endregion
```

## Eager Resolution

When `EagerResolveOptions` is set to `Singleton`, `Scoped`, or `SingletonAndScoped`, services are eagerly resolved during container or scope construction.

### Eager Singleton Resolution

```csharp
#region Define:
[IocRegister<IEagerService>(ServiceLifetime.Singleton)]
public class EagerService : IEagerService;

[IocContainer(EagerResolveOptions = EagerResolveOptions.Singleton)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    private static readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers = _localResolvers.ToFrozenDictionary();
    private global::IEagerService _eagerService;  // Not nullable (eagerly initialized)

    public AppContainer() : this((IServiceProvider?)null) { }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;

        // Eagerly resolve singleton services
        _eagerService = GetEagerService_Internal();
    }

    private global::IEagerService GetEagerService_Internal()
    {
        return new global::EagerService();
    }

    // No synchronization field needed for eager services
    // (field is non-nullable and initialized in constructor)
}
#endregion
```

### Eager Scoped Resolution

```csharp
#region Define:
[IocRegister<IEagerScoped>(ServiceLifetime.Scoped)]
public class EagerScoped : IEagerScoped;

[IocContainer(EagerResolveOptions = EagerResolveOptions.Scoped)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    private global::IEagerScoped _eagerScoped;  // Not nullable

    public AppContainer() : this((IServiceProvider?)null) { }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
        _isRootScope = true;
    }

    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;

        // Root scope doesn't have scoped instances yet
        // Eager resolution happens only when creating child scopes

        // Eagerly resolve scoped services for this scope
        _eagerScoped = GetEagerScoped_Internal();
    }

    private global::IEagerScoped GetEagerScoped_Internal()
    {
        return new global::EagerScoped();
    }
}
#endregion
```

## Code Generation Efficiency

### FrozenDictionary vs. Switch Statement

- **`FrozenDictionary`** (default): Efficient for large service sets (>50 services). O(1) average lookup time. **Always used when container has imported modules.**
- **`UseSwitchStatement = true`**: Use cascading `if`/`switch` statements. More efficient for small containers (≤50 services) with JIT inlining. Switch statements allow better branch prediction.

### Example: UseSwitchStatement = true

```csharp
#region Define:
[IocRegister<IService1>]
public class Service1 : IService1;

[IocRegister<IService2>]
public class Service2 : IService2;

[IocRegister<IService3>]
public class Service3 : IService3;

[IocContainer(UseSwitchStatement = true)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();

        // Direct dispatch via switch statement (better branch prediction)
        if(serviceType == typeof(Service1)) return GetService1();
        if(serviceType == typeof(IService1)) return GetService1();
        if(serviceType == typeof(Service2)) return GetService2();
        if(serviceType == typeof(IService2)) return GetService2();
        if(serviceType == typeof(Service3)) return GetService3();
        if(serviceType == typeof(IService3)) return GetService3();

        return _fallbackProvider?.GetService(serviceType);
    }
}
#endregion
```

### Allocation Reduction

1. **Singleton/Scoped caching**: Field-based caching eliminates repeated allocations for stateless services.
2. **FrozenDictionary**: Immutable, no allocation overhead per lookup.
3. **Lazy initialization**: Services are created only when first accessed (unless eager resolution is enabled).
4. **No array allocations in GetService**: Direct dictionary lookup or switch statement dispatch.

## Diagnostics Summary

| ID | Category | Severity | Condition |
| :--- | :--- | :--- | :--- |
| SGIOC018 | Resolver | Error | Could not resolve service type for container with `IntegrateServiceProvider = false` |
| SGIOC019 | Usage | Error | Container class must be declared as partial and cannot be static |
| SGIOC020 | Usage | Warning | `UseSwitchStatement = true` is ignored when container imports modules; `FrozenDictionary` is used instead |
| SGIOC021 | Accessor | Error | Partial accessor return type not resolvable with `IntegrateServiceProvider = false` |

## See Also

- [Service Lifetime Management](Container.Lifetime.spec.md)
- [Container Options](Container.Options.spec.md)
- [Thread Safety](Container.ThreadSafety.spec.md)
- [Disposal and Async Disposal Patterns](Container.Lifetime.spec.md)
