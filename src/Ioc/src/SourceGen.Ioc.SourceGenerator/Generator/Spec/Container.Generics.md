# Open Generic Support

## Overview

Handle open generic registrations with closed generic resolution. The container efficiently resolves discovered closed generic types through method dispatch or switch statements.

## Open Generic Resolution

```csharp
#region Define:
public interface IRepository<T>;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
public class Repository<T> : IRepository<T>;

// Discovered via GetService<IRepository<User>>() call or [IocDiscover]
[IocDiscover<IRepository<User>>]
[IocDiscover<IRepository<Order>>]
[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    #region Service Resolution

    private global::Repository<global::User>? _repositoryUser;
    private readonly SemaphoreSlim _repositoryUserSemaphore = new(1, 1);
    private global::Repository<global::User> GetRepositoryUser()
    {
        if (_repositoryUser is not null) return _repositoryUser;

        _repositoryUserSemaphore.Wait();
        try
        {
            if (_repositoryUser is not null) return _repositoryUser;

            var instance = new global::Repository<global::User>();
            _repositoryUser = instance;
            return instance;
        }
        finally
        {
            _repositoryUserSemaphore.Release();
        }
    }

    private global::Repository<global::Order>? _repositoryOrder;
    private readonly SemaphoreSlim _repositoryOrderSemaphore = new(1, 1);
    private global::Repository<global::Order> GetRepositoryOrder()
    {
        if (_repositoryOrder is not null) return _repositoryOrder;

        _repositoryOrderSemaphore.Wait();
        try
        {
            if (_repositoryOrder is not null) return _repositoryOrder;

            var instance = new global::Repository<global::Order>();
            _repositoryOrder = instance;
            return instance;
        }
        finally
        {
            _repositoryOrderSemaphore.Release();
        }
    }

    #endregion

    public object? GetService(Type serviceType)
    {
        // Closed generic resolution
        if(serviceType == typeof(global::IRepository<global::User>)) return GetRepositoryUser();
        if(serviceType == typeof(global::IRepository<global::Order>)) return GetRepositoryOrder();
        if(serviceType == typeof(global::Repository<global::User>)) return GetRepositoryUser();
        if(serviceType == typeof(global::Repository<global::Order>)) return GetRepositoryOrder();
        // ...
    }
}
#endregion
```

## See Also

- [Generic Registration](Register.Generics.md)
- [Explicit Closed Generic Discovery](Register.Generics.md)
