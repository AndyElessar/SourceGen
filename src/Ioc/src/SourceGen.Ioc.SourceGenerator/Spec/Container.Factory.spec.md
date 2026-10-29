# Factory and Instance Registration

## Overview

Support for custom factory methods and static instances in containers. Factory registrations use field caching for Singleton/Scoped lifetimes while Transient factories create new instances each call. Instance registrations directly return pre-existing static instances.

## Factory and Instance Support

- **Factory Registration**: Uses field caching for Singleton/Scoped lifetimes to ensure the same instance is returned. Transient factories create a new instance each call.
- **Instance Registration**: Directly returns the pre-existing static instance without field caching. Instance registrations are externally managed and not disposed by the container.

```csharp
#region Define:
public interface IConfig { string ConnectionString { get; } }
public interface IConnection;
public sealed class Connection(string connectionString) : IConnection;

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
        // Copy only factory-based singleton fields from parent (an eager singleton by default)
        // Instance registrations don't have fields, no need to copy
        _iConnection_ConnectionFactory_Create = parent._iConnection_ConnectionFactory_Create;
    }

    #region Service Resolution

    // Factory registration (Singleton) - uses field for caching; members are named after the service type and factory
    private global::IConnection _iConnection_ConnectionFactory_Create = null!;
    private global::IConnection GetIConnection_ConnectionFactory_Create() => _iConnection_ConnectionFactory_Create ?? CreateIConnection_ConnectionFactory_Create();

    private global::IConnection CreateIConnection_ConnectionFactory_Create()
    {
        var instance = (global::IConnection)global::ConnectionFactory.Create(this);

        _iConnection_ConnectionFactory_Create = instance;
        return instance;
    }

    #endregion

    // Instance registration - the resolver entry returns the static instance directly, no field or resolver method
    // The instance is externally managed and will NOT be disposed by the container
    private static readonly KeyValuePair<ServiceIdentifier, Func<global::AppContainer, object>>[] _localResolvers =
    [
        // ...built-in services...
        __Resolver(typeof(global::IConnection), static c => c.GetIConnection_ConnectionFactory_Create()),
        __KeyedResolver(typeof(global::IConnection), "default", static _ => global::ConnectionFactory.Default),
    ];

    #region Disposal

    public void Dispose()
    {
        if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;

        if(!_isRootScope)
        {
            DisposeService(_fallbackScope);
            return;
        }

        // Only dispose factory-created instances
        // Instance registrations are NOT disposed (externally managed)
        DisposeService(_iConnection_ConnectionFactory_Create);
        DisposeService(_ownedFallbackProvider);
    }

    #endregion
}
#endregion
```

## See Also

- [Factory Registration](Register.Factory.spec.md)
- [Service Lifetime Management](Container.Lifetime.spec.md)
- [Disposal Order](Container.Performance.spec.md)
