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
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class DependencyInjectionRegistrationBenchmark
{
    private ServiceCollection _services = null!;
    private IServiceProvider _providerWithTypeRegistration = null!;
    private IServiceProvider _providerWithFactoryRegistration = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Build providers for resolution benchmarks
        var typeServices = new ServiceCollection();
        typeServices.AddTransient<ISimpleService, SimpleService>();
        _providerWithTypeRegistration = typeServices.BuildServiceProvider();

        var factoryServices = new ServiceCollection();
        factoryServices.AddTransient<ISimpleService>(sp => new SimpleService());
        _providerWithFactoryRegistration = factoryServices.BuildServiceProvider();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _services = new ServiceCollection();
    }

    #region Resolution Benchmarks

    /// <summary>
    /// Benchmark: Resolving a service registered with type-based registration
    /// </summary>
    [Benchmark]
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

#endregion
