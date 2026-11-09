# Performance and Advanced Topics

## Overview

Performance optimization strategies, disposal order, eager resolution patterns, and generated code efficiency.

## Disposal Order (LIFO)

Singleton and scoped services are disposed in LIFO (Last In, First Out) order when the container or scope is disposed. Services are disposed in the reverse order they were created/resolved.

### Disposal Field Registration

`Singleton` and `Scoped` service fields are passed to `DisposeService`/`DisposeServiceAsync`, which dispose the instance only when it implements `IDisposable` or `IAsyncDisposable` (checked at runtime). Fields are disposed in reverse declaration order.

The generator MUST omit the disposal call of a field whose instance is known at compile time not to implement `IDisposable` or `IAsyncDisposable`. An instance is known when the container creates it with `new`: the outermost decorator for a decorated service, otherwise the implementation type of a registration without `Factory`. Its runtime type is then exactly that type, so its disposability does not depend on the instance.

- A registration with `Factory` (including a generic factory) MUST keep the runtime check, because the factory may return a disposable subtype of its declared return type. Decorators of a factory registration are still created with `new`.
- A type MUST be treated as unknown when it, one of its base classes, or one of its interfaces has a `partial` declaration in source, or when its hierarchy contains an error type: another source generator may add `IDisposable` to it, and the generator cannot see that output.
- A closed generic type is disposable exactly when its open generic definition is, because type arguments cannot add interfaces to a type.
- `Instance` registrations have no field and are never disposed (see [Factory and Instance](Container.Factory.spec.md)).
- The `SemaphoreSlim` of an omitted field is still disposed. A losing `CompareExchange` instance whose type is known not to be disposable is not disposed either.
- When `DisposeAsync` has nothing to await (no disposal calls, imported modules, fallback scope or owned fallback provider), it MUST NOT be an `async` method: it calls `Dispose()` and returns `default`.

`DisposeServiceAsync(object?)` MUST NOT be an `async` method, so that `null` and non-disposable fields cost no state machine: it returns the service's `DisposeAsync()` task directly, or `default` after a synchronous `Dispose()`.

### Example

```csharp
#region Define:
public interface IConnection : IDisposable;
public interface ILogger : IDisposable;
public interface IClock;

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

// Not disposable: no disposal call
[IocRegister<IClock>(ServiceLifetime.Singleton)]
public class Clock : IClock;

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer : IDisposable, IAsyncDisposable
{
    private int _disposed;
    // Eager singletons (the default EagerResolveOptions)
    private global::Connection _connection = null!;
    private global::Logger _logger = null!;
    private global::Clock _clock = null!;

    public void Dispose()
    {
        if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // A scope disposes its scoped services, then its fallback scope
        if(!_isRootScope)
        {
            DisposeService(_fallbackScope);
            return;
        }

        // LIFO order: newest services are disposed first; _clock is known not to be disposable
        DisposeService(_logger);
        DisposeService(_connection);
        DisposeService(_ownedFallbackProvider);
    }

    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
    {
        if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope)
        {
            await DisposeServiceAsync(_fallbackScope).ConfigureAwait(false);
            return;
        }

        // LIFO order
        await DisposeServiceAsync(_logger).ConfigureAwait(false);
        await DisposeServiceAsync(_connection).ConfigureAwait(false);
        await DisposeServiceAsync(_ownedFallbackProvider).ConfigureAwait(false);
    }

    // Not async: null and non-disposable services need no state machine
    private static global::System.Threading.Tasks.ValueTask DisposeServiceAsync(object? service)
    {
        if(service is global::System.IAsyncDisposable asyncDisposable) return asyncDisposable.DisposeAsync();
        if(service is global::System.IDisposable disposable) disposable.Dispose();
        return default;
    }

    private static void DisposeService(object? service)
    {
        if(service is global::System.IDisposable disposable) disposable.Dispose();
        else if(service is global::System.IAsyncDisposable) throw new global::System.InvalidOperationException($"'{service.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
    }
}
#endregion
```

## Eager Resolution

When `EagerResolveOptions` is set to `Singleton`, `Scoped`, or `SingletonAndScoped`, services are eagerly resolved during container or scope construction.

### Eager Singleton Resolution

```csharp
#region Define:
public interface IEagerService;

[IocRegister<IEagerService>(ServiceLifetime.Singleton)]
public class EagerService : IEagerService;

[IocContainer(EagerResolveOptions = EagerResolveOptions.Singleton)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    private static readonly FrozenDictionary<ServiceIdentifier, Func<global::AppContainer, object>> _serviceResolvers = _localResolvers.ToFrozenDictionary();
    private global::EagerService _eagerService = null!;  // Not nullable (eagerly initialized)

    public AppContainer() : this((IServiceProvider?)null) { }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;

        // Initialize eager singletons
        GetEagerService();
    }

    private AppContainer(AppContainer parent)
    {
        // ...
        _eagerService = parent._eagerService;
    }

    private global::EagerService GetEagerService() => _eagerService ?? CreateEagerService();

    // No synchronization field needed for eager singletons:
    // the root constructor creates them before the container can be used from other threads
    private global::EagerService CreateEagerService()
    {
        var instance = new global::EagerService();

        _eagerService = instance;
        return instance;
    }
}
#endregion
```

### Eager Scoped Resolution

```csharp
#region Define:
public interface IEagerScoped;

[IocRegister<IEagerScoped>(ServiceLifetime.Scoped)]
public class EagerScoped : IEagerScoped;

[IocContainer(EagerResolveOptions = EagerResolveOptions.Scoped)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Nullable and synchronized like a lazy scoped service: the root creates it on demand,
    // possibly from several threads
    private global::EagerScoped? _eagerScoped;
    private readonly global::System.Threading.Lock _eagerScopedLock = new();
    private global::EagerScoped GetEagerScoped() => _eagerScoped ?? CreateEagerScoped();

    public AppContainer() : this((IServiceProvider?)null) { }

    public AppContainer(IServiceProvider? fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
        // Eager resolution happens only when creating child scopes
    }

    private AppContainer(AppContainer parent, IIocFallbackScopeOwner? fallbackScopeOwner)
    {
        _fallbackProvider = parent._fallbackProvider;
        _fallbackScopeOwner = fallbackScopeOwner;
        _isRootScope = false;

        // Eagerly resolve scoped services for this scope
        GetEagerScoped();
    }

    private global::EagerScoped CreateEagerScoped()
    {
        lock(_eagerScopedLock)
        {
            if(_eagerScoped is not null) return _eagerScoped;

            var instance = new global::EagerScoped();
            _eagerScoped = instance;
            return instance;
        }
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

        return GetFallbackServiceProvider()?.GetService(serviceType);
    }
}
#endregion
```

### Allocation Reduction

1. **Singleton/Scoped caching**: Field-based caching eliminates repeated allocations for stateless services.
2. **FrozenDictionary**: Immutable, no allocation overhead per lookup.
3. **Lazy initialization**: Services are created only when first accessed (unless eager resolution is enabled).
4. **No array allocations in GetService**: Direct dictionary lookup or switch statement dispatch.

### Compile-Time Cost of Generated Code

The generated container MUST stay cheap for the C# compiler to bind, because it is compiled in every build and analyzed in the IDE.

- Generated files (container and registration) MUST start with `#nullable enable annotations` and `#nullable disable warnings` instead of `#nullable enable`. Annotations keep the nullability of generated signatures, such as partial accessors, while disabled warnings skip nullable flow analysis, which dominated the binding time of the generated container and checks nothing users can act on: for 9,507 `_localResolvers` entries, binding the file took 10.0 s with `#nullable enable` and 1.6 s with annotations only. Generator tests compile generated files with nullable warnings enabled and MUST fail on any warning in generated code.
- `_localResolvers` entries MUST be explicitly typed instead of target-typed `new(...)`. Target-typed entries that take a lambda bind superlinearly in a large array initializer: with nullable analysis enabled, for 3,803 entries, binding took 12.9 s with target-typed `new(...)` and 1.45 s with the file-level `__IocResolver` alias (`KeyValuePair<ServiceIdentifier, Func<TContainer, object>>`).
- Entries MUST be created through the generated helpers `__Resolver(Type, Func)`, `__KeyedResolver(Type, object, Func)` and `__CollectionResolvers<T>(Func)`, which return `__IocResolver` (or `__IocResolver[]`, spread with `..`) and keep the repeated `ServiceIdentifier` / `KeyedService.AnyKey` text out of every entry, so large containers generate less source. The helpers MUST NOT be overloaded, so that binding each lambda argument needs no overload resolution. `__CollectionResolvers<T>` registers one resolver under the `IEnumerable<T>`, `IReadOnlyCollection<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IList<T>` and `T[]` keys; `__KeyedResolver` and `__CollectionResolvers<T>` are generated only when used.

```csharp
// <auto-generated/>
#nullable enable annotations
#nullable disable warnings

using __IocResolver = global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<global::AppContainer, object>>;

private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
[
    __Resolver(typeof(global::IMyService), static c => c.GetMyService()),
    __KeyedResolver(typeof(global::IMyService), "key", static c => c.GetMyService_key_()),
    ..__CollectionResolvers<global::IPlugin>(static c => c.GetAllIPluginArray()),
];

private static __IocResolver __Resolver(global::System.Type serviceType, global::System.Func<global::AppContainer, object> resolver) =>
    new(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey), resolver);
```

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
