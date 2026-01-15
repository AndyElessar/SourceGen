namespace IocSample.Shared;

public interface ILogger<T>
{
    public void Log(string msg);
}

[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
internal sealed class Logger<T> : ILogger<T>
{
    public void Log(string msg)
    {
        Console.WriteLine(msg);
    }
}
