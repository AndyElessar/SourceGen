namespace SourceGen.Ioc.TestCase;

/// <summary>Interface for async-initialized service.</summary>
public interface IAsyncInitService
{
    bool IsInitialized { get; }
    string? InitializedBy { get; }
}

public static class AsyncInitServiceProbe
{
    private static int constructedCount;
    private static int initializeStartedCount;

    public static int ConstructedCount => global::System.Threading.Volatile.Read(ref constructedCount);
    public static int InitializeStartedCount => global::System.Threading.Volatile.Read(ref initializeStartedCount);

    public static void Reset()
    {
        global::System.Threading.Interlocked.Exchange(ref constructedCount, 0);
        global::System.Threading.Interlocked.Exchange(ref initializeStartedCount, 0);
    }

    internal static void OnConstructed() => global::System.Threading.Interlocked.Increment(ref constructedCount);

    internal static void OnInitializeStarted() => global::System.Threading.Interlocked.Increment(ref initializeStartedCount);
}

internal sealed class AsyncInitService : IAsyncInitService
{
    public AsyncInitService() => AsyncInitServiceProbe.OnConstructed();

    public bool IsInitialized { get; private set; }
    public string? InitializedBy { get; private set; }

    [IocInject]
    public async Task InitializeAsync(IInjectionDependency dep)
    {
        AsyncInitServiceProbe.OnInitializeStarted();
        await Task.CompletedTask;
        InitializedBy = dep.Name;
        IsInitialized = true;
    }
}

[IocRegisterFor<AsyncInitService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncInitService)])]
[IocContainer(ExplicitOnly = true, ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim, EagerResolveOptions = EagerResolveOptions.None)]
public sealed partial class AsyncInjectionModule
{
    /// <summary>Async accessor — generated as <c>async Task&lt;IAsyncInitService&gt;</c> → awaits the internal resolver.</summary>
    public partial Task<IAsyncInitService> GetAsyncInitServiceAsync();
}

/// <summary>Async-init service resolved concurrently to verify single initialization.</summary>
public interface IContendedAsyncInitService;

public static class ContendedAsyncInitServiceProbe
{
    private static int initializeCount;

    public static int InitializeCount => global::System.Threading.Volatile.Read(ref initializeCount);

    public static void Reset() => global::System.Threading.Interlocked.Exchange(ref initializeCount, 0);

    internal static void OnInitialize() => global::System.Threading.Interlocked.Increment(ref initializeCount);
}

internal sealed class ContendedAsyncInitService : IContendedAsyncInitService
{
    [IocInject]
    public async Task InitializeAsync()
    {
        ContendedAsyncInitServiceProbe.OnInitialize();
        await Task.Delay(10);
    }
}

// CompareExchange is overridden to Lock for async-init services, so initialization never runs twice.
[IocRegisterFor<ContendedAsyncInitService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IContendedAsyncInitService)])]
[IocContainer(ExplicitOnly = true, ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange, EagerResolveOptions = EagerResolveOptions.None)]
public sealed partial class CompareExchangeAsyncInjectionModule
{
    public partial Task<IContendedAsyncInitService> GetContendedAsyncInitServiceAsync();
}
