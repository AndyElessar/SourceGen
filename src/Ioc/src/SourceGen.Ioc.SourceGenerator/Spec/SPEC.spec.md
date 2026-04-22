# IocSourceGenerator Specification

`IocSourceGenerator` is an `IIncrementalGenerator` that emits two kinds of output from `Ioc*` attributes (and `IServiceProvider.GetService<T>` invocations) in user code:

- **Register output** (`{assemblyName}.ServiceRegistration.g.cs`) — extension methods that register services into `IServiceCollection`.
- **Container output** (`{containerClassName}.Container.g.cs`) — standalone, `IServiceCollection`-free DI container partial classes.

Per-feature documentation lives under `Spec/Register.*.spec.md` and `Spec/Container.*.spec.md` (see [Spec Index](#spec-index)). This document is the navigation map and architecture overview.

---

## Pipeline at a glance

```text
Stage 1  ForAttributeWithMetadataName + CreateSyntaxProvider
         ├── [IocRegister] / [IocRegister<T>]                ─ Transforms/TransformRegister.cs
         ├── [IocRegisterFor] / [IocRegisterFor<T>]          ─ Transforms/TransformRegister.cs
         ├── [IocRegisterDefaults] / [IocRegisterDefaults<T>]─ Transforms/TransformDefaultSettings.cs
         ├── [IocImportModule] / [IocImportModule<T>]        ─ Transforms/TransformImportModule.cs
         ├── [IocDiscover] / [IocDiscover<T>]                ─ Transforms/TransformDiscover.cs
         ├── [IocContainer]                                  ─ Transforms/TransformContainer.cs
         └── IServiceProvider.GetService<T> invocations      ─ Transforms/IServiceProviderInvocations.cs
              ↓
Stage 2  Compilation / MSBuild / default-settings inputs
         (BuildCompilationInfoProvider, BuildMsBuildPropertiesProvider, BuildDefaultLifetimeProvider)
              ↓
Stage 3  Per-registration processing (cacheable per registration)
         ProcessSingleRegistration(RegistrationData, DefaultSettingsMap, ct) → BasicRegistrationResult
              ↓
Stage 4  Closed-generic resolution + grouping
         CombineAndResolveClosedGenerics → ImmutableEquatableArray<ServiceRegistrationWithTags>
              ├─→ GroupRegistrationsForRegister  → RegisterOutputModel
              └─→ FilterRegistrationsForContainer → GroupRegistrationsForContainer → ContainerWithGroups
              ↓
Stage 5  Emit
         GenerateRegisterOutput  → {assemblyName}.ServiceRegistration.g.cs
         GenerateContainerOutput → {containerClassName}.Container.g.cs
```

The container branch splits on `ContainerModel.ExplicitOnly`:

- **ExplicitOnly** uses `GroupExplicitOnlyRegistrations` and is independent of `serviceRegistrations` (its own caching branch).
- **Normal** combines with `serviceRegistrations`, then `Select(FilterRegistrationsForContainer)` acts as a caching barrier so unrelated `serviceRegistrations` changes do not invalidate downstream nodes.

---

## Source layout

```text
src/SourceGen.Ioc.SourceGenerator/
├── IocSourceGenerator.cs                     # Initialize() — pipeline wiring
├── IocSourceGenerator.ConfigProviders.cs     # MSBuild / Compilation / default-lifetime helpers
├── Constants.cs (in Models/)
├── RoslynExtensions.cs · TypeArgMap.cs · GlobalUsings.cs
├── Transforms/   — Stage 1: attribute symbol → data model (+ TransformExtensions.cs helpers)
├── Processing/   — Stage 3 + part of Stage 4 (ProcessSingleRegistration, CombineAndResolveClosedGenerics)
├── Grouping/     — Stage 4 grouping (GroupRegistrationsForRegister, GroupRegistrationsForContainer)
├── Emit/
│   ├── Shared/    (CodeGenHelpers, SourceWriterExtensions, FeatureFilterHelper)
│   ├── Register/  (GenerateRegisterOutput + RegisterOutputModel + RegisterEntry hierarchy + wrapper helpers)
│   └── Container/ (GenerateContainerOutput + ContainerEntry hierarchy + ResolvedDependency + injection/interface/async helpers)
├── Models/       — pipeline-shared immutable data models
├── Analyzer/     — diagnostic analyzers (separate concern; see Analyzer/Spec/SPEC.spec.md)
└── Spec/         — this file + per-feature specs
```

> **Framework requirements** — The generator assembly targets `netstandard2.0`. Generated code and consumer projects require `.NET 10` (the `SourceGen.Ioc` runtime library targets `net10.0`).

All files outside `Analyzer/` are `partial class IocSourceGenerator` in namespace `SourceGen.Ioc`. Models use namespace `SourceGen.Ioc.SourceGenerator.Models`.

---

## Architecture pattern: discriminated unions for emission

Both Register and Container pipelines pre-compute a discriminated union of "entry" instances during Stage 4. Stage 5 then walks them with polymorphic `Write*` methods — emission is pure string formatting, no shared mutable context.

| Pipeline | Entry base | Subtypes | Write methods |
|---|---|---|---|
| Register | `RegisterEntry` | 7 (`Simple`, `Instance`, `Forwarding`, `Factory`, `Injection`, `AsyncInjection`, `Decorator`) + 3 wrapper structs (`Lazy`, `Func`, `Kvp`RegistrationEntry) | `WriteRegistration(SourceWriter, RegisterWriteContext)` |
| Container | `ContainerEntry` | 10 (6 service via `ServiceContainerEntry`, 3 wrapper, 1 collection) | `WriteField`, `WriteResolver`, `WriteEagerInit`, `WriteDisposal`, `WriteInit`, `WriteCollectionResolver`, `WriteLocalResolverEntries` |

Container dependency lookups are pre-resolved into the **`ResolvedDependency`** hierarchy (18 subtypes such as `DirectServiceDependency`, `LazyInlineDependency`, `MultiParamFuncDependency`, `KvpInlineDependency`, `FallbackProviderDependency`, …) each implementing `FormatExpression(bool isOptional): string`. See `Emit/Container/ContainerEntry.cs` and `Emit/Container/ResolvedDependency.cs` for the full tables.

---

## Key data models

All under `Models/` unless noted; all immutable with value equality.

| Model | Purpose |
|---|---|
| `RegistrationData` | Raw per-attribute payload (Stage 1 output) |
| `DefaultSettingsModel` / `DefaultSettingsMap` / `DefaultSettingsResult` | Defaults matching + multi-payload defaults result |
| `ImportModuleResult` | Per-module imported defaults + open generics |
| `BasicRegistrationResult` | Cacheable Stage 3 intermediate |
| `ServiceRegistrationModel` / `ServiceRegistrationWithTags` | Stage 4 registration record (+ tags) |
| `ContainerModel` | `[IocContainer]` input |
| `ContainerWithGroups` | Container + pre-grouped `ContainerRegistrationGroups` |
| `ContainerRegistrationGroups` | Grouped `ContainerEntry` arrays + `LastWinsByServiceType` lookup |
| `RegisterOutputModel` (`Emit/Register/`) | Top-level Register output model with per-tag `RegisterTagGroup` |
| `MsBuildProperties` | `RootNamespace`, `CustomIocName`, `Features` |
| `IocFeatures` | Feature-flag enum (`Register`, `Container`, `PropertyInject`, `FieldInject`, `MethodInject`, `AsyncMethodInject`) |
| `TypeData` and subtypes (`GenericTypeData`, `WrapperTypeData` family) | Type representation including wrapper-kind hierarchy (see [Wrapper kinds](#wrapper-kinds)) |

---

## Inputs and configuration

### Attributes

| Attribute | Purpose |
|---|---|
| `IocRegisterAttribute(<T>)` | Mark a class for registration |
| `IocRegisterForAttribute(<T>)` | Register an external type |
| `IocRegisterDefaultsAttribute(<T>)` | Default settings + bulk `ImplementationTypes` |
| `IocImportModuleAttribute(<T>)` | Import another assembly's defaults / open generics |
| `IocDiscoverAttribute(<T>)` | Explicit closed-generic discovery |
| `IocContainerAttribute` | Mark a `partial class` as a generated container |
| `IocGenericFactoryAttribute` | Map generic factory type parameters |

### MSBuild properties

| Property | Default | Effect |
|---|---|---|
| `RootNamespace` | _assembly name_ | Namespace for generated output |
| `SourceGenIocName` | _assembly name_ | Override the generated method/class base name; if unset, falls back to assembly name (`"Generated"` when assembly name is absent) |
| `SourceGenIocDefaultLifetime` | `Transient` | Default lifetime when the attribute does not specify one |
| `SourceGenIocFeatures` | `Register,Container,PropertyInject,MethodInject` | Comma-separated feature flags (case-insensitive). `AsyncMethodInject` requires `MethodInject` (analyzer `SGIOC026`). |

### IServiceProvider invocations collected

`GetService<T>`, `GetRequiredService<T>`, `GetKeyedService<T>`, `GetRequiredKeyedService<T>`, `GetServices<T>`, `GetKeyedServices<T>` and their non-generic overloads.

---

## Parse logic essentials

| Topic | Rule |
|---|---|
| **Settings merge order** | explicit attribute → matching defaults → MSBuild `SourceGenIocDefaultLifetime` → `Transient` |
| **Defaults match priority** | (1) implementation type itself, (2) closest base class, (3) first interface in `AllInterfaces` |
| **`ImplementationTypes` service derivation** | open generic → `TargetServiceType` + configured `ServiceTypes`; closed/non-generic → matched closed types from `AllInterfaces`/`AllBaseClasses`, falling back to `TargetServiceType` if no match (e.g. when framework metadata is invisible) |
| **Key interpretation** | `KeyType=Value` → literal; `KeyType=Csharp` → C# expression (`MyClass.Field`, `nameof(...)`) |
| **Inject attribute matching** | by name only — `IocInjectAttribute` or `InjectAttribute` (any namespace, e.g. `Microsoft.AspNetCore.Components.InjectAttribute`) |
| **Constructor selection** | (1) `[IocInject]`-marked, (2) primary, (3) most parameters |
| **Parameter resolution** | `[ServiceKey]` → registration key; `[FromKeyedServices]`/`[IocInject(Key=…)]` → keyed; `IServiceProvider` → pass through; collection types → element service type |

### Wrapper kinds

`WrapperKind` is a unified enum; each value has a dedicated `WrapperTypeData` subtype.

```text
TypeData
└── GenericTypeData
    └── WrapperTypeData
        ├── CollectionWrapperTypeData → Enumerable / ReadOnlyCollection / Collection / ReadOnlyList / List / Array
        ├── LazyTypeData
        ├── FuncTypeData
        ├── TaskTypeData               (resolves Task<T> for async-init, Task.FromResult(...) otherwise)
        ├── DictionaryTypeData
        └── KeyValuePairTypeData
```

Wrappers nest: `IEnumerable<Lazy<IMyService>>` parses to `EnumerableTypeData(LazyTypeData(IMyService))`.

### Generic factory type mapping

`IocGenericFactoryAttribute` maps service-type parameters to factory-method type parameters. Example for `IRequestHandler<,>`:

```csharp
[IocGenericFactory(typeof(IRequestHandler<Task<int>, decimal>), typeof(decimal), typeof(int))]
public static IRequestHandler<T1, T2> Create<T1, T2>() => new Handler<T1, T2>();
// decimal → T1, int → T2
```

---

## Spec index

### Registration features

| Feature | File |
|---|---|
| Basic registration | [Register.Basic.spec.md](Register.Basic.spec.md) |
| Decorators | [Register.Decorators.spec.md](Register.Decorators.spec.md) |
| Tags | [Register.Tags.spec.md](Register.Tags.spec.md) |
| Injection members | [Register.Injection.spec.md](Register.Injection.spec.md) |
| Imported modules | [Register.ImportModule.spec.md](Register.ImportModule.spec.md) |
| Open generics | [Register.Generics.spec.md](Register.Generics.spec.md) |
| `IServiceProvider` invocations | [Register.ServiceProviderInvocation.spec.md](Register.ServiceProviderInvocation.spec.md) |
| MSBuild configuration | [Register.MSBuild.spec.md](Register.MSBuild.spec.md) |
| Factory & instance | [Register.Factory.spec.md](Register.Factory.spec.md) |
| `KeyValuePair` | [Register.KeyValuePair.spec.md](Register.KeyValuePair.spec.md) |

### Container features

| Feature | File |
|---|---|
| Basic container | [Container.Basic.spec.md](Container.Basic.spec.md) |
| Service lifetime | [Container.Lifetime.spec.md](Container.Lifetime.spec.md) |
| Keyed services | [Container.KeyedServices.spec.md](Container.KeyedServices.spec.md) |
| Injection | [Container.Injection.spec.md](Container.Injection.spec.md) |
| Decorators | [Container.Decorators.spec.md](Container.Decorators.spec.md) |
| Imported modules | [Container.ImportModule.spec.md](Container.ImportModule.spec.md) |
| Factory & instance | [Container.Factory.spec.md](Container.Factory.spec.md) |
| Open generics | [Container.Generics.spec.md](Container.Generics.spec.md) |
| Collections & wrappers | [Container.Collections.spec.md](Container.Collections.spec.md) |
| Container options | [Container.Options.spec.md](Container.Options.spec.md) |
| Thread safety | [Container.ThreadSafety.spec.md](Container.ThreadSafety.spec.md) |
| Partial accessors | [Container.PartialAccessors.spec.md](Container.PartialAccessors.spec.md) |
| MVC & Blazor | [Container.AspNetCore.spec.md](Container.AspNetCore.spec.md) |
| Performance | [Container.Performance.spec.md](Container.Performance.spec.md) |
