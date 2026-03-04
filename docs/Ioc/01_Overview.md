# SourceGen.Ioc Overview

`SourceGen.Ioc` is a C# source generator that extends `Microsoft.Extensions.DependencyInjection.Abstractions` (`MS.E.DI.A`) — it is **not** a replacement for `Microsoft.Extensions.DependencyInjection` (`MS.E.DI`) .

Its primary goal is to **generate `IServiceCollection` registration code at compile time**, eliminating repetitive manual registration while keeping your business code free of DI framework concerns. An optional compile-time container (`[IocContainer]`) is available for high-performance or specialized scenarios (such as building a tag-filtered container for a specific subsystem).

## Quick Start

The simplest way to get started is to **register services without modifying their source code**:

```csharp
// In a dedicated registration file — your service types stay attribute-free
[assembly: IocRegisterFor<UserService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IUserService)])]
[assembly: IocRegisterFor<OrderService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IOrderService)])]
```

```csharp
// In Program.cs
var services = new ServiceCollection();
services.AddMyProject(); // Generated extension method
```

> [!TIP]
> Use `[IocRegisterFor]` and `[IocRegisterDefaults]` in dedicated registration files to keep your domain types completely free of `SourceGen.Ioc` attributes.
> Use `[IocRegister]` directly on infrastructure types when local annotation improves readability.

## Why Use SourceGen.Ioc?

### Reduce Boilerplate

- **Centralized Defaults** — `[IocRegisterDefaults]` lets you define lifetime, decorators, and tags once for all implementations of a service type - no repetitive per-class configuration.
- **Batch Registration** — `ImplementationTypes` on defaults registers multiple types without individual attributes.
- **CLI Tool** — `SourceGen.Ioc.Cli` can add attributes or generate registration files for existing codebases in bulk.

### Native AOT Compatible

- All generated code compiles to Native AOT without trimming warnings or runtime reflection.
- The optional `[IocContainer]` generates fully typed resolution code — no `Activator.CreateInstance` or expression tree compilation at runtime.

### Compile-Time Safety

- **Lifecycle Analysis** — Detects risky lifetime chains (for example, singleton → scoped/transient).
- **Circular Dependency Detection** — Reports circular constructor dependencies at compile time.
- **Decorator Constraint Validation** — Skips invalid decorators when generic constraints are not satisfied.

|ID|Severity|Description|
|:---|:---|:---|
|SGIOC002|Error|Circular dependency detected among registered services.|
|SGIOC003|Error|Singleton service depends on a scoped service.|
|SGIOC004|Error|Singleton service depends on a transient service.|
|SGIOC005|Error|Scoped service depends on a transient service.|

### Better Generic Support

- **Open Generic Auto-Discovery** — Automatically discovers closed generic services from constructor/property/field/method injection and `IServiceProvider` invocations.
- **Nested Generic Support** — Supports nested open generic patterns (for example, `IHandler<Request<T>, List<T>>`) that `MS.E.DI` cannot resolve at runtime.

### Flexible Configuration

- **Keys** — Register and resolve keyed services with value keys or C# expression keys.
- **Tags** — Group registrations behind runtime tag filters for startup-time profile selection.
- **Decorators** — Build ordered decorator chains with generic constraint checking.
- **Property/Field/Method Injection** — Supports `[IocInject]` (and `[Inject]`-named attributes) for member and parameter injection.
- **Factory & Instance** — Use static factory methods or static instances for custom creation logic.
- **Generated Container** — Use `[IocContainer]` to generate a high-performance compile-time container for specific scenarios.

## Core Concepts

### Attributes Reference

|Attribute|Purpose|
|:---|:---|
|`[IocRegisterFor]` / `[IocRegisterFor<T>]`|Register types you do not own, or any type — without modifying its source code.|
|`[IocRegisterDefaults]` / `[IocRegisterDefaults<T>]`|Define shared policy (lifetime, decorators, tags) for all implementations of a service type.|
|`[IocImportModule]` / `[IocImportModule<T>]`|Import defaults from another module type or assembly.|
|`[IocRegister]` / `[IocRegister<T>]`|Register a class directly on its declaration (best for infrastructure code you own).|
|`[IocInject]`|Mark constructor/property/field/method/parameter for generator-aware injection behavior.|
|`[IocDiscover]` / `[IocDiscover<T>]`|Manually discover closed generic types for open-generic registration.|
|`[IocGenericFactory]`|Map discovered closed generic types to generic factory method type parameters.|
|`[IocContainer]`|Generate a compile-time container on a partial class (advanced).|

> [!NOTE]
> Generic attributes (`[Attribute<T>]`) require C# 11 or later.

### Generated Outputs

`SourceGen.Ioc` generates two kinds of output:

1. **Registration extension method** (`Add{ProjectName}`) — the primary output, always generated.
2. **Container implementation** (`[IocContainer]`) — optional, for high-performance or tag-filtered container scenarios.

```csharp
// Generated registration extension — use with any MS.E.DI-compatible container
public static IServiceCollection AddMyProject(this IServiceCollection services, params IEnumerable<string> tags)
{
    // registration code...
    return services;
}
```

> [!IMPORTANT]
> The generated container does **not** replace `MS.E.DI`. It does not parse `IServiceCollection` registrations — so extension methods such as `services.AddLogging()` or `services.AddOptions()` are not available in container-only mode.
> For general-purpose applications, use `MS.E.DI` as the primary container and `SourceGen.Ioc`'s generated registration methods as an extension layer.
> Use `[IocContainer]` when you need a typed, high-performance container for specific subsystems (for example, a `MediatorContainer` with `IncludeTags = ["Mediator"]`).

## Diagnostics

`SourceGen.Ioc` analyzers currently define `SGIOC001` through `SGIOC024`.

- **Usage validation** — attribute misuse, incompatible settings, invalid injection members.
- **Design validation** — cycles, lifetime issues, duplicate registrations/defaults.
- **Keyed/factory validation** — key type mismatches, generic factory mapping issues.
- **Container validation** — unresolved dependencies and container option conflicts.

## Table of Contents

|Document|Description|
|:---|:---|
|[Overview](01_Overview.md)|Entry point, quick start, and feature map.|
|[Basic Usage](02_Basic.md)|Registration patterns, external types, and method naming.|
|[Default Settings](03_Defaults.md)|Shared defaults, implementation lists, module import, and precedence.|
|[Injection](04_Field_Property_Method_Injection.md)|Property, field, method, and constructor injection behavior.|
|[Keyed Services](05_Keyed.md)|Keyed registration and keyed injection patterns.|
|[Decorators](06_Decorator.md)|Decorator registration and type-constraint behavior.|
|[Tags](07_Tags.md)|Tag-based registration and mutually exclusive tag model.|
|[Factory & Instance](08_Factory_Instance.md)|Static factory methods, static instances, and generic factory mapping.|
|[Open Generics](09_OpenGeneric.md)|Open generic registration, auto-discovery, and manual discover.|
|[Wrapper Types](10_Wrapper.md)|Lazy, Func, collections, and dictionary resolution.|
|[CLI Tool](11_CliTool.md)|CLI workflows for attribute and registration file generation.|
|[Container](12_Container.md)|Compile-time container generation and options (advanced).|
|[MSBuild Configuration](13_MSBuild_Configuration.md)|MSBuild properties controlling generation behavior.|
|[Best Practices](14_Best_Practices.md)|Production-safe patterns, decision matrix, and diagnostics quick-fix guide.|
