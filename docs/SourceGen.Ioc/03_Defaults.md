# Default Settings

Use `[IocRegisterDefaults<T>]` to define default registration settings for all types that implement or inherit from `T`.

## Basic Defaults

```csharp
// All classes implementing IHandler will be registered as Transient by default
[assembly: IocRegisterDefaults<IHandler>(ServiceLifetime.Transient)]

public interface IHandler;

// Uses default lifetime (Transient) from IHandler
[IocRegister]
internal class MyHandler : IHandler;

// Override the default
[IocRegister(ServiceLifetime.Singleton)]
internal class SingletonHandler : IHandler;
```

## Default Service Types

```csharp
[assembly: IocRegisterDefaults<IRepository>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository)])]

public interface IRepository;

// Automatically registered as IRepository
[IocRegister]
internal class UserRepository : IRepository;
```

## Mark on assembly or marker type

```csharp
// Assembly
[assembly: IocRegisterDefaults<IService>(ServiceLifetime.Scoped)]

// Or marker class
[IocRegisterDefaults<IService>(ServiceLifetime.Scoped)]
public class Marker;
```

## Import Module Defaults

Import default settings from another assembly:

```csharp
// In shared library
public interface IMyService;
public interface IRequestHandler<TRequest, TResponse>;

[assembly: IocRegisterDefaults<IMyService>(ServiceLifetime.Transient)]
[IocRegisterDefaults(typeof(IRequestHandler<,>), ServiceLifetime.Transient)]
public sealed class SharedMarker;

// In consuming project, **only** import defaults from attribute on `SharedMarker` or its assembly
[IocImportModule(typeof(SharedMarker))]
public sealed class Module;
```

## Diagnostics

|ID|Severity|Description|
|:---|:---|:---|
|SGIOC012|Warning|Duplicated `[IocRegisterDefaults]` detected for the same target type and at least one matching tag.|

---

[← Back to Overview](01_Overview.md)
