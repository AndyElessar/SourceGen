# Constructor, Property, and Method Injection

## Overview

The container supports all injection patterns including constructor parameters, properties, methods, and optional parameters, consistent with the registration generator.

## Injection Support

Support for all injection patterns consistent with Registration generator:

```csharp
#region Define:
[IocRegister<IMyService>(ServiceLifetime.Scoped)]
public class MyService : IMyService
{
    private readonly IDependency _dep;

    // Constructor injection
    public MyService(IDependency dep, [FromKeyedServices("special")] ISpecial special)
    {
        _dep = dep;
    }

    // Property injection
    [IocInject]
    public ILogger Logger { get; set; } = default!;

    [IocInject(Key = "config")]
    public IConfiguration? Config { get; set; }

    // Method injection
    [IocInject]
    public void Initialize(IInitializer init, [ServiceKey] object? key)
    {
        // ...
    }
}

[IocContainer]
public partial class AppContainer;
#endregion

#region Generate:
partial class AppContainer
{
    // Field and lock for complex initialization (property/method injection)
    private global::MyService? _myService;
    private readonly Lock _myServiceLock = new();
    private global::MyService GetMyService()
    {
        if(_myService is not null) return _myService;

        lock(_myServiceLock)
        {
            if(_myService is not null) return _myService;

            var instance = new global::MyService((global::IDependency)GetRequiredService(typeof(global::IDependency)), (global::ISpecial)GetRequiredKeyedService(typeof(global::ISpecial), "special"))
            {
                Logger = (global::ILogger)GetRequiredService(typeof(global::ILogger)),
                Config = GetKeyedService(typeof(global::IConfiguration), "config") as global::IConfiguration,
            };
            instance.Initialize((global::IInitializer)GetRequiredService(typeof(global::IInitializer)), null);

            _myService = instance;
            return instance;
        }
    }
}
#endregion
```

## See Also

- [Injection Registration](Register.Injection.md)
- [Wrapper Types](Container.Collections.md)
