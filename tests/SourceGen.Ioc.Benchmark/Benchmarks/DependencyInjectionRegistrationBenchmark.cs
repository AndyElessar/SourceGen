namespace SourceGen.Ioc.Benchmark;

/// <summary>
/// Benchmark comparing different registration methods in Microsoft.Extensions.DependencyInjection.
/// <para>
/// Tests the performance difference between:
/// <list type="bullet">
///   <item><c>services.AddTransient&lt;IService, Service&gt;()</c> - Type-based registration</item>
///   <item><c>services.AddTransient&lt;IService&gt;(sp => new Service())</c> - Factory-based registration</item>
/// </list>
/// </para>
/// </summary>
[MemoryDiagnoser]
[SimpleJob]
public class DependencyInjectionRegistrationBenchmark
{
    private IServiceProvider _providerWithTypeRegistration = null!;
    private IServiceProvider _providerWithFactoryRegistration = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Build providers for resolution benchmarks
        var typeServices = new ServiceCollection();
        typeServices.AddTransient<ISimpleService, SimpleService>();
        typeServices.AddTransient<IHaveInjectService, HaveInjectService>();
        _providerWithTypeRegistration = typeServices.BuildServiceProvider();

        var factoryServices = new ServiceCollection();
        factoryServices.AddTransient<ISimpleService>(sp => new SimpleService());
        factoryServices.AddTransient<IHaveInjectService>(sp => new HaveInjectService(sp.GetRequiredService<ISimpleService>()));
        _providerWithFactoryRegistration = factoryServices.BuildServiceProvider();
    }

    #region Resolution Benchmarks

    /// <summary>
    /// Benchmark: Resolving a service registered with type-based registration
    /// </summary>
    [Benchmark(Baseline = true)]
    public ISimpleService Resolve_TypeBased()
    {
        return _providerWithTypeRegistration.GetRequiredService<ISimpleService>();
    }

    /// <summary>
    /// Benchmark: Resolving a service registered with factory-based registration
    /// </summary>
    [Benchmark]
    public ISimpleService Resolve_FactoryBased()
    {
        return _providerWithFactoryRegistration.GetRequiredService<ISimpleService>();
    }

    /// <summary>
    /// Benchmark: Resolving a service registered with type-based registration
    /// </summary>
    [Benchmark]
    public IHaveInjectService Resolve_TypeBased_HaveInjectService()
    {
        return _providerWithTypeRegistration.GetRequiredService<IHaveInjectService>();
    }

    /// <summary>
    /// Benchmark: Resolving a service registered with factory-based registration
    /// </summary>
    [Benchmark]
    public IHaveInjectService Resolve_FactoryBased_HaveInjectService()
    {
        return _providerWithFactoryRegistration.GetRequiredService<IHaveInjectService>();
    }

    #endregion
}

#region Test Services

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

#endregion
