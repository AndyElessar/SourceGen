namespace IocSample;

public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

[IoCRegisterDefaultSettings(
    typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IGenericTest<,>)],
    Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>)]
    )]
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}

public sealed record TestRequest(int Key) : IRequest<TestRequest, List<string>>;

[IoCRegister]
internal sealed class TestHandler : IRequestHandler<TestRequest, List<string>>
{
    public List<string> Handle(TestRequest request)
    {
        return [.. Enumerable.Range(1, request.Key).Select(i => $"Value {i}")];
    }
}

public sealed record TestRequest2(string Msg) : IRequest<TestRequest2, List<string>>;

[IoCRegister]
internal sealed class TestRequest2Handler(ILogger<TestRequest2Handler> logger) : IRequestHandler<TestRequest2, List<string>>
{
    private readonly ILogger<TestRequest2Handler> logger = logger;

    public List<string> Handle(TestRequest2 request)
    {
        logger.Log(nameof(TestRequest2Handler));
        return [request.Msg, request.Msg, request.Msg];
    }
}

public interface ILogger<T>
{
    public void Log(string msg);
}
[IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
public sealed class Logger<T> : ILogger<T>
{
    public void Log(string msg)
    {
        Console.WriteLine(msg);
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