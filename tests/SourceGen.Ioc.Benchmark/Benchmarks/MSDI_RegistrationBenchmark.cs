using BenchmarkDotNet.Configs;

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
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MSDI_RegistrationBenchmark
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
        typeServices.AddTransient(typeof(IGenericService<>), typeof(GenericService<>));
        typeServices.AddTransient<IInjectGenericService, InjectGenericService>();
        _providerWithTypeRegistration = typeServices.BuildServiceProvider();

        var factoryServices = new ServiceCollection();
        factoryServices.AddTransient<ISimpleService>(sp => new SimpleService());
        factoryServices.AddTransient<IHaveInjectService>(sp => new HaveInjectService(sp.GetRequiredService<ISimpleService>()));
        factoryServices.AddTransient(typeof(IGenericService<>), typeof(GenericService<>));
        factoryServices.AddTransient<IGenericService<string>, GenericService<string>>();
        factoryServices.AddTransient<IGenericService<int>, GenericService<int>>();
        factoryServices.AddTransient<IInjectGenericService>(sp => new InjectGenericService(sp.GetRequiredService<IGenericService<int>>()));
        _providerWithFactoryRegistration = factoryServices.BuildServiceProvider();
    }

    [BenchmarkCategory("NoInject"), Benchmark(Baseline = true)]
    public ISimpleService Resolve_TypeBased()
    {
        return _providerWithTypeRegistration.GetRequiredService<ISimpleService>();
    }

    [BenchmarkCategory("NoInject"), Benchmark]
    public ISimpleService Resolve_FactoryBased()
    {
        return _providerWithFactoryRegistration.GetRequiredService<ISimpleService>();
    }

    [BenchmarkCategory("HasInject"), Benchmark(Baseline = true)]
    public IHaveInjectService Resolve_TypeBased_HaveInjectService()
    {
        return _providerWithTypeRegistration.GetRequiredService<IHaveInjectService>();
    }

    [BenchmarkCategory("HasInject"), Benchmark]
    public IHaveInjectService Resolve_FactoryBased_HaveInjectService()
    {
        return _providerWithFactoryRegistration.GetRequiredService<IHaveInjectService>();
    }

    [BenchmarkCategory("Generic"), Benchmark(Baseline = true)]
    public IGenericService<string> Resolve_OpenGenericService()
    {
        return _providerWithTypeRegistration.GetRequiredService<IGenericService<string>>();
    }

    [BenchmarkCategory("Generic"), Benchmark]
    public IGenericService<string> Resolve_ClosedGenericService()
    {
        return _providerWithFactoryRegistration.GetRequiredService<IGenericService<string>>();
    }

    [BenchmarkCategory("InjectGeneric"), Benchmark(Baseline = true)]
    public IInjectGenericService Resolve_TypeBased_InjectGenericService()
    {
        return _providerWithTypeRegistration.GetRequiredService<IInjectGenericService>();
    }

    [BenchmarkCategory("InjectGeneric"), Benchmark]
    public IInjectGenericService Resolve_FactoryBased_InjectGenericService()
    {
        return _providerWithFactoryRegistration.GetRequiredService<IInjectGenericService>();
    }
}
