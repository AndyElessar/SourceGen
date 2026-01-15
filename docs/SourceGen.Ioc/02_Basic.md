# Basic Usage

## Simple Registration

Mark a class with `[IocRegister]` to register it for dependency injection:

```csharp
public interface IMyService;

[IocRegister<IMyService>]
internal class MyService : IMyService;
// Or non-generic version
[IocRegister(typeof(IMyService))]
internal class MyService : IMyService;
```

## Specifying Lifetime

```csharp
// Singleton (default)
[IocRegister<IService>]
internal class SingletonService : IService;

// Scoped
[IocRegister<IService>(ServiceLifetime.Scoped)]
internal class ScopedService : IService;

// Transient
[IocRegister<IService>(ServiceLifetime.Transient)]
internal class TransientService : IService;
```

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

## Register All Interfaces/Base Classes

```csharp
// Register all implemented interfaces
[IocRegister(RegisterAllInterfaces = true)]
internal class MyService : IService1, IService2, IDisposable;

// Register all base classes
[IocRegister(RegisterAllBaseClasses = true)]
internal class DerivedService : BaseService;
```

## Registering External Types

Use `[IocRegisterFor<T>]` to register types you don't own:

```csharp
// On assembly level
[assembly: IocRegisterFor<ExternalService>(ServiceLifetime.Singleton)]

// Or on a marker class
[IocRegisterFor<ExternalService>(ServiceLifetime.Singleton)]
[IocRegisterFor<AnotherExternal>(ServiceTypes = [typeof(IExternal)])]
public class RegistrationMarker;
```

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

[← Back to Overview](01_Overview.md)
