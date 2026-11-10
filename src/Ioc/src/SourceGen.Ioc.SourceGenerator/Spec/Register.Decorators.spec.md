# Decorator Pattern Registration

## Overview

Decorator pattern support allows you to compose multiple decorators around a core service implementation. When decorators are specified, the generator creates factory methods that chain decorators in the correct order.

## Feature: Decorator Pattern

When `Decorators` is not empty, generate register code to handle decorator pattern:\
Only generate decorator when all type arguments constraints are satisfied.\
A type constraint is satisfied when the type argument is the constraint type, or implements or derives from it. Covariant (`out`) type arguments of variant interfaces convert to their reference type supertypes, so `where T : IEnumerable<object>` is satisfied by `List<string>` but not by `List<int>`; contravariant (`in`) and nested conversions are not considered, so such decorators are skipped. The `class`, `struct`, `unmanaged`, and `new()` constraints are checked against the type argument (`notnull` only affects nullable warnings and is not checked).

A decorator is only applied to service types it implements, also for non-generic service types: with `RegisterAllInterfaces = true`, a decorator implementing `IFoo` decorates the `IFoo` registration but not `IBar`. This also applies to closed generic registrations generated from open generic registrations, so a decorator is skipped for each closed service type whose type arguments do not satisfy its constraints.

An open generic decorator's type arguments are found by matching the service type as the decorator implements it against the decorated service type, so type parameter names and order MAY differ from the service type's. Constraints are closed with these type arguments before they are checked, including constraints that refer to other type parameters. A decorator is skipped for a service type it cannot implement.

A non-keyed decorator chain MUST be registered as a keyed service under the hidden key `"SourceGen.Ioc.Decorated:{ServiceType}|{ImplementationType}"` (type names without `global::`), and the service type MUST be registered as a forwarder to it (`sp.GetRequiredKeyedService<IMyService>(hiddenKey)`). Resolving the service type and the `Lazy<T>` / `Func<T>` wrappers of the registration (see [Injection](Register.Injection.spec.md)) therefore share one decorated instance; resolving the implementation type returns the undecorated instance. A keyed decorator chain is registered under its own key.

The decorator chain wraps the instance resolved from the implementation type, including when that instance comes from a non-generic `Factory` (see [Factory](Register.Factory.spec.md)). Decorators are not applied to `Instance` registrations.

### Decorated Parameter

The decorated instance MUST be passed to the first constructor parameter (of the constructor selected like an implementation's: `[IocInject]`, then primary, then most parameters) whose type is the decorated service type, ignoring nullable annotations; for an open generic decorator, the type after closing. The parameter MAY be at any position. Every other parameter MUST be resolved as a dependency, also when its type is the service type, another type the decorator or the implementation implements, or a base type of the service type, so that a decorator gets the same arguments in the Register and Container outputs.

A decorator MUST NOT be applied to a service type when none of its constructor parameters is of that type, since nothing could receive the decorated instance; the analyzer reports `SGIOC035`. More than one parameter of the service type is reported with `SGIOC034`.

```csharp
public sealed class LoggingDecorator(ILogger logger, IHandler inner) : IHandler;          // inner ← decorated instance
public sealed class PairDecorator(IHandler inner, [FromKeyedServices("other")] IHandler other) : IHandler; // other resolved (SGIOC034)
public sealed class FooDecorator(IFoo inner) : IFoo, IBar;                                // not applied to IBar (SGIOC035)
```

### Async Decorators

A decorated service type is async-init when its implementation type has async inject methods, or when a decorator applied to it has `[IocInject]` methods returning `Task` (see [Async Method Injection](Register.Injection.spec.md#async-method-injection)); `AsyncMethodInject` gates both. A decorator only makes the service types it decorates async-init: the implementation type, and service types the decorator does not implement, keep their own shape.

|Rule|Required behavior|
|:---|:----------------|
|Registration|The service type has no synchronous registration: the decorator chain MUST be registered as `Task<ServiceType>` under the registration's own key (no hidden key and no forwarder). Registering `ServiceType` would hand out a decorator whose initialization never ran.|
|Inner instance|The `async Task<ServiceType> Init()` local function MUST await the implementation type's `Task<ImplementationType>` when the implementation type is async-init (`sp.GetRequiredService<Task<MyService>>()`, keyed: `sp.GetRequiredKeyedService<Task<MyService>>(key)`), otherwise resolve the implementation type synchronously.|
|Order|Decorators MUST be applied innermost (last) first. Each decorator is constructed, its properties and fields assigned, its synchronous inject methods called, and its async inject methods awaited in source declaration order, before the next decorator wraps it, so every decorator wraps a fully initialized instance.|

```csharp
#region Define:
public interface IMyService;

[IocRegister<IMyService>(ServiceLifetime.Singleton, Decorators = [typeof(LoggingDecorator), typeof(WarmupDecorator)])]
public class MyService : IMyService;

public class LoggingDecorator(IMyService inner) : IMyService;

public class WarmupDecorator(IMyService inner) : IMyService
{
    [IocInject]
    public Task WarmupAsync(ICache cache) => cache.LoadAsync();
}
#endregion

#region Generate:
services.AddSingleton<MyService, MyService>();
services.AddSingleton<Task<IMyService>>((IServiceProvider sp) =>
{
    async Task<IMyService> Init()
    {
        var s0 = sp.GetRequiredService<MyService>();
        var s1_m0 = sp.GetRequiredService<ICache>();
        var s1 = new WarmupDecorator(s0);
        await s1.WarmupAsync(s1_m0);
        var s2 = new LoggingDecorator(s1);
        return s2;
    }
    return Init();
});
#endregion
```

| Decorator | Service type | Closed decorator |
| :--- | :--- | :--- |
| `Decorator<TResponse, TRequest> : IHandler<TRequest, TResponse>` | `IHandler<Req, Res>` | `Decorator<Res, Req>` |
| `TextDecorator<TRequest> : IHandler<TRequest, string>` | `IHandler<Req, int>` | none |
| `Decorator<TRequest, TResponse> : IHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>` | `IHandler<Req, Other>`, `Req : IRequest<Res>` | none |
| `Decorator<TRequest, TResponse> : IHandler<TRequest, TResponse> where TRequest : TResponse` | `IHandler<Req, IRequest>`, `Req : IRequest` | `Decorator<Req, IRequest>` |

```csharp
#region Define:
public interface IMyService;

[IocRegister(
    Lifetime = ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IMyService)],
    Decorators = [typeof(MyServiceDecorator), typeof(MyServiceDecorator2)])]
public class MyService(ILogger<MyService> logger) : IMyService;

// IocRegister attribute on decorator is optional
[IocRegister]
public class MyServiceDecorator(ILogger<MyServiceDecorator> logger, IMyService myservice) : IMyService
{
    private readonly IMyService myservice = myservice;
    private readonly ILogger<MyServiceDecorator> logger = logger;
}

public class MyServiceDecorator2(ILogger<MyServiceDecorator2> logger, IMyService myservice) : IMyService
{
    private readonly IMyService myservice = myservice;
    private readonly ILogger<MyServiceDecorator2> logger = logger;
}
#endregion

#region Generate:
services.AddSingleton<MyService, MyService>();
services.AddKeyedSingleton<IMyService>("SourceGen.Ioc.Decorated:IMyService|MyService", (IServiceProvider sp, object? key) =>
{
    var s0 = sp.GetRequiredService<MyService>();
    var s1_p0 = sp.GetRequiredService<ILogger<MyServiceDecorator2>>();
    var s1 = new MyServiceDecorator2(s1_p0, s0);
    var s2_p0 = sp.GetRequiredService<ILogger<MyServiceDecorator>>();
    var s2 = new MyServiceDecorator(s2_p0, s1);
    return s2;
});
services.AddSingleton<IMyService>((IServiceProvider sp) => sp.GetRequiredKeyedService<IMyService>("SourceGen.Ioc.Decorated:IMyService|MyService"));
// [IocRegister] on the decorator also registers the decorator itself
services.AddTransient<MyServiceDecorator, MyServiceDecorator>();
#endregion
```

### Generic Decorators

Decorators work seamlessly with generic service types:

```csharp
#region Define:
public interface ILogger<T>
{
    public void Log(string msg);
}
[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
public sealed class Logger<T> : ILogger<T>
{
    public void Log(string msg)
    {
        Console.WriteLine(msg);
    }
}

public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

[IocRegisterDefaults(
    typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>)]
)]
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}

public sealed record TestRequest(int Key) : IRequest<TestRequest, List<string>>;

[IocRegister]
internal sealed class TestHandler : IRequestHandler<TestRequest, List<string>>
{
    public List<string> Handle(TestRequest request)
    {
        return [.. Enumerable.Range(1, request.Key).Select(i => $"Value {i}")];
    }
}

internal sealed class HandlerDecorator1<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner,
    ILogger<HandlerDecorator1<TRequest, TResponse>> logger
) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    private readonly ILogger<HandlerDecorator1<TRequest, TResponse>> logger = logger;

    public TResponse Handle(TRequest request)
    {
        logger.Log(request.ToString() ?? string.Empty);
        Console.WriteLine("HandlerDecorator1: Before handling request");
        var response = inner.Handle(request);
        Console.WriteLine("HandlerDecorator1: After handling request");
        return response;
    }
}

internal sealed class HandlerDecorator2<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner
) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    public TResponse Handle(TRequest request)
    {
        Console.WriteLine("HandlerDecorator2: Before handling request");
        var response = inner.Handle(request);
        Console.WriteLine("HandlerDecorator2: After handling request");
        return response;
    }
}
#endregion

#region Generate:
services.AddKeyedSingleton<IRequestHandler<TestRequest, List<string>>>("SourceGen.Ioc.Decorated:IRequestHandler<TestRequest, System.Collections.Generic.List<string>>|TestHandler", (IServiceProvider sp, object? key) =>
{
    var s0 = sp.GetRequiredService<TestHandler>();
    var s1 = new HandlerDecorator2<TestRequest, List<string>>(s0);
    var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<TestRequest, List<string>>>>();
    var s2 = new HandlerDecorator1<TestRequest, List<string>>(s1, s2_p0);
    return s2;
});
services.AddSingleton<IRequestHandler<TestRequest, List<string>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<TestRequest, List<string>>>("SourceGen.Ioc.Decorated:IRequestHandler<TestRequest, System.Collections.Generic.List<string>>|TestHandler"));
#endregion
```

## See Also

- [Basic Registration](Register.Basic.spec.md)
- [Injection](Register.Injection.spec.md)
- [Container Decorators](Container.Decorators.spec.md)
