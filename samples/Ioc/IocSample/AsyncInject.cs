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
