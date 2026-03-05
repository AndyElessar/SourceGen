# Factory and Instance Registration

## Overview

Support for custom factory methods and static instances in containers. Factory registrations use field caching for Singleton/Scoped lifetimes while Transient factories create new instances each call. Instance registrations directly return pre-existing static instances.

## Factory and Instance Support

- **Factory Registration**: Uses field caching for Singleton/Scoped lifetimes to ensure the same instance is returned. Transient factories create a new instance each call.
- **Instance Registration**: Directly returns the pre-existing static instance without field caching. Instance registrations are externally managed and not disposed by the container.

```csharp
#region Define:
public interface IConnection;

public static class ConnectionFactory
{
    public static IConnection Create(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfig>();
        return new Connection(config.ConnectionString);
    }

    public static readonly IConnection Default = new Connection("default");
}

[IocRegisterFor<IConnection>(ServiceLifetime.Singleton, Factory = nameof(ConnectionFactory.Create))]
[IocRegisterFor<IConnection>(ServiceLifetime.Singleton, Key = "default", Instance = nameof(ConnectionFactory.Default))]
[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    private AppContainer(AppContainer parent)
    {
        _fallbackProvider = parent._fallbackProvider;
        _isRootScope = false;
        // Copy only factory-based singleton fields from parent
        // Instance registrations don't have fields, no need to copy
        _connection = parent._connection;
        _serviceResolvers = parent._serviceResolvers;
    }

    #region Service Resolution

    // Factory registration (Singleton) - uses field for caching
    private global::IConnection? _connection;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private global::IConnection GetConnection()
    {
        if (_connection is not null) return _connection;

        _connectionSemaphore.Wait();
        try
        {
            if (_connection is not null) return _connection;

            var instance = (global::IConnection)global::ConnectionFactory.Create(this);
            _connection = instance;
            return instance;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    // Instance registration - directly returns the static instance, no field needed
    // The instance is externally managed and will NOT be disposed by the container
    private global::IConnection GetDefaultConnection() => global::ConnectionFactory.Default;

    #endregion

    #region Disposal

    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope) return;

        // Only dispose factory-created instances
        // Instance registrations are NOT disposed (externally managed)
        DisposeService(_connection);
    }

    #endregion
}
#endregion
```

## See Also

- [Factory Registration](Register.Factory.spec.md)
- [Service Lifetime Management](Container.Lifetime.spec.md)
- [Disposal Order](Container.Performance.spec.md)
