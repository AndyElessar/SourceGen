using IocSample.Shared;

namespace IocSample;

[IocImportModule(typeof(IRequestHandler<,>))]
public sealed class Module;

public sealed record TestQuery(string Name) : IQuery<TestQuery, string>;

[IocRegister]
internal sealed class TestQueryHandler : IRequestHandler<TestQuery, string>
{
    public string Handle(TestQuery request)
    {
        return $"Hello, {request.Name}!";
    }
}

public sealed record GenericRequest<T>(int Count) : IRequest<GenericRequest<T>, List<T>> where T : new();

[IocRegister]
internal sealed class GenericRequestHandler<T>(ILogger<GenericRequestHandler<T>> logger)
    : IRequestHandler<GenericRequest<T>, List<T>> where T : new()
{
    private readonly ILogger<GenericRequestHandler<T>> logger = logger;

    public List<T> Handle(GenericRequest<T> request)
    {
        return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
    }
}

public class Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

[IocRegister]
internal sealed class ViewModel(IRequestHandler<GenericRequest<Entity>, List<Entity>> handler)
{
    private readonly IRequestHandler<GenericRequest<Entity>, List<Entity>> handler = handler;

    public List<Entity> LoadEntities(int count)
    {
        var request = new GenericRequest<Entity>(count);
        return handler.Handle(request);
    }
}

public sealed record GenericRequest2<T>(int Count) : IRequest<GenericRequest2<T>, List<T>> where T : new();
[IocRegister]
internal sealed class GenericRequestHandler2<T>(ILogger<GenericRequestHandler2<T>> logger)
    : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
{
    private readonly ILogger<GenericRequestHandler2<T>> logger = logger;

    public List<T> Handle(GenericRequest2<T> request)
    {
        return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
    }
}

[IocRegister]
internal sealed class CustomMessenger(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider serviceProvider = serviceProvider;

    public TResponse Send<TRequest, TResponse>(IRequest<TRequest, TResponse> request)
        where TRequest : IRequest<TRequest, TResponse>
    {
        if(request is GenericRequest2<Entity> tr)
        {
            return (TResponse)(object)serviceProvider.GetRequiredService<IRequestHandler<GenericRequest2<Entity>, List<Entity>>>().Handle(tr);
        }
        else
        {
            throw new NotSupportedException($"Request of type {typeof(TRequest).FullName} is not supported.");
        }
    }
}

internal class Entity2;
internal class Entity3;

[IocRegister]
internal sealed class ViewModel2(CustomMessenger customMessenger)
{
    private readonly CustomMessenger customMessenger = customMessenger;

    [IocDiscover(typeof(IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>))]
    public List<Entity2> SendRequest2()
    {
        return customMessenger.Send(new GenericRequest2<Entity2>(5));
    }

    [IocInject]
    public void Initialize(IRequestHandler<GenericRequest2<Entity3>, List<Entity3>> handler)
    {
    }

    [IocInject]
    public IRequestHandler<GenericRequest<Entity2>, List<Entity2>> Handler = null!;

    [IocInject]
    public IRequestHandler<GenericRequest<Entity3>, List<Entity3>> Handler2 { get; init; } = null!;
}
