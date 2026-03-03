# SourceGen.Ioc Overview

SourceGen.Ioc is a C# source-generator-based dependency injection library built on top of `Microsoft.Extensions.DependencyInjection.Abstractions`.

It generates registration code and (optionally) a compile-time container, so your app avoids runtime reflection-heavy registration logic.

## Why Use SourceGen.Ioc?

### Better Generic Support

- **Open Generic Auto-Discovery** - Automatically discovers closed generic services from constructor/property/field/method injection and `IServiceProvider` invocations.
- **Nested Generic Support** - Supports nested open generic patterns (for example, `IHandler<Request<T>, List<T>>`) that are difficult for runtime-only registration.

### Compile-time Safety

- **Lifecycle Analysis** - Detects risky lifetime chains (for example, singleton -> scoped/transient).
- **Circular Dependency Detection** - Detects circular constructor dependencies at compile time.
- **Decorator Constraint Validation** - Skips invalid decorators when generic constraints are not satisfied.

|ID|Severity|Description|
|:---|:---|:---|
|SGIOC002|Error|Circular dependency detected among registered services.|
|SGIOC003|Error|Singleton service depends on a scoped service.|
|SGIOC004|Error|Singleton service depends on a transient service.|
|SGIOC005|Error|Scoped service depends on a transient service.|

### Flexible Configuration

- **Property/Field/Method Injection** - Supports `[IocInject]` (and `[Inject]`-named attributes) for member and parameter injection.
- **Defaults and Modules** - Use `[IocRegisterDefaults]` and `[IocImportModule]` for centralized registration policy.
- **Decorators** - Build ordered decorator chains with generic constraint checking.
- **Keyed Services** - Register and resolve keyed services with value keys or C# expression keys.
- **Tags** - Group registrations behind runtime tag filters.
- **Factory and Instance Registration** - Use static factory methods or static instances.
- **Generated Container** - Use `[IocContainer]` to generate a high-performance compile-time container.

### Developer Experience

- **CLI Tool** - `SourceGen.Ioc.Cli` can add attributes and generate registration attribute files for existing projects.

## Core Concepts

### Attributes Reference

|Attribute|Description|
|:---|:---|
|`[IocRegister]`<br/>`[IocRegister<T>]`|Mark a class for DI registration.|
|`[IocRegisterFor]`<br/>`[IocRegisterFor<T>]`|Register external types you do not own.|
|`[IocRegisterDefaults]`<br/>`[IocRegisterDefaults<T>]`|Define default settings for implementations of a target type.|
|`[IocImportModule]`<br/>`[IocImportModule<T>]`|Import default settings from another module type/assembly.|
|`[IocDiscover]`<br/>`[IocDiscover<T>]`|Manually discover closed generic types for open-generic registration.|
|`[IocInject]`|Mark constructor/property/field/method/parameter for generator-aware injection behavior.|
|`[IocGenericFactory]`|Map discovered closed generic types to generic factory method type parameters.|
|`[IocContainer]`|Generate a compile-time container on a partial class.|

> [!NOTE]
> Generic attributes (`[Attribute<T>]`) require C# 11 or later.

### Generated Outputs

SourceGen.Ioc can generate:

1. `IServiceCollection` extension method (`Add{ProjectName}`)
2. Optional container implementation (`[IocContainer]`) with typed resolution APIs

```csharp
// Generated registration extension
public static IServiceCollection AddMyProject(this IServiceCollection services, params IEnumerable<string> tags)
{
    // registration code...
    return services;
}
```

## Diagnostics

SourceGen.Ioc analyzers currently define `SGIOC001` to `SGIOC022`.

- Usage validation: attribute misuse, incompatible settings, invalid injection members.
- Design validation: cycles, lifetime issues, duplicate registrations/defaults.
- Keyed/factory validation: key type mismatches, generic factory mapping issues.
- Container validation: unresolved dependencies and container option conflicts.

## Table of Contents

|#|Document|Description|
|:---|:---|:---|
|1|[01_Overview.md](01_Overview.md)|Entry point and feature map.|
|2|[02_Basic.md](02_Basic.md)|Basic registration patterns and method naming.|
|3|[03_Defaults.md](03_Defaults.md)|Defaults, implementation lists, module import, and precedence.|
|4|[04_Field_Property_Method_Injection.md](04_Field_Property_Method_Injection.md)|Property/field/method/constructor injection behavior.|
|5|[05_Keyed.md](05_Keyed.md)|Keyed registration and keyed injection patterns.|
|6|[06_Decorator.md](06_Decorator.md)|Decorator registration and type-constraint behavior.|
|7|[07_Tags.md](07_Tags.md)|Tag-based registration and mutually exclusive tag model.|
|8|[08_Factory_Instance.md](08_Factory_Instance.md)|Factory/instance registration and generic factory mapping.|
|9|[09_OpenGeneric.md](09_OpenGeneric.md)|Open generic registration, auto discovery, and manual discover.|
|10|[10_Wrapper.md](10_Wrapper.md)|Wrapper types: Lazy, Func, collections, and dictionary resolution.|
|11|[11_CliTool.md](11_CliTool.md)|CLI workflows for attribute and registration file generation.|
|12|[12_Container.md](12_Container.md)|Compile-time container generation and options.|
|13|[13_MSBuild_Configuration.md](13_MSBuild_Configuration.md)|MSBuild properties controlling generation behavior.|
|14|[14_Best_Practices.md](14_Best_Practices.md)|Production-safe patterns, decision matrix, and diagnostics quick-fix guide.|
