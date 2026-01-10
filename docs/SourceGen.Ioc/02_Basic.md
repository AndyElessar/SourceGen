# Basic Usage

## Simple Registration

Mark a class with `[IoCRegister]` to register it for dependency injection:

```csharp
public interface IMyService;

[IoCRegister<IMyService>]
internal class MyService : IMyService;
```

## Specifying Lifetime

```csharp
// Singleton (default)
[IoCRegister<IService>]
internal class SingletonService : IService;

// Scoped
[IoCRegister<IService>(ServiceLifetime.Scoped)]
internal class ScopedService : IService;

// Transient
[IoCRegister<IService>(ServiceLifetime.Transient)]
internal class TransientService : IService;
```

## Multiple Service Types

Register a class under multiple service types:

```csharp
// Using generic attribute parameters
[IoCRegister<IService1, IService2>]
internal class MultiService : IService1, IService2;

// Or using params
[IoCRegister(typeof(IService1), typeof(IService2))]
internal class MultiService : IService1, IService2;
```

## Register All Interfaces/Base Classes

```csharp
// Register all implemented interfaces
[IoCRegister(RegisterAllInterfaces = true)]
internal class MyService : IService1, IService2, IDisposable;

// Register all base classes
[IoCRegister(RegisterAllBaseClasses = true)]
internal class DerivedService : BaseService;
```

## Registering External Types

Use `[IoCRegisterFor<T>]` to register types you don't own:

```csharp
// On assembly level
[assembly: IoCRegisterFor<ExternalService>(ServiceLifetime.Singleton)]

// Or on a marker class
[IoCRegisterFor<ExternalService>(ServiceLifetime.Singleton)]
[IoCRegisterFor<AnotherExternal>(ServiceTypes = [typeof(IExternal)])]
public class RegistrationMarker;
```

## Usage

```csharp
// In your startup/program
var services = new ServiceCollection();
services.AddMyProject(); // Generated extension method
```

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
