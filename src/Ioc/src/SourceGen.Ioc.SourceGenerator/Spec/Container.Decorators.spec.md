# Decorator Support in Containers

## Overview

Decorators are resolved in the correct order within the container. Like the [Register output](Register.Decorators.spec.md), a decorated registration whose service type differs from its implementation type is split into two resolvers:

- The implementation type resolver (`Get{Impl}()`) MUST create, inject and cache the **undecorated** instance. Resolving the implementation type (directly, as a dependency, or as `Task<Impl>` for async-init services) MUST return this instance.
- The service type resolver (`Get{Impl}_As_{ServiceType}()`, field `_{impl}_As_{ServiceType}`) MUST obtain the instance from the implementation type resolver and apply the decorators. Its field type is the service type. For async-init services it MUST await the implementation type's `Task<Impl>` before applying the decorators.

Both resolvers use the registration's lifetime, so for singleton and scoped services the decorator chain wraps the same instance that the implementation type resolves. Both instances are disposed; the decorated instance is disposed first.

With `ThreadSafeStrategy.CompareExchange`, a decorated instance that loses the race MUST NOT be disposed, because it wraps the shared undecorated instance.

Instance registrations and registrations whose service type is the implementation type are not split; their single resolver applies the decorators.

## Decorator Resolution

> **Note**: The `GetService` snippet below shows `UseSwitchStatement = true` style. The decorator resolution logic remains the same regardless of the resolution strategy.

```csharp
#region Define:
public interface IHandler;

[IocRegister<IHandler>(ServiceLifetime.Scoped, Decorators = [typeof(LoggingDecorator), typeof(CachingDecorator)])]
public class Handler : IHandler;

public class LoggingDecorator(IHandler inner, ILogger logger) : IHandler;
public class CachingDecorator(IHandler inner, ICache cache) : IHandler;

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    public object? GetService(Type serviceType)
    {
        if(serviceType == typeof(Handler)) return GetHandler();
        if(serviceType == typeof(IHandler)) return GetHandler_As_IHandler();
        // ...
    }

    #region Service Resolution

    // Implementation type: the undecorated instance
    private global::Handler? _handler;
    private readonly global::System.Threading.Lock _handlerLock = new();
    private global::Handler GetHandler() => _handler ?? CreateHandler();

    private global::Handler CreateHandler()
    {
        lock(_handlerLock)
        {
            if(_handler is not null) return _handler;

            var instance = new global::Handler();

            _handler = instance;
            return instance;
        }
    }

    // Service type: wraps the undecorated instance; field type is IHandler (service type)
    private global::IHandler? _handler_As_IHandler;
    private readonly global::System.Threading.Lock _handler_As_IHandlerLock = new();
    private global::IHandler GetHandler_As_IHandler() => _handler_As_IHandler ?? CreateHandler_As_IHandler();

    private global::IHandler CreateHandler_As_IHandler()
    {
        lock(_handler_As_IHandlerLock)
        {
            if(_handler_As_IHandler is not null) return _handler_As_IHandler;

            global::IHandler instance = GetHandler();

            // Decorators are applied from last to first, so the first one is outermost:
            // CachingDecorator wraps Handler, LoggingDecorator wraps CachingDecorator
            instance = new global::CachingDecorator(instance, (global::ICache)GetRequiredService(typeof(global::ICache)));
            instance = new global::LoggingDecorator(instance, (global::ILogger)GetRequiredService(typeof(global::ILogger)));

            _handler_As_IHandler = instance;
            return instance;
        }
    }

    #endregion
}
#endregion
```

## See Also

- [Decorator Registration](Register.Decorators.spec.md)
- [Basic Container Generation](Container.Basic.spec.md)
