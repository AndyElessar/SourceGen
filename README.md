# SourceGen

A collection of C# Source Generators for compile-time code generation.

## Packages

|Package|Description|
|:---|:---|
|[![NuGet](https://img.shields.io/nuget/v/SourceGen.Ioc.svg)](https://www.nuget.org/packages/SourceGen.Ioc)|IoC/DI registration source generator for `Microsoft.Extensions.DependencyInjection.Abstractions`|
|[![NuGet](https://img.shields.io/nuget/v/SourceGen.Ioc.Cli.svg)](https://www.nuget.org/packages/SourceGen.Ioc.Cli)|CLI tool for adding IoC attributes to existing projects|

## SourceGen.Ioc

A C# source generator that extends the capabilities of `Microsoft.Extensions.DependencyInjection.Abstractions` by generating registration code.

### Why Use SourceGen.Ioc?

Compared to `Microsoft.Extensions.DependencyInjection` aka. `MS.E.DI`:

|Feature|MS.E.DI|SourceGen.Ioc|
|:-|:-|:-|
|Open Generic|Runtime resolution only|Auto-discovers closed types from usage|
|Nested Open Generic|❌ Not supported|✅ Supported by auto-discovery and manual `[IocDiscover]` attribute|
|Decorator Pattern|❌ Not supported|✅ Fully supported with type constraint validation|
|Field/Property/Method Injection|❌ Not supported|✅ `[IocInject]` attribute|
|Lifecycle validation|Runtime errors|✅ Compile-time analyzer|
|Circular dependency|Runtime errors|✅ Compile-time analyzer|

1. **Open Generic Auto-Discovery** - Automatically discovers closed generic types from constructor/property/method injection and `IServiceProvider.GetService<T>()` invocations

2. **Nested Open Generic Support** - Supports nested open generic service interfaces (e.g., `IHandler<Request<T>, List<T>>`) that `MS.E.DI` cannot resolve at runtime

3. **Compile-time Analyzers** - Detects lifetime conflicts (e.g., Singleton depending on Scoped) and circular dependencies at compile time, not runtime

4. **Field/Property/Method Injection** - Use `[IocInject]` attribute for injection beyond constructors

5. **Decorator Pattern with Type Constraint Validation** - Built-in decorator chain support with automatic type constraint checking, skips non-matching decorators

6. **Centralized Default Settings** - Use `[IocRegisterDefaults<T>]` to define default lifetime, decorators, and tags for all implementations of interface/base class

7. **Tag-based Registration** - Use `Tags` parameter to group services, then selectively register them at runtime via `services.AddMyProject("Feature1")`

8. **External Type Registration** - Use `[IocRegisterFor<T>]` to register types you don't own (e.g., third-party library classes)

9. **Module Import** - Use `[IocImportModule<T>]` to share `[IocRegisterDefaults]` settings across projects/assemblies

10. **Wrapper Types** - Built-in support for `Lazy<T>`, `Func<T>`, collection types, and `IDictionary<TKey, TValue>` for keyed services

11. **Factory & Instance Registration** - Use `Factory` or `Instance` properties on `[IocRegister]` for custom creation logic

12. **Compile-time Container** - Use `[IocContainer]` to generate a high-performance container with typed resolution APIs

### Installation

```bash
dotnet add package SourceGen.Ioc
```

### Quick Start

```csharp
using SourceGen.Ioc;
using Microsoft.Extensions.DependencyInjection;

// Basic registration
public interface IMyService;

[IocRegister<IMyService>(ServiceLifetime.Scoped)]
internal class MyService : IMyService;

// Register in DI container
services.AddMyProject(); // Generated extension method
```

### Documentation

See [SourceGen.Ioc Documentation](docs/SourceGen.Ioc/01_Overview.md) for complete usage guide.

## License

This project is licensed under the MIT License.
