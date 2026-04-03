namespace IocSample;

public interface IAsyncDependency;

[IocRegister<IAsyncDependency>(ServiceLifetime.Scoped)]
internal sealed class AsyncDependency : IAsyncDependency
{
    [IocInject]
    public async Task InitAsync()
    {
        await Task.Delay(1000);
    }
}

[IocRegister]
internal sealed class AsyncDependentClass(Task<IAsyncDependency> dependency)
{
    private readonly Task<IAsyncDependency> _dependency = dependency;
}
