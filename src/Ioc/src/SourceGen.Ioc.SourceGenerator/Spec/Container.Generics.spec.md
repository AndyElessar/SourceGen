# Open Generic Support

## Overview

Handle open generic registrations with closed generic resolution. The container efficiently resolves discovered closed generic types through method dispatch or switch statements.

## Open Generic Resolution

```csharp
#region Define:
public interface IRepository<T>;
public class User;
public class Order;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
public class Repository<T> : IRepository<T>;

// Discovered via GetService<IRepository<User>>() call or [IocDiscover]
[IocDiscover<IRepository<User>>]
[IocDiscover<IRepository<Order>>]
[IocContainer(UseSwitchStatement = true)]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    #region Service Resolution

    // Closed generic members are named after the type arguments
    private global::Repository<global::User>? _repository_User;
    private readonly global::System.Threading.Lock _repository_UserLock = new();
    private global::Repository<global::User> GetRepository_User() => _repository_User ?? CreateRepository_User();

    private global::Repository<global::User> CreateRepository_User()
    {
        lock(_repository_UserLock)
        {
            if(_repository_User is not null) return _repository_User;

            var instance = new global::Repository<global::User>();

            _repository_User = instance;
            return instance;
        }
    }

    private global::Repository<global::Order>? _repository_Order;
    private readonly global::System.Threading.Lock _repository_OrderLock = new();
    private global::Repository<global::Order> GetRepository_Order() => _repository_Order ?? CreateRepository_Order();

    private global::Repository<global::Order> CreateRepository_Order()
    {
        lock(_repository_OrderLock)
        {
            if(_repository_Order is not null) return _repository_Order;

            var instance = new global::Repository<global::Order>();

            _repository_Order = instance;
            return instance;
        }
    }

    #endregion

    public object? GetService(global::System.Type serviceType)
    {
        // ...built-in services...
        // Closed generic resolution (UseSwitchStatement = true)
        if(serviceType == typeof(global::Repository<global::User>)) return GetRepository_User();
        if(serviceType == typeof(global::IRepository<global::User>)) return GetRepository_User();
        if(serviceType == typeof(global::Repository<global::Order>)) return GetRepository_Order();
        if(serviceType == typeof(global::IRepository<global::Order>)) return GetRepository_Order();
        // ...
    }
}
#endregion
```

## See Also

- [Generic Registration](Register.Generics.spec.md)
- [Explicit Closed Generic Discovery](Register.Generics.spec.md)
