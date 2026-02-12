using BenchmarkDotNet.Configs;

namespace SourceGen.Ioc.Benchmark.Benchmarks;

/// <summary>
/// Benchmark comparing different <see cref="ThreadSafeStrategy"/> options using a realistic dependency graph.
/// <para>
/// Tests the performance difference between:
/// <list type="bullet">
///   <item><c>ThreadSafeStrategy.None</c> - No synchronization (fastest but not thread-safe)</item>
///   <item><c>ThreadSafeStrategy.Lock</c> - Uses lock statement (default)</item>
///   <item><c>ThreadSafeStrategy.SemaphoreSlim</c> - Uses SemaphoreSlim (async-friendly)</item>
///   <item><c>ThreadSafeStrategy.SpinLock</c> - Uses SpinLock (best for short operations)</item>
/// </list>
/// Each scenario resolves <see cref="IRequestHandler{TRequest, TResponse}"/> for <see cref="GetUserRequest"/>,
/// exercising the full multi-layer dependency graph (Singleton → Scoped → Transient).
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ThreadSafeStrategyBenchmark
{
    private ServiceProvider _msdiProvider = null!;
    private RealisticContainerNone _containerNone = null!;
    private RealisticContainerLock _containerLock = null!;
    private RealisticContainerSemaphoreSlim _containerSemaphoreSlim = null!;
    private RealisticContainerSpinLock _containerSpinLock = null!;
    private RealisticContainerCompareExchange _containerCompareExchange = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _msdiProvider = MsdiHelper.CreateServiceProvider();

        _containerNone = new RealisticContainerNone();
        _containerLock = new RealisticContainerLock();
        _containerSemaphoreSlim = new RealisticContainerSemaphoreSlim();
        _containerSpinLock = new RealisticContainerSpinLock();
        _containerCompareExchange = new RealisticContainerCompareExchange();

        // Warm up all singletons
        MsdiHelper.WarmUpSingletons(_msdiProvider);
        MsdiHelper.WarmUpSingletons(_containerNone);
        MsdiHelper.WarmUpSingletons(_containerLock);
        MsdiHelper.WarmUpSingletons(_containerSemaphoreSlim);
        MsdiHelper.WarmUpSingletons(_containerSpinLock);
        MsdiHelper.WarmUpSingletons(_containerCompareExchange);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _msdiProvider.Dispose();
        _containerNone.Dispose();
        _containerLock.Dispose();
        _containerSemaphoreSlim.Dispose();
        _containerSpinLock.Dispose();
        _containerCompareExchange.Dispose();
    }

    #region Synchronous Resolution (scope → resolve handler)

    [BenchmarkCategory("Sync"), Benchmark(Baseline = true)]
    public IRequestHandler<GetUserRequest, GetUserResponse> Sync_MSDI()
    {
        using var scope = _msdiProvider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("Sync"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> Sync_None()
    {
        using var scope = _containerNone.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("Sync"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> Sync_Lock()
    {
        using var scope = _containerLock.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("Sync"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> Sync_SemaphoreSlim()
    {
        using var scope = _containerSemaphoreSlim.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("Sync"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> Sync_SpinLock()
    {
        using var scope = _containerSpinLock.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("Sync"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> Sync_CompareExchange()
    {
        using var scope = _containerCompareExchange.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    #endregion

    #region Asynchronous Concurrent Resolution (16 parallel full-request tasks)

    private const int ConcurrentTasks = 16;

    [BenchmarkCategory("Async"), Benchmark(Baseline = true)]
    public async Task<GetUserResponse[]> Async_MSDI()
    {
        var tasks = new Task<GetUserResponse>[ConcurrentTasks];

        for(var i = 0; i < ConcurrentTasks; i++)
        {
            var userId = i + 1;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = _msdiProvider.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();

                return await handler.HandleAsync(new GetUserRequest(userId));
            });
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Async"), Benchmark]
    public async Task<GetUserResponse[]> Async_None()
    {
        var tasks = new Task<GetUserResponse>[ConcurrentTasks];

        for(var i = 0; i < ConcurrentTasks; i++)
        {
            var userId = i + 1;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = _containerNone.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();

                return await handler.HandleAsync(new GetUserRequest(userId));
            });
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Async"), Benchmark]
    public async Task<GetUserResponse[]> Async_Lock()
    {
        var tasks = new Task<GetUserResponse>[ConcurrentTasks];

        for(var i = 0; i < ConcurrentTasks; i++)
        {
            var userId = i + 1;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = _containerLock.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();

                return await handler.HandleAsync(new GetUserRequest(userId));
            });
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Async"), Benchmark]
    public async Task<GetUserResponse[]> Async_SemaphoreSlim()
    {
        var tasks = new Task<GetUserResponse>[ConcurrentTasks];

        for(var i = 0; i < ConcurrentTasks; i++)
        {
            var userId = i + 1;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = _containerSemaphoreSlim.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();

                return await handler.HandleAsync(new GetUserRequest(userId));
            });
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Async"), Benchmark]
    public async Task<GetUserResponse[]> Async_SpinLock()
    {
        var tasks = new Task<GetUserResponse>[ConcurrentTasks];

        for(var i = 0; i < ConcurrentTasks; i++)
        {
            var userId = i + 1;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = _containerSpinLock.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();

                return await handler.HandleAsync(new GetUserRequest(userId));
            });
        }

        return await Task.WhenAll(tasks);
    }

    [BenchmarkCategory("Async"), Benchmark]
    public async Task<GetUserResponse[]> Async_CompareExchange()
    {
        var tasks = new Task<GetUserResponse>[ConcurrentTasks];

        for(var i = 0; i < ConcurrentTasks; i++)
        {
            var userId = i + 1;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = _containerCompareExchange.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();

                return await handler.HandleAsync(new GetUserRequest(userId));
            });
        }

        return await Task.WhenAll(tasks);
    }

    #endregion

    #region Cold Start (new container → scope → resolve handler)

    [BenchmarkCategory("ColdStart"), Benchmark(Baseline = true)]
    public IRequestHandler<GetUserRequest, GetUserResponse> ColdStart_MSDI()
    {
        using var provider = MsdiHelper.CreateServiceProvider();
        using var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("ColdStart"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> ColdStart_None()
    {
        using var container = new RealisticContainerNone();
        using var scope = container.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("ColdStart"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> ColdStart_Lock()
    {
        using var container = new RealisticContainerLock();
        using var scope = container.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("ColdStart"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> ColdStart_SemaphoreSlim()
    {
        using var container = new RealisticContainerSemaphoreSlim();
        using var scope = container.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("ColdStart"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> ColdStart_SpinLock()
    {
        using var container = new RealisticContainerSpinLock();
        using var scope = container.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    [BenchmarkCategory("ColdStart"), Benchmark]
    public IRequestHandler<GetUserRequest, GetUserResponse> ColdStart_CompareExchange()
    {
        using var container = new RealisticContainerCompareExchange();
        using var scope = container.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IRequestHandler<GetUserRequest, GetUserResponse>>();
    }

    #endregion
}
