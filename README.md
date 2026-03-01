# SourceGen

A collection of C# Source Generators for compile-time code generation.

## Packages

|Package|Description|
|:---|:---|
|[![NuGet](https://img.shields.io/nuget/v/SourceGen.Ioc.svg)](https://www.nuget.org/packages/SourceGen.Ioc)|IoC/DI registration source generator for `Microsoft.Extensions.DependencyInjection.Abstractions`|
|[![NuGet](https://img.shields.io/nuget/v/SourceGen.Ioc.Cli.svg)](https://www.nuget.org/packages/SourceGen.Ioc.Cli)|CLI tool for adding IoC attributes to existing projects|

## SourceGen.Ioc

A C# source generator that extends `Microsoft.Extensions.DependencyInjection.Abstractions` by generating registration code at compile time.

### Compared to `MS.E.DI`

|Feature|MS.E.DI|SourceGen.Ioc|
|:-|:-|:-|
|Open Generic|Runtime resolution only|✅ Auto-discovers closed types from usage|
|Nested Open Generic|❌|✅ Auto-discovery + `[IocDiscover]`|
|Decorator Pattern|❌|✅ With type constraint validation|
|Field/Property/Method Injection|❌|✅ `[IocInject]`|
|Lifecycle validation|Runtime errors|✅ Compile-time|
|Circular dependency|Runtime errors|✅ Compile-time|

### Quick Start

```bash
dotnet add package SourceGen.Ioc
```

```csharp
using SourceGen.Ioc;
using Microsoft.Extensions.DependencyInjection;

public interface IMyService;

[IocRegister<IMyService>(ServiceLifetime.Scoped)]
internal class MyService : IMyService;

// Register in DI container
services.AddMyProject(); // Generated extension method
```

### Documentation

See [full documentation](docs/Ioc/01_Overview.md) for all features including defaults, keyed services, decorators, open generics, tags, modules, wrappers, factory/instance registration, and compile-time containers.

## License

MIT
