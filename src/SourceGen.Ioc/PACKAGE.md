# SourceGen.Ioc

A C# source generator that extends the capabilities of `Microsoft.Extensions.DependencyInjection.Abstractions` by generating registration code.

## Why Use SourceGen.Ioc?

|Feature|MS.E.DI|SourceGen.Ioc|
|:-|:-|:-|
|Open Generic|Runtime resolution only|Auto-discovers closed types from usage|
|Nested Open Generic|❌ Not supported|✅ Supported by auto-discovery and manual `[IocDiscover]` attribute|
|Decorator Pattern|❌ Not supported|✅ Fully supported with type constraint validation|
|Field/Property/Method Injection|❌ Not supported|✅ `[IocInject]` attribute|
|Lifecycle validation|Runtime errors|✅ Compile-time analyzer|
|Circular dependency|Runtime errors|✅ Compile-time analyzer|

## Installation

```bash
dotnet add package SourceGen.Ioc
```

## Quick Start

```csharp
using SourceGen.Ioc;
using Microsoft.Extensions.DependencyInjection;

// 1. Mark your class with [IocRegister]
public interface IMyService;

[IocRegister<IMyService>(ServiceLifetime.Scoped)]
internal class MyService : IMyService;

// 2. Register in DI container
var services = new ServiceCollection();
services.AddMyProject(); // Generated extension method
```

## Features

### Basic Registration

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

### Multiple Service Types

```csharp
[IocRegister(ServiceTypes = [typeof(IService1), typeof(IService2)])]
internal class MultiService : IService1, IService2;
```

### Field/Property/Method Injection

```csharp
[IocRegister<IMyService>]
internal class MyService : IMyService
{
    [IocInject]
    private ILogger _logger;

    [IocInject]
    public IConfiguration Config { get; set; }

    [IocInject]
    public void Initialize(IOptions<MyOptions> options) { }
}
```

### Decorator Pattern

```csharp
[IocRegisterDefaults<IHandler>(Decorators = [typeof(LoggingDecorator<>), typeof(CachingDecorator<>)])]
internal partial class Defaults;

[IocRegister]
internal class MyHandler : IHandler;
```

### Keyed Services

```csharp
[IocRegister<IService>(Key = "primary")]
internal class PrimaryService : IService;

[IocRegister<IService>(Key = "secondary")]
internal class SecondaryService : IService;
```

### Open Generic Support

```csharp
[IocRegister(typeof(IHandler<>))]
internal class GenericHandler<T> : IHandler<T>;

// Auto-discovers closed types from usage:
// - Constructor injection
// - Field/Property/Method injection
// - IServiceProvider.GetService<T>() calls
```

### Tag-based Registration

```csharp
[IocRegister<IService>(Tags = ["Feature1"])]
internal class Feature1Service : IService;

// Generates: services.AddMyProject_Feature1();
```

## Custom Method Name

```xml
<PropertyGroup>
    <SourceGenIocName>MyApp</SourceGenIocName>
</PropertyGroup>
```

This generates `services.AddMyApp()` instead of `services.AddMyProject()`.

## Documentation

For complete documentation, see the [GitHub repository](https://github.com/AndyElessar/SourceGen).

## Related Packages

- **SourceGen.Ioc.Cli** - CLI tool for adding attributes to existing projects

## License

MIT License
