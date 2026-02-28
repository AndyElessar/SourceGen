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

// Register selectively at runtime:
// services.AddMyProject("Feature1");
```

### External Type Registration

```csharp
[IocRegisterFor<ThirdPartyService>(ServiceTypes = [typeof(IService)])]
internal partial class Registrations;
```

### Module Import

Share `[IocRegisterDefaults]` settings across assemblies:

```csharp
[IocImportModule<SharedModule>]
internal partial class Registrations;
```

### Factory & Instance Registration

```csharp
[IocRegister<IService>(Factory = nameof(Create))]
internal class MyService : IService
{
    public static MyService Create(ILogger logger) => new(logger);
}
```

### Wrapper Types

Built-in support for `Lazy<T>`, `Func<T>`, collection types (`IEnumerable<T>`, `T[]`, etc.), and `IDictionary<TKey, TValue>` for keyed services.

### Compile-time Container

```csharp
[IocContainer]
internal partial class MyContainer;
```

Generates a high-performance container with typed resolution APIs, supporting thread-safe strategies and eager resolve options.

## Custom Method Name

```xml
<PropertyGroup>
    <SourceGenIocName>MyApp</SourceGenIocName>
</PropertyGroup>
```

This generates `services.AddMyApp()` instead of `services.AddMyProject()`.

## All Features

|Feature|Description|
|:---|:---|
|`[IocRegister]`|Basic DI registration with lifetime, keyed, tags support|
|`[IocRegisterFor]`|Register external types you don't own|
|`[IocRegisterDefaults]`|Centralized default settings for target type implementations|
|`[IocImportModule]`|Import defaults from another module/assembly|
|`[IocInject]`|Field, property, method, and parameter injection|
|`[IocDiscover]`|Manual closed generic type discovery|
|`[IocGenericFactory]`|Map discovered generics to factory method type parameters|
|`[IocContainer]`|Generate compile-time container with typed resolution|
|Decorator chains|Ordered decorator support with generic constraint validation|
|Wrapper types|`Lazy<T>`, `Func<T>`, collections, `IDictionary<TKey, TValue>`|
|Factory / Instance|Static factory methods or static instance registration|
|Tag-based groups|Runtime tag filtering for selective registration|
|Compile-time analyzers|Lifecycle, circular dependency, and usage validation|

## Documentation

For complete documentation, see the [GitHub repository](https://github.com/AndyElessar/SourceGen).

## Related Packages

- **SourceGen.Ioc.Cli** - CLI tool for adding attributes to existing projects

## License

MIT License
