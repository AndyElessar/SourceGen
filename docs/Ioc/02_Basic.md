# Basic Usage

`SourceGen.Ioc` provides two main approaches for service registration:

1. **Non-intrusive** — `[IocRegisterFor]` registers types externally, keeping your service code attribute-free.
2. **Direct annotation** — `[IocRegister]` marks the class at its declaration (best for infrastructure types you own).

> [!TIP]
> Prefer `[IocRegisterFor]` for domain/business types and `[IocRegister]` for infrastructure types. This keeps your domain layer free of DI framework concerns.

## Registering External Types (Non-Intrusive)

Use `[IocRegisterFor<T>]` (generic) or `[IocRegisterFor]` (non-generic) to register types **without modifying their source code**:

```csharp
// In a dedicated registration file — service types stay attribute-free
[assembly: IocRegisterFor<UserService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IUserService)])]
[assembly: IocRegisterFor<OrderService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IOrderService)])]

// Or on a marker class
[IocRegisterFor<UserService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IUserService)])]
[IocRegisterFor<OrderService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IOrderService)])]
public class RegistrationMarker;
```

> [!NOTE]
> Generic attributes require C# 11+. If your project targets an older language version, use the non-generic form: `[IocRegisterFor(typeof(UserService))]`.

`IocRegisterFor` supports the same options as `IocRegister` (`Key`, `KeyType`, `Tags`, `Decorators`, `Factory`, `Instance`, and lifetime settings).

<details>
<summary>Generated Code</summary>

```csharp
services.AddScoped<UserService, UserService>();
services.AddScoped<IUserService>((global::System.IServiceProvider sp) => sp.GetRequiredService<UserService>());
services.AddScoped<OrderService, OrderService>();
services.AddScoped<IOrderService>((global::System.IServiceProvider sp) => sp.GetRequiredService<OrderService>());
```

</details>

## Simple Registration (Direct Annotation)

Mark a class with `[IocRegister]` to register it directly:

```csharp
public interface IMyService;

[IocRegister<IMyService>]
internal class MyService : IMyService;
// Or non-generic version
[IocRegister(typeof(IMyService))]
internal class MyService : IMyService;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddTransient<MyService, MyService>();
services.AddTransient<IMyService>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
```

</details>

## Specifying Lifetime

```csharp
// Transient (default)
[IocRegister<IService>]
internal class TransientService : IService;

// Singleton
[IocRegister<IService>(ServiceLifetime.Singleton)]
internal class SingletonService : IService;

// Scoped
[IocRegister<IService>(ServiceLifetime.Scoped)]
internal class ScopedService : IService;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddTransient<TransientService, TransientService>();
services.AddTransient<IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<TransientService>());

services.AddSingleton<SingletonService, SingletonService>();
services.AddSingleton<IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<SingletonService>());

services.AddScoped<ScopedService, ScopedService>();
services.AddScoped<IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<ScopedService>());
```

</details>

## Multiple Service Types

Register a class under multiple service types:

```csharp
// Using generic attribute parameters
[IocRegister<IService1, IService2>]
internal class MultiService : IService1, IService2;

// Or using params
[IocRegister(typeof(IService1), typeof(IService2))]
internal class MultiService : IService1, IService2;
```

<details>
<summary>Generated Code</summary>

```csharp
services.AddTransient<MultiService, MultiService>();
services.AddTransient<IService1>((global::System.IServiceProvider sp) => sp.GetRequiredService<MultiService>());
services.AddTransient<IService2>((global::System.IServiceProvider sp) => sp.GetRequiredService<MultiService>());
```

</details>

## Register All Interfaces/Base Classes

```csharp
// Register all implemented interfaces
[IocRegister(RegisterAllInterfaces = true)]
internal class MyService : IService1, IService2, IDisposable;

// Register all base classes
[IocRegister(RegisterAllBaseClasses = true)]
internal class DerivedService : BaseService;
```

<details>
<summary>Generated Code</summary>

```csharp
// RegisterAllInterfaces
services.AddSingleton<MyService, MyService>();
services.AddSingleton<IService1>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
services.AddSingleton<IService2>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());
services.AddSingleton<IDisposable>((global::System.IServiceProvider sp) => sp.GetRequiredService<MyService>());

// RegisterAllBaseClasses
services.AddSingleton<DerivedService, DerivedService>();
services.AddSingleton<BaseService>((global::System.IServiceProvider sp) => sp.GetRequiredService<DerivedService>());
```

</details>

## Usage

```csharp
// In your startup/program
var services = new ServiceCollection();
services.AddMyProject(); // Generated extension method
```

## Diagnostics

|ID|Severity|Description|
|:---|:---|:---|
|SGIOC001|Error|`[IocRegister]` or `[IocRegisterFor]` cannot be applied to `private` or `abstract` classes.|
|SGIOC011|Warning|Duplicated registration detected for the same implementation type, same key, and at least one matching tag.|

## Custom Method Name

Use the `SourceGenIocName` MSBuild property to customize the generated method name:

```xml
<PropertyGroup>
    <SourceGenIocName>MyApp</SourceGenIocName>
</PropertyGroup>
```

This generates:

```csharp
// Generated extension method with custom name
services.AddMyApp();

// Tag methods also use the custom name
services.AddMyApp_Tag1();
```

If not specified, the assembly name is used as the default method name.

---

[← Back to Overview](01_Overview.md#table-of-contents)
