namespace IocSample.Shared;

public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
public interface IQuery<TSelf, TResponse> : IRequest<TSelf, TResponse> where TSelf : IQuery<TSelf, TResponse>;

[IocRegisterDefaults(
    typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>), typeof(HandlerDecorator3<,>)],
    TagOnly = true,
    Tags = ["Mediator"]
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

public sealed record TestRequest2(string Msg) : IQuery<TestRequest2, List<string>>;

[IocRegister]
internal sealed class TestRequest2Handler(ILogger<TestRequest2Handler> logger) : IRequestHandler<TestRequest2, List<string>>
{
    private readonly ILogger<TestRequest2Handler> logger = logger;

    public List<string> Handle(TestRequest2 request)
    {
        logger.Log(nameof(TestRequest2Handler));
        return [request.Msg, request.Msg, request.Msg];
    }
}

public sealed record TestRequest3(string Msg) : IRequest<TestRequest3, int>;
[IocRegister(Decorators = [typeof(HandlerDecorator2<,>), typeof(HandlerDecorator1<,>)])]
internal sealed class TestRequest3Handler(ILogger<TestRequest3Handler> logger) : IRequestHandler<TestRequest3, int>
{
    private readonly ILogger<TestRequest3Handler> logger = logger;

    public int Handle(TestRequest3 request)
    {
        logger.Log(nameof(TestRequest3Handler));
        return request.Msg.Length;
    }
}

public sealed class HandlerDecorator1<TRequest, TResponse>(
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

public sealed class HandlerDecorator2<TRequest, TResponse>(
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

public sealed class HandlerDecorator3<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner
) : IRequestHandler<TRequest, TResponse> where TRequest : IQuery<TRequest, TResponse>
{
    public TResponse Handle(TRequest request)
    {
        Console.WriteLine("HandlerDecorator3: Before handling request");
        var response = inner.Handle(request);
        Console.WriteLine("HandlerDecorator3: After handling request");
        return response;
    }
}
