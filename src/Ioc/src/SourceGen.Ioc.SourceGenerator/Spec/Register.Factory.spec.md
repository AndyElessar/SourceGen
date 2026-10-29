# Factory Method and Instance Registration

## Overview

Factory method registration allows creating services through custom factory methods. Instance registration allows registering pre-existing static instances. Both support keying and default settings.

## Feature 1: Factory Method Registration

When `Factory` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

### Factory Method Parameter Analysis

- If parameter type is `IServiceProvider`: Pass the service provider directly
- If parameter has `[ServiceKey]` attribute: Pass the registration's Key value, or `default!` when the registration has no Key
- Other parameters: Resolve from the service provider, using the same rules as `[IocInject]` method parameters
- Arguments MUST be passed in the factory method's parameter declaration order, wherever the `IServiceProvider` and `[ServiceKey]` parameters appear (e.g., `Create(ILogger logger, IServiceProvider sp)` is called as `Create(logger, sp)`)

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
// The implementation type itself is also registered through the factory, cast to it
services.AddSingleton<MyService>((IServiceProvider sp) => (MyService)MyServiceFactory.Get(sp));
services.AddSingleton<IMyService>((IServiceProvider sp) => MyServiceFactory.Get(sp));
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
services.AddKeyedSingleton<IMyService>("MyKey", (IServiceProvider sp, object? key) => MyServiceFactory.Create(sp, "MyKey"));
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
services.AddSingleton<IMyService>((IServiceProvider sp) => MyServiceFactory.Create());
#endregion
```

### Factory in DefaultSettings

When `Factory` is specified in `IocRegisterDefaultsAttribute`, it applies as the default factory for all services implementing the target type. Explicit `Factory` specified in `IocRegisterAttribute` takes precedence over the default.

**Important**: When using Factory in DefaultSettings:

- The factory method must be compatible with all implementations that match the target service type
- The factory method should typically return the target service type (interface/base class)
- Factory is only applied when the registration doesn't have its own explicit Factory

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
services.AddScoped<IMyHandler>((IServiceProvider sp) => HandlerFactory.Create(sp));

// SpecialHandler uses its own explicit Factory
services.AddScoped<SpecialHandler>((IServiceProvider sp) => (SpecialHandler)SpecialHandlerFactory.Create(sp));
services.AddScoped<IMyHandler>((IServiceProvider sp) => SpecialHandlerFactory.Create(sp));
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
// The registration has no Key, so the [ServiceKey] parameter gets default; the object result is cast to the service type
services.AddSingleton<QueryHandler>((IServiceProvider sp) => (QueryHandler)HandlerFactory.CreateHandler(sp, default!));
services.AddSingleton<IRequestHandler<QueryRequest, QueryResponse>>((IServiceProvider sp) => (IRequestHandler<QueryRequest, QueryResponse>)HandlerFactory.CreateHandler(sp, default!));
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
