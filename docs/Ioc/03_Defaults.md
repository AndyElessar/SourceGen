# Default Settings

Use `[IocRegisterDefaults<T>]` to define default registration settings for all types that implement or inherit from `T`.

## Basic Defaults

```csharp
// All classes implementing IHandler will be registered as Transient by default
// Automatically registered as IHandler
[assembly: IocRegisterDefaults<IHandler>(ServiceLifetime.Transient)]

public interface IHandler;

// Need mark with [IocRegister]
// Uses default lifetime (Transient) from IHandler
[IocRegister]
internal class MyHandler : IHandler;

// Override the default
[IocRegister(ServiceLifetime.Singleton)]
internal class SingletonHandler : IHandler;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddTransient<MyHandler, MyHandler>();
services.AddTransient<IHandler>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyHandler>());
services.AddSingleton<SingletonHandler, SingletonHandler>();
services.AddSingleton<IHandler>((global::System.IServiceProvider sp) => sp.GetRequiredService<SingletonHandler>());
```

</details>

## Multiple Service Types

Use `ServiceTypes` register multiple service types:

```csharp
[assembly: IocRegisterDefaults<IMyService1>(
  ServiceLifetime.Transient,
  ServiceTypes = [typeof(IMyService2)])]

public interface IMyService1;
public interface IMyService2;

[IocRegister]
internal class MyService : IMyService1, IMyService2;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddTransient<MyService, MyService>();
services.AddTransient<IMyService1>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
services.AddTransient<IMyService2>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
```

</details>

## Implementation Types

Use `ImplementationTypes` to directly register implementation types without marking each one with `[IocRegister]` or `[IocRegisterFor]`:

```csharp
[assembly: IocRegisterDefaults<IMyService>(
    ServiceLifetime.Scoped,
    ImplementationTypes = [typeof(MyService), typeof(AnotherService)])]

public interface IMyService;

// Registered directly via ImplementationTypes - no [IocRegister] or [IocRegisterFor] needed
public class MyService : IMyService;
public class AnotherService : IMyService;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddScoped<MyService, MyService>();
services.AddScoped<IMyService>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
services.AddScoped<AnotherService, AnotherService>();
services.AddScoped<IMyService>((global::System.IServiceProvider sp) => sp.GetRequiredService<AnotherService>());
```

</details>

### With Service Types

Combine `ImplementationTypes` with `ServiceTypes` to register implementations for multiple service types:

```csharp
[assembly: IocRegisterDefaults<IBaseService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(ISecondaryService)],
    ImplementationTypes = [typeof(MyService)])]

public interface IBaseService;
public interface ISecondaryService;

// Registered as IBaseService and ISecondaryService
public class MyService : IBaseService, ISecondaryService;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddSingleton<MyService, MyService>();
services.AddSingleton<ISecondaryService>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
services.AddSingleton<IBaseService>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
```

</details>

### With Decorators

Apply decorators to implementation types:

```csharp
[assembly: IocRegisterDefaults<IMyService>(
    ServiceLifetime.Scoped,
    Decorators = [typeof(LoggingDecorator)],
    ImplementationTypes = [typeof(MyService)])]

public interface IMyService { void DoWork(); }

public class MyService : IMyService
{
    public void DoWork() { }
}

public class LoggingDecorator(IMyService inner) : IMyService
{
    public void DoWork()
    {
        Console.WriteLine("Before");
        inner.DoWork();
        Console.WriteLine("After");
    }
}
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddScoped<MyService, MyService>();
services.AddScoped<IMyService>((IServiceProvider sp) =>
{
    var s0 = sp.GetRequiredService<MyService>();
    var s1 = new LoggingDecorator(s0);
    return s1;
});
```

</details>

### With Tags

Use tags to control which implementations are registered when calling the generated method with tags:

```csharp
[assembly: IocRegisterDefaults<IMyService>(
    ServiceLifetime.Scoped,
    Tags = ["Production"],
    ImplementationTypes = [typeof(ProductionService)])]

[assembly: IocRegisterDefaults<IMyService>(
    ServiceLifetime.Scoped,
    Tags = ["Development"],
    ImplementationTypes = [typeof(MockService)])]

public interface IMyService;

public class ProductionService : IMyService;
public class MockService : IMyService;

// Usage:
// - AddMyAssembly() registers services without tags only (none in this example)
// - AddMyAssembly(["Production"]) registers ProductionService
// - AddMyAssembly(["Development"]) registers MockService
```

<details>
<summary>Generated Code</summary>

```csharp
public static IServiceCollection AddMyAssembly(this IServiceCollection services, params IEnumerable<string> tags)
{
    if (tags.Contains("Production"))
    {
        services.AddScoped<ProductionService, ProductionService>();
        services.AddScoped<IMyService>((global::System.IServiceProvider sp) => sp.GetRequiredService<ProductionService>());
    }

    if (tags.Contains("Development"))
    {
        services.AddScoped<MockService, MockService>();
        services.AddScoped<IMyService>((global::System.IServiceProvider sp) => sp.GetRequiredService<MockService>());
    }

    return services;
}
```

</details>

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

// In consuming project, import via a class marker.
// IocImportModule is class-only and reads defaults from the module type and its assembly.
[IocImportModule(typeof(SharedMarker))]
public sealed class Module;
```

## Registration Priority

SourceGen.Ioc uses two related precedence rules:

### 1) Settings merge order

When applying settings to a registration, the merge order is:

1. Explicit settings on `[IocRegister]` or `[IocRegisterFor]`
2. Matching `[IocRegisterDefaults]`
3. MSBuild `SourceGenIocDefaultLifetime` (for lifetime fallback)
4. Built-in fallback `Transient`

### 2) Registration mechanism for discovered closed generics

When a closed generic type is discovered (for example via `[IocDiscover]` or dependency analysis), generation uses:

|Priority|Mechanism|Description|
|:-:|:-|:-|
|1|`[IocRegister]` on implementation type|Use explicit registration behavior|
|2|`ImplementationTypes` in defaults|Use direct constructor-based registration for listed implementations|
|3|`Factory` in defaults|Use factory fallback for discovered types not covered by `ImplementationTypes`|

### Example: Factory with ImplementationTypes

When both `Factory` and `ImplementationTypes` are specified:

```csharp
[assembly: IocRegisterDefaults(
    typeof(IRequestHandler<>),
    ServiceLifetime.Singleton,
    Factory = nameof(HandlerFactory.Create),
    ImplementationTypes = [typeof(EntityHandler)])]

public interface IRequestHandler<TResponse>;

public static class HandlerFactory
{
    [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
    public static IRequestHandler<Task<T>> Create<T>() => new DefaultHandler<T>();
}

// Specified in ImplementationTypes - uses constructor (new EntityHandler())
public class EntityHandler : IRequestHandler<Task<Entity>>;

// Not in ImplementationTypes - uses Factory (HandlerFactory.Create<User>())
public class UserHandler : IRequestHandler<Task<User>>;

[IocDiscover<IRequestHandler<Task<Entity>>>]  // → new EntityHandler()
[IocDiscover<IRequestHandler<Task<User>>>]    // → HandlerFactory.Create<User>()
[IocContainer]
public partial class AppContainer;
```

This allows you to:

- **Explicitly control** specific implementations via `ImplementationTypes`
- **Fallback to Factory** for types not covered by `ImplementationTypes`

## Diagnostics

|ID|Severity|Description|
|:---|:---|:---|
|SGIOC012|Warning|Duplicated `[IocRegisterDefaults]` detected for the same target type and at least one matching tag.|

---

[← Back to Overview](01_Overview.md#table-of-contents)
