namespace SourceGen.Ioc.TestCase;

/// <summary>Interface for async-initialized service.</summary>
public interface IAsyncInitService
{
    bool IsInitialized { get; }
    string? InitializedBy { get; }
}

internal sealed class AsyncInitService : IAsyncInitService
{
    public bool IsInitialized { get; private set; }
    public string? InitializedBy { get; private set; }

    [IocInject]
    public async Task InitializeAsync(IInjectionDependency dep)
    {
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
