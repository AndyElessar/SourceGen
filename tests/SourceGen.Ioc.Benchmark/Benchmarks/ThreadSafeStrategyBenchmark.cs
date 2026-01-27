using BenchmarkDotNet.Configs;

namespace SourceGen.Ioc.Benchmark.Benchmarks;

/// <summary>
/// Benchmark comparing different ThreadSafeStrategy options for singleton resolution.
/// <para>
/// Tests the performance difference between:
/// <list type="bullet">
///   <item><c>ThreadSafeStrategy.None</c> - No synchronization (fastest but not thread-safe)</item>
///   <item><c>ThreadSafeStrategy.Lock</c> - Uses lock statement</item>
///   <item><c>ThreadSafeStrategy.SemaphoreSlim</c> - Uses SemaphoreSlim (default, async-friendly)</item>
///   <item><c>ThreadSafeStrategy.SpinLock</c> - Uses SpinLock (best for short operations)</item>
/// </list>
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ThreadSafeStrategyBenchmark
{
    private ServiceProvider _serviceProvider = null!;
    private BenchmarkContainerNone _containerNone = null!;
    private BenchmarkContainerLock _containerLock = null!;
    private BenchmarkContainerSemaphoreSlim _containerSemaphoreSlim = null!;
    private BenchmarkContainerSpinLock _containerSpinLock = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _serviceProvider = new ServiceCollection()
            .AddSingleton<ISingletonBenchmarkService, SingletonBenchmarkService>()
            .BuildServiceProvider();

        _containerNone = new BenchmarkContainerNone();
        _containerLock = new BenchmarkContainerLock();
        _containerSemaphoreSlim = new BenchmarkContainerSemaphoreSlim();
        _containerSpinLock = new BenchmarkContainerSpinLock();

        // Warm up - pre-resolve singleton instances
        _ = _serviceProvider.GetRequiredService<ISingletonBenchmarkService>();
        _ = _containerNone.GetRequiredService<ISingletonBenchmarkService>();
        _ = _containerLock.GetRequiredService<ISingletonBenchmarkService>();
        _ = _containerSemaphoreSlim.GetRequiredService<ISingletonBenchmarkService>();
        _ = _containerSpinLock.GetRequiredService<ISingletonBenchmarkService>();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _serviceProvider.Dispose();
        _containerNone.Dispose();
        _containerLock.Dispose();
        _containerSemaphoreSlim.Dispose();
        _containerSpinLock.Dispose();
    }

    #region Single-threaded Resolution (after initialization)

    [BenchmarkCategory("Resolve_SingleThreaded"), Benchmark(Baseline = true)]
    public ISingletonBenchmarkService Resolve_MSDI()
    {
        return _serviceProvider.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("Resolve_SingleThreaded"), Benchmark]
    public ISingletonBenchmarkService Resolve_None()
    {
        return _containerNone.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("Resolve_SingleThreaded"), Benchmark]
    public ISingletonBenchmarkService Resolve_Lock()
    {
        return _containerLock.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("Resolve_SingleThreaded"), Benchmark]
    public ISingletonBenchmarkService Resolve_SemaphoreSlim()
    {
        return _containerSemaphoreSlim.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("Resolve_SingleThreaded"), Benchmark]
    public ISingletonBenchmarkService Resolve_SpinLock()
    {
        return _containerSpinLock.GetRequiredService<ISingletonBenchmarkService>();
    }

    #endregion

    #region Concurrent Resolution (simulating multi-threaded access)

    private const int ConcurrentTasks = 16;

    [BenchmarkCategory("Resolve_Concurrent"), Benchmark(Baseline = true)]
    public async Task<ISingletonBenchmarkService[]> ConcurrentResolve_MSDI()
    {
        var tasks = new Task<ISingletonBenchmarkService>[ConcurrentTasks];
        for(var i = 0; i < ConcurrentTasks; i++)
        {
            tasks[i] = Task.Run(() => _serviceProvider.GetRequiredService<ISingletonBenchmarkService>());
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Resolve_Concurrent"), Benchmark]
    public async Task<ISingletonBenchmarkService[]> ConcurrentResolve_None()
    {
        var tasks = new Task<ISingletonBenchmarkService>[ConcurrentTasks];
        for(var i = 0; i < ConcurrentTasks; i++)
        {
            tasks[i] = Task.Run(() => _containerNone.GetRequiredService<ISingletonBenchmarkService>());
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Resolve_Concurrent"), Benchmark]
    public async Task<ISingletonBenchmarkService[]> ConcurrentResolve_Lock()
    {
        var tasks = new Task<ISingletonBenchmarkService>[ConcurrentTasks];
        for(var i = 0; i < ConcurrentTasks; i++)
        {
            tasks[i] = Task.Run(() => _containerLock.GetRequiredService<ISingletonBenchmarkService>());
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Resolve_Concurrent"), Benchmark]
    public async Task<ISingletonBenchmarkService[]> ConcurrentResolve_SemaphoreSlim()
    {
        var tasks = new Task<ISingletonBenchmarkService>[ConcurrentTasks];
        for(var i = 0; i < ConcurrentTasks; i++)
        {
            tasks[i] = Task.Run(() => _containerSemaphoreSlim.GetRequiredService<ISingletonBenchmarkService>());
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Resolve_Concurrent"), Benchmark]
    public async Task<ISingletonBenchmarkService[]> ConcurrentResolve_SpinLock()
    {
        var tasks = new Task<ISingletonBenchmarkService>[ConcurrentTasks];
        for(var i = 0; i < ConcurrentTasks; i++)
        {
            tasks[i] = Task.Run(() => _containerSpinLock.GetRequiredService<ISingletonBenchmarkService>());
        }

        return await Task.WhenAll(tasks);
    }

    #endregion

    #region First-time Initialization (cold start)

    [BenchmarkCategory("FirstInit"), Benchmark(Baseline = true)]
    public ISingletonBenchmarkService FirstInit_MSDI()
    {
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<ISingletonBenchmarkService, SingletonBenchmarkService>()
            .BuildServiceProvider();

        return serviceProvider.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("FirstInit"), Benchmark]
    public ISingletonBenchmarkService FirstInit_None()
    {
        using var container = new BenchmarkContainerNone();

        return container.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("FirstInit"), Benchmark]
    public ISingletonBenchmarkService FirstInit_Lock()
    {
        using var container = new BenchmarkContainerLock();

        return container.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("FirstInit"), Benchmark]
    public ISingletonBenchmarkService FirstInit_SemaphoreSlim()
    {
        using var container = new BenchmarkContainerSemaphoreSlim();

        return container.GetRequiredService<ISingletonBenchmarkService>();
    }

    [BenchmarkCategory("FirstInit"), Benchmark]
    public ISingletonBenchmarkService FirstInit_SpinLock()
    {
        using var container = new BenchmarkContainerSpinLock();

        return container.GetRequiredService<ISingletonBenchmarkService>();
    }

    #endregion
}
