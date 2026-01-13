namespace SourceGen.Ioc.Benchmark;

/// <summary>
/// Simple service interface for benchmarking.
/// </summary>
public interface ISimpleService
{
    void DoWork();
}

/// <summary>
/// Simple service implementation for benchmarking.
/// </summary>
public sealed class SimpleService : ISimpleService
{
    public void DoWork()
    {
        // Intentionally empty - just for DI benchmarking
    }
}

public interface IHaveInjectService
{
    void DoWork();
}

public sealed class HaveInjectService(ISimpleService simpleService) : IHaveInjectService
{
    private readonly ISimpleService _simpleService = simpleService;

    public void DoWork()
    {
        _simpleService.DoWork();
    }
}

public interface IGenericService<T>
{
    string GetValue();
}

public sealed class GenericService<T> : IGenericService<T>
{
    public string GetValue()
    {
        return typeof(T).FullName!;
    }
}

public interface IInjectGenericService
{
    string GetGenericValue();
}

public sealed class InjectGenericService(IGenericService<int> genericService) : IInjectGenericService
{
    private readonly IGenericService<int> _genericService = genericService;
    public string GetGenericValue()
    {
        return _genericService.GetValue();
    }
}