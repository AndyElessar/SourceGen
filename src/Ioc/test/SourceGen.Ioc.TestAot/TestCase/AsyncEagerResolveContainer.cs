namespace SourceGen.Ioc.TestAot.TestCase;

public interface IAsyncEagerSingletonService
{
    bool IsInitialized { get; }
}

public static class AsyncEagerSingletonProbe
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

[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncEagerSingletonService)])]
public sealed class AsyncEagerSingletonService : IAsyncEagerSingletonService
{
    public AsyncEagerSingletonService() => AsyncEagerSingletonProbe.OnConstructed();

    public bool IsInitialized { get; private set; }

    [IocInject]
    public async Task InitializeAsync()
    {
        AsyncEagerSingletonProbe.OnInitializeStarted();
        await Task.CompletedTask;
        IsInitialized = true;
    }
}

[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.Singleton)]
public sealed partial class AsyncEagerResolveContainer;