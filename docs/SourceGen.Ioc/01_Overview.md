# SourceGen.Ioc Overview

SourceGen.Ioc is a C# source generator that automatically generates dependency injection registration code at compile time for `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Why Use SourceGen.Ioc?

### Performance & AOT

- **Minimal Runtime Reflection** - All registration code is generated at compile time, eliminating runtime reflection overhead
- **Open Generic Auto-Discovery** - Automatically discovers closed generic types from constructor/property/method injection and `IServiceProvider` invocations
- **Nested Open Generic Support** - Supports nested open generic service interfaces (e.g., `IHandler<Request<T>, Response<T>>`) that standard DI cannot resolve at runtime, with both auto-discovery and manual `[Discover]` attribute
- **AOT Compatible** - Works with Native AOT and trimming

### Compile-time Safety

- **Lifecycle Analysis** - Analyzer detects lifetime conflicts (e.g., Singleton depending on Scoped)
- **Circular Dependency Detection** - Analyzer detects circular dependencies at compile time
- **Decorator Type Constraint Validation** - Automatically validates decorator type constraints and skips non-matching decorators

### Flexible Configuration

- **Field, Property & Method Injection** - Supports `[Inject]` attribute on fields, properties, methods, and constructor parameters
- **Centralized Defaults** - Use `[IoCRegisterDefaults<T>]` to define default settings for all implementations, with ability to override per-registration
- **Decorator Pattern** - Built-in support for decorator chains with type constraint validation
- **Keyed Services** - Full support for keyed service registration with string, enum, or C# expression keys
- **Tags** - Organize registrations into groups with tag-based extension methods
- **Factory & Instance** - Support custom factory methods and static instance registration

### Developer Experience

- **CLI Tool** - `SourceGen.Ioc.Cli` helps add attributes to existing projects quickly

## Core Concepts

### Attributes

| Attribute | Description |
|-----------|-------------|
| `[IoCRegister]` | Mark a class for DI registration |
| `[IoCRegisterFor<T>]` | Register an external type |
| `[IoCRegisterDefaults<T>]` | Define default settings for types implementing T |
| `[Inject]` | Mark property/field/method for injection |
| `[ImportModule<T>]` | Import defaults from another module |
| `[Discover<T>]` | Discover closed generic types for open generic registration |

### Generated Code

The generator creates an extension method for `IServiceCollection`:

```csharp
// Generated
public static IServiceCollection AddMyProject(this IServiceCollection services)
{
    // Registration code here
    return services;
}
```

## Service Lifetimes

```csharp
[IoCRegister<IService>(ServiceLifetime.Singleton)]  // Default
[IoCRegister<IService>(ServiceLifetime.Scoped)]
[IoCRegister<IService>(ServiceLifetime.Transient)]
```

## Table of Contents

1. [Overview](01_Overview.md) - Introduction and core concepts
2. [Basic Usage](02_Basic.md) - Basic registration patterns
3. [Default Settings](03_Defaults.md) - Configure default registration settings
4. [Field, Property & Method Injection](04_Field_Property_Method_Injection.md) - Injection patterns
5. [Keyed Services](05_Keyed.md) - Keyed service registration
6. [Decorators](06_Decorator.md) - Decorator pattern support
7. [Tags](07_Tags.md) - Tag-based registration methods
8. [Factory & Instance](08_Factory_Instance.md) - Factory and instance registration
9. [Open Generics](09_OpenGeneric.md) - Open generic type support
10. [CLI Tool](10_CliTool.md) - Command-line tool for adding attributes