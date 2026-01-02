using IocSample.Shared;

namespace IocSample;

public sealed record TestQuery(string Name) : IQuery<TestQuery, string>;

[IoCRegister]
internal sealed class TestHandler : IRequestHandler<TestQuery, string>
{
    public string Handle(TestQuery request)
    {
        return $"Hello, {request.Name}!";
    }
}

public sealed record GenericRequest<T>(int Count) : IRequest<GenericRequest<T>, List<T>> where T : new();

[IoCRegister]
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

[IoCRegister]
internal sealed class ViewModel(IRequestHandler<GenericRequest<Entity>, List<Entity>> handler)
{
    private readonly IRequestHandler<GenericRequest<Entity>, List<Entity>> handler = handler;

    public List<Entity> LoadEntities(int count)
    {
        var request = new GenericRequest<Entity>(count);
        return handler.Handle(request);
    }
}

[IoCRegister]
internal sealed class ViewModel2(GenericRequestHandler<Entity> handler)
{
    private readonly GenericRequestHandler<Entity> handler = handler;

    public List<Entity> LoadEntities(int count)
    {
        var request = new GenericRequest<Entity>(count);
        return handler.Handle(request);
    }
}
