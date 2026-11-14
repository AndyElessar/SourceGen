# Factory Method and Instance Registration

## Overview

Factory method registration allows creating services through custom factory methods. Instance registration allows registering pre-existing static instances. Both support keying and default settings.

## Instance and Factory Precedence

A registration with an `Instance` MUST use it and ignore any `Factory`, whether its own (reported by `SGIOC010`) or the `Factory` of its default settings, in both the Register and [Container](Container.Factory.spec.md) outputs: it is neither created by the factory nor decorated, and has no dependencies. Otherwise, its own `Factory` MUST take precedence over the `Factory` of its default settings.

`Factory` given with `nameof()` MUST be resolved to the method, whose parameters are analyzed below. Any other constant string, a literal or a `const` such as `Factory = Factories.CreatePath`, or a `nameof()` that does not resolve to a method, MUST be used as the method path: its parameters are unknown, so it is called with the service provider only (`Create(sp)` in the Register output) and has no other dependencies. `Instance` MUST be read the same way.

A `Factory` or `Instance` given with `nameof()` MUST keep the type arguments of the member's containing type, such as `Factories<int>.Create` for `Factory = nameof(Factories<int>.Create)` and `Holder<int>.Default` for `Instance = nameof(Holder<int>.Default)`. An `Instance` of an open generic registration whose containing type uses the implementation type's type parameters, such as `Instance = nameof(Default)` on `Node<T>` with `public static readonly Node<T> Default`, or `Instance = nameof(Holder<T>.Default)`, MUST be read from the containing type closed with the type arguments of each closed implementation type (`Node<string>.Default`, `Holder<string>.Default`) for the closed registrations created from it (see [Generic Registration](Register.Generics.spec.md)). Other members, such as `Node.Default` of a non-generic `Node` next to `Node<T>`, MUST be kept unchanged. Its open registration MUST NOT be emitted in the Register output, since MS.DI would construct the implementation type instead of using the instance, so only the closed types discovered at compile time are registered.

## Feature 1: Factory Method Registration

When `Factory` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

### Factory Method Parameter Analysis

- If parameter type is `IServiceProvider`: Pass the service provider directly
- If parameter has `[ServiceKey]` attribute: Pass the registration's Key value, or `default!` when the registration has no Key
- Other parameters: Resolve from the service provider, using the same rules as `[IocInject]` method parameters
- Arguments MUST be passed in the factory method's parameter declaration order, wherever the `IServiceProvider` and `[ServiceKey]` parameters appear (e.g., `Create(ILogger logger, IServiceProvider sp)` is called as `Create(logger, sp)`)

### Service Types Share the Factory Instance

- The factory MUST be called only by the implementation type's registration, which casts the result to the implementation type.
- Each other service type MUST forward to the implementation type (`sp.GetRequiredService<MyService>()`, or `sp.GetRequiredKeyedService<MyService>(key)` when keyed), so all service types of a singleton or scoped registration share one instance. This matches the [Container](Container.Factory.spec.md).
- A service type with `Decorators` MUST wrap that shared instance, using the decorator chain from [Decorators](Register.Decorators.spec.md). Decorators on an `Instance` registration are not applied.
- A generic factory (`TypeParameterCount > 0`, see [Generic Registration](Register.Generics.spec.md)) is closed per service type, so each service type MUST call it separately; its result is the service type's own instance, registered under the hidden key like a decorator chain, and wrapped by the service type's `Decorators`. A registration with async injection methods also calls the factory per service type.
- A closed registration whose `Factory` (its own or from its default settings) is a generic factory, such as `EntityHandler : IRequestHandler<Task<Entity>>` with the generic `Factory` of `[IocRegisterDefaults(typeof(IRequestHandler<>))]`, MUST be generated like a closed generic registration: each service type the mapping matches MUST call the factory with its type arguments (`Create<Entity>`), its parameters substituted (see [Generic Registration](Register.Generics.spec.md#feature-3-generic-factory-type-mapping)), and a service type the mapping does not match MUST NOT be registered (`SGIOC039`). The implementation type MUST be constructed when the mapping does not match it, or not registered when generated code cannot construct it: an interface, an abstract or static class, or a class without a public constructor or an internal one it can access (declared in the same assembly, or in one that gives it access with `InternalsVisibleTo`) (`SGIOC039`); when the mapping matches it, such as the target of `[IocRegisterFor<IRequestHandler<Task<Entity>>>(Factory = nameof(Create))]`, it MUST call the factory like a service type.
- A factory that returns a type other than the implementation type fails the cast at resolution. When the factory chooses the implementation, the registration targets the service type itself (`[IocRegisterFor<IMyService>(Factory = ...)]`). The target type is then the only registered type, and the factory result is returned without a cast:

```csharp
[IocRegisterFor<IMyService>(ServiceLifetime.Singleton, Factory = nameof(MyServiceFactory.Create))]
public static class MyServiceFactory
{
    public static IMyService Create(IServiceProvider sp) => Environment.ProcessorCount > 1 ? new FastService() : new SafeService();
}

// Generate:
services.AddSingleton<IMyService>((IServiceProvider sp) => MyServiceFactory.Create(sp));
```

### Basic Factory Example

```csharp
#region Define:
public interface IMyService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Factory = nameof(MyServiceFactory.Get))]
internal sealed class MyService : IMyService;

public sealed class MyServiceFactory
{
  // Must be static
  // Parameter IServiceProvider is optional
  public static IMyService Get(IServiceProvider sp)
  {
    return new MyService();
  }
}
#endregion

#region Generate:
// The implementation type itself is registered through the factory, cast to it; the service type forwards to it
services.AddSingleton<MyService>((IServiceProvider sp) => (MyService)MyServiceFactory.Get(sp));
services.AddSingleton<IMyService>((IServiceProvider sp) => sp.GetRequiredService<MyService>());
#endregion
```

### Keyed Factory with `[ServiceKey]` parameter

```csharp
#region Define:
public interface IMyService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Key = "MyKey", Factory = nameof(MyServiceFactory.Create))]
internal sealed class MyService : IMyService;

public sealed class MyServiceFactory
{
  public static IMyService Create(IServiceProvider sp, [ServiceKey] string key)
  {
    // Use key to customize creation
    return new MyService();
  }
}
#endregion

#region Generate:
services.AddKeyedSingleton<MyService>("MyKey", (IServiceProvider sp, object? key) => (MyService)MyServiceFactory.Create(sp, "MyKey"));
services.AddKeyedSingleton<IMyService>("MyKey", (IServiceProvider sp, object? key) => sp.GetRequiredKeyedService<MyService>(key));
#endregion
```

### Factory without IServiceProvider parameter

```csharp
#region Define:
public interface IMyService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Factory = nameof(MyServiceFactory.Create))]
internal sealed class MyService : IMyService;

public sealed class MyServiceFactory
{
    public static IMyService Create()
    {
    return new MyService();
    }
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) => (MyService)MyServiceFactory.Create());
services.AddSingleton<IMyService>((IServiceProvider sp) => sp.GetRequiredService<MyService>());
#endregion
```

### Factory in DefaultSettings

When `Factory` is specified in `IocRegisterDefaultsAttribute`, it applies as the default factory for all services implementing the target type. Explicit `Factory` or `Instance` specified in `IocRegisterAttribute` takes precedence over the default (see [Instance and Factory Precedence](#instance-and-factory-precedence)).

**Important**: When using Factory in DefaultSettings:

- The factory method must be compatible with all implementations that match the target service type
- The factory method should typically return the target service type (interface/base class)
- Factory is only applied when the registration doesn't have its own explicit `Factory` or `Instance`

```csharp
#region Define:
public interface IMyHandler { void Handle(); }

// DefaultSettings with Factory - applies to all IMyHandler implementations
[assembly: IocRegisterDefaults(
    typeof(IMyHandler),
    ServiceLifetime.Scoped,
    Factory = nameof(HandlerFactory.Create))]

public static class HandlerFactory
{
    public static IMyHandler Create(IServiceProvider sp)
    {
        // Custom creation logic for all handlers
        var handler = sp.GetRequiredService<MyHandlerImpl>();
        // Additional setup...
        return handler;
    }
}

[IocRegister]
public class MyHandlerImpl : IMyHandler
{
    public void Handle() { }
}

// This handler uses explicit Factory, overrides default
[IocRegister(Factory = nameof(SpecialHandlerFactory.Create))]
public class SpecialHandler : IMyHandler
{
    public void Handle() { }
}

public static class SpecialHandlerFactory
{
    public static IMyHandler Create(IServiceProvider sp) => new SpecialHandler();
}
#endregion

#region Generate:
// MyHandlerImpl uses Factory from DefaultSettings
services.AddScoped<MyHandlerImpl>((IServiceProvider sp) => (MyHandlerImpl)HandlerFactory.Create(sp));
services.AddScoped<IMyHandler>((IServiceProvider sp) => sp.GetRequiredService<MyHandlerImpl>());

// SpecialHandler uses its own explicit Factory
services.AddScoped<SpecialHandler>((IServiceProvider sp) => (SpecialHandler)SpecialHandlerFactory.Create(sp));
services.AddScoped<IMyHandler>((IServiceProvider sp) => sp.GetRequiredService<SpecialHandler>());
#endregion
```

### Generic DefaultSettings with Factory

```csharp
#region Define:
public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

public sealed record QueryRequest : IRequest<QueryRequest, QueryResponse>;
public sealed record QueryResponse;

[assembly: IocRegisterDefaults(typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Factory = nameof(HandlerFactory.CreateHandler))]

public static class HandlerFactory
{
    public static object CreateHandler(IServiceProvider sp, [ServiceKey] object? key)
    {
        // Factory for all IRequestHandler implementations
        // Note: For generic handlers, factory typically creates the concrete handler
        // and returns it as the interface type
        return new QueryHandler();
    }
}

[IocRegister]
public class QueryHandler : IRequestHandler<QueryRequest, QueryResponse>
{
    public QueryResponse Handle(QueryRequest request) => new();
}
#endregion

#region Generate:
// The registration has no Key, so the [ServiceKey] parameter gets default; the object result is cast to the implementation type
services.AddSingleton<QueryHandler>((IServiceProvider sp) => (QueryHandler)HandlerFactory.CreateHandler(sp, default!));
services.AddSingleton<IRequestHandler<QueryRequest, QueryResponse>>((IServiceProvider sp) => sp.GetRequiredService<QueryHandler>());
#endregion
```

## Feature 2: Instance Registration

When `Instance` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

```csharp
#region Define:
public interface IMyService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Instance = nameof(Default))]
internal sealed class MyService : IMyService
{
    // Must be static
    public static MyService Default = new MyService();
}
#endregion

#region Generate:
// When specify Instance, only allow AddSingleton or AddKeyedSingleton
services.AddSingleton<MyService>(MyService.Default);
services.AddSingleton<IMyService>(MyService.Default);
#endregion
```

## See Also

- [Generic Registration](Register.Generics.spec.md)
- [Injection](Register.Injection.spec.md)
- [Container Factory](Container.Factory.spec.md)
