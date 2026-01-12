# SourceGen.Ioc

A C# source generator that automatically generates dependency injection registration code at compile time for `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Why Use SourceGen.Ioc?

|Feature|MS.DI|SourceGen.Ioc|
|:---|:---|:---|
|Open Generic|Runtime resolution only|Auto-discovers closed types|
|Nested Open Generic|❌ Not supported|✅ Fully supported|
|Decorator Pattern|❌ Not supported|✅ Built-in support|
|Lifecycle validation|Runtime errors|✅ Compile-time analyzer|
|Circular dependency|Runtime errors|✅ Compile-time analyzer|
|Field/Property/Method Injection|❌ Not supported|✅ `[Inject]` attribute|

## Installation

```bash
dotnet add package SourceGen.Ioc
```

## Quick Start

```csharp
using SourceGen.Ioc;
using Microsoft.Extensions.DependencyInjection;

// 1. Mark your class with [IoCRegister]
public interface IMyService;

[IoCRegister<IMyService>(ServiceLifetime.Scoped)]
internal class MyService : IMyService;

// 2. Register in DI container
var services = new ServiceCollection();
services.AddMyProject(); // Generated extension method
```

## Features

### Basic Registration

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

### Multiple Service Types

```csharp
[IoCRegister<IService1, IService2>]
internal class MultiService : IService1, IService2;
```

### Field/Property/Method Injection

```csharp
[IoCRegister<IMyService>]
internal class MyService : IMyService
{
    [Inject]
    private ILogger _logger;

    [Inject]
    public IConfiguration Config { get; set; }

    [Inject]
    public void Initialize(IOptions<MyOptions> options) { }
}
```

### Decorator Pattern

```csharp
[IoCRegisterDefaults<IHandler>(Decorators = [typeof(LoggingDecorator<>), typeof(CachingDecorator<>)])]
internal partial class Defaults;

[IoCRegister]
internal class MyHandler : IHandler;
```

### Keyed Services

```csharp
[IoCRegister<IService>(Key = "primary")]
internal class PrimaryService : IService;

[IoCRegister<IService>(Key = "secondary")]
internal class SecondaryService : IService;
```

### Open Generic Support

```csharp
[IoCRegister(typeof(IHandler<>))]
internal class GenericHandler<T> : IHandler<T>;

// Auto-discovers closed types from usage:
// - Constructor injection
// - Field/Property/Method injection
// - IServiceProvider.GetService<T>() calls
```

### Tag-based Registration

```csharp
[IoCRegister<IService>(Tags = ["Feature1"])]
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
