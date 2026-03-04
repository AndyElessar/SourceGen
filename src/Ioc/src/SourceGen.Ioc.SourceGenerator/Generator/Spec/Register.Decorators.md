# Decorator Pattern Registration

## Overview

Decorator pattern support allows you to compose multiple decorators around a core service implementation. When decorators are specified, the generator creates factory methods that chain decorators in the correct order.

## Feature: Decorator Pattern

When `Decorators` is not empty, generate register code to handle decorator pattern:\
Only generate decorator when all type arguments constraints are satisfied.

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
service.AddSingleton<MyService>();
service.AddSingleton<IMyService>((IServiceProvider sp) =>
{
    var s0 = sp.GetRequiredService<MyService>();
    var s1_p1 = sp.GetRequiredService<ILogger<MyServiceDecorator2>>();
    var s1 = new MyServiceDecorator2(s1_p1, s0);
    var s2_p1 = sp.GetRequiredService<ILogger<MyServiceDecorator>>();
    var s2 = new MyServiceDecorator(s2_p1, s1);
    return s2;
});
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
services.AddSingleton<IRequestHandler<TestRequest, List<string>>>((IServiceProvider sp) =>
{
    var s0 = sp.GetRequiredService<TestHandler>();

    var s1 = new HandlerDecorator2<TestRequest, List<string>>(s0);

    var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<TestRequest, List<string>>>>();
    var s2 = new HandlerDecorator1<TestRequest, List<string>>(s1, s2_p0);
    return s2;
});
#endregion
```

## See Also

- [Basic Registration](Register.Basic.md)
- [Injection](Register.Injection.md)
- [Container Decorators](Container.Decorators.md)
