# SourceGen.Ioc Overview

SourceGen.Ioc is a C# source generator that extends the capabilities of `Microsoft.Extensions.DependencyInjection.Abstractions` by generating registration code.

## Why Use SourceGen.Ioc?

### Better Generic support

- **Open Generic Auto-Discovery** - Automatically discovers closed generic types from constructor/property/method injection and `IServiceProvider` invocations
- **Nested Open Generic Support** - Supports nested open generic service interfaces (e.g., `IHandler<Request<T>, Response<T>>`) that `MS.DI` cannot resolve at runtime, with both auto-discovery and manual `[Discover]` attribute

### Compile-time Safety

- **Lifecycle Analysis** - Analyzer detects lifetime conflicts (e.g., Singleton depending on Scoped)
- **Circular Dependency Detection** - Analyzer detects circular dependencies at compile time
- **Decorator Type Constraint Validation** - Automatically validates decorator type constraints and skips non-matching decorators

|ID|Severity|Description|
|:---|:---|:---|
|SGIOC002|Error|Circular dependency detected among registered services.|
|SGIOC003|Error|Singleton service cannot depend on Scoped service.|
|SGIOC004|Error|Singleton service cannot depend on Transient service.|
|SGIOC005|Error|Scoped service cannot depend on Transient service.|

### Flexible Configuration

- **Field, Property & Method Injection** - Supports `[IocInject]` attribute on fields, properties, methods and constructors
- **Centralized Defaults** - Use `[IocRegisterDefaults<T>]` to define default settings for all implementations, with ability to override per-registration
- **Decorator Pattern** - Built-in support for decorator chains with type constraint validation
- **Keyed Services** - Full support for keyed service registration with string, enum, or C# expression keys
- **Tags** - Organize registrations into groups with tag-based extension methods
- **Factory & Instance** - Support custom factory methods and static instance registration

### Developer Experience

- **CLI Tool** - `SourceGen.Ioc.Cli` helps add attributes to existing projects quickly

## Core Concepts

### Attributes Reference

|Attribute|Description|
|:---|:---|
|`[IocRegister]`<br/>`[IocRegister<T>]`<br/>`[IocRegister<T,T>]`<br/>`[IocRegister<T,T,T>]`<br/>`[IocRegister<T,T,T,T>]`|Mark a class for DI registration|
|`[IocRegisterFor]`<br/>`[IocRegisterFor<T>]`|Register an external type|
|`[IocRegisterDefaults]`<br/>`[IocRegisterDefaults<T>]`|Define default settings for types implementing T|
|`[IocInject]`|Mark property/field/method/constructor for injection, or parameter for keyed services|
|`[IocImportModule]`<br/>`[IocImportModule<T>]`|Import defaults from another module|
|`[IocDiscover]`<br/>`[IocDiscover<T>]`|Discover closed generic types for open generic registration|

> [!NOTE]  
> Generic attribute only supported in C# 11 and later.

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
[IocRegister<IService>(ServiceLifetime.Singleton)]  // Default
[IocRegister<IService>(ServiceLifetime.Scoped)]
[IocRegister<IService>(ServiceLifetime.Transient)]
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
