# Default Settings

Use `[IoCRegisterDefaults<T>]` to define default registration settings for all types that implement or inherit from `T`.

## Basic Defaults

```csharp
// All classes implementing IHandler will be registered as Transient by default
[IoCRegisterDefaults<IHandler>(ServiceLifetime.Transient)]
public interface IHandler;

// Uses default lifetime (Transient) from IHandler
[IoCRegister]
internal class MyHandler : IHandler;

// Override the default
[IoCRegister(ServiceLifetime.Singleton)]
internal class SingletonHandler : IHandler;
```

## Default Service Types

```csharp
[IoCRegisterDefaults<IRepository>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository)])]
public interface IRepository;

// Automatically registered as IRepository
[IoCRegister]
internal class UserRepository : IRepository;
```

## Default Decorators

```csharp
[IoCRegisterDefaults<IHandler>(
    ServiceLifetime.Transient,
    Decorators = [typeof(LoggingDecorator<>), typeof(CachingDecorator<>)])]
public interface IHandler;
```

## Exclude from Defaults

```csharp
// Set ExcludeFromDefault in defaults
[IoCRegisterDefaults<IHandler>(ServiceLifetime.Transient, ExcludeFromDefault = true)]
public interface IHandler;

// Or exclude specific registration
[IoCRegister(ExcludeFromDefault = true)]
internal class SpecialHandler : IHandler;
```

## Assembly-Level Defaults

```csharp
[assembly: IoCRegisterDefaults<IService>(ServiceLifetime.Scoped)]
```

## Import Module Defaults

Import default settings from another assembly:

```csharp
// In shared library
[IoCRegisterDefaults(typeof(IRequestHandler<,>), ServiceLifetime.Transient)]
public interface IRequestHandler<TRequest, TResponse>;

// In consuming project
[ImportModule(typeof(IRequestHandler<,>))]
public sealed class Module;
```