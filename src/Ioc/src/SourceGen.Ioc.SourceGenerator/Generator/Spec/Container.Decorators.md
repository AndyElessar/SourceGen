# Decorator Support in Containers

## Overview

Decorators are resolved in the correct order within the container. When decorators are present, the field type is the service type (interface) rather than the implementation type.

## Decorator Resolution

> **Note**: The `GetService` snippet below shows `UseSwitchStatement = true` style. The decorator resolution logic (`GetHandler`) remains the same regardless of the resolution strategy.

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
        if(serviceType == typeof(IHandler)) return GetHandler();
        // ...
    }

    #region Service Resolution

    // Field and lock are placed directly above the resolver method for better readability
    // Field type is IHandler (service type) when decorators are present
    private global::IHandler? _handler;
    private readonly Lock _handlerLock = new();
    private global::IHandler GetHandler()
    {
        if(_handler is not null) return _handler;

        lock(_handlerLock)
        {
            if(_handler is not null) return _handler;

            // Create the inner implementation
            global::IHandler instance = new global::Handler();

            // Apply decorators in order
            // Order in attribute: [LoggingDecorator, CachingDecorator]
            // Wrapping order: CachingDecorator(LoggingDecorator(Handler))
            instance = new global::LoggingDecorator(instance, (global::ILogger)GetRequiredService(typeof(global::ILogger)));
            instance = new global::CachingDecorator(instance, (global::ICache)GetRequiredService(typeof(global::ICache)));

            _handler = instance;
            return instance;
        }
    }

    #endregion
}
#endregion
```

## See Also

- [Decorator Registration](Register.Decorators.md)
- [Basic Container Generation](Container.Basic.md)
