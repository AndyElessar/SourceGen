# RegisterAnalyzer

Pipeline of `RegisterAnalyzer`: registration validation, dependency/lifetime checks and key diagnostics. Index: [Ioc Analyzer](SPEC.spec.md).

## CompilationStart Setup

1. Parse `SourceGenIocFeatures` via `ParseIocFeatures`
2. Queue SGIOC026 if `AsyncMethodInject` enabled without `MethodInject`
3. Build `IocAttributeSymbols` cache
4. Early exit if no registration attributes present
5. Build concurrent state: `RegisteredServices`, `ServiceTypeIndex`, and the bags of pending registrations, type-level `[IocRegisterDefaults]` and `[IocImportModule]` attributes. `RegisteredServices` and `ServiceTypeIndex` are keyed by runtime type (`RuntimeTypeSymbolComparer`), like the generator and `ContainerAnalyzer`: tuple element names are not compared, so a dependency on `IHandler<(Entity A, string B)>` finds a registration of `IHandler<(Entity X, string Y)>` (SGIOC002-005, 033). `KeyedServiceIndex` (SGIOC015), the async-init pairs (SGIOC030), the service types of a registration and `ServiceSymbolKey` (whose equality uses `RuntimeTypeSymbolComparer`) compare service types the same way.
6. Construct `AnalyzerContext`
7. Pre-collect assembly-level `IocRegisterFor` attributes as pending registrations, indexed by their syntax (tree and span) for the attribute syntax action
8. Build `IocAttributeNameFilter` from the IoC attribute names

## Registered Actions

| Action Type | Handler | Purpose | Diagnostics |
| :---------- | :------ | :------ | :---------- |
| Symbol (NamedType) | `CollectAndValidateNamedType` | Collect pending registrations, type-level `[IocRegisterDefaults]` and `[IocImportModule]`; validate target type, explicit service types and the explicit lifetime of an `Instance` | SGIOC001, 009, 032 |
| Symbol (NamedType) | `AnalyzeImportedOpenRegistrations` | Imported open generic registrations the compiled assembly cannot construct | SGIOC001 |
| Symbol (Property/Field/Method) | `AnalyzeInjectAttribute` | Validate `[IocInject]` usage and feature gating | SGIOC007, 022, 028 |
| Symbol (Method) | `AnalyzeDuplicatedKeyedServiceAttributes` | Duplicate keyed-service parameter attributes | SGIOC006 |
| Symbol (Method) | `AnalyzeIocGenericFactoryAttribute` | Duplicate placeholders and incomplete generic factory mapping | SGIOC016, 017 |
| SyntaxNode (Attribute) | `AnalyzeAssemblyLevelRegistration` | Assembly-level register-for attributes, looked up by syntax instead of bound again | SGIOC001, 009, 032 |
| SyntaxNode (Attribute) | `AnalyzeFactoryAndInstanceOnAttribute` | Factory/Instance member references, InjectMembers validation | SGIOC008, 010, 016, 017, 023, 024 |
| SyntaxNode (Attribute) | `AnalyzeNameofTypeParameters` | `Factory` / `Instance` / C# `Key` given with `nameof()` of a member of an unbound generic type, and C# `Key` of an open generic registration that uses its type parameters; records the registration attributes they keep from being registered open for SGIOC042 | SGIOC040, 041 |
| SyntaxNode (Attribute) | `AnalyzeOpenTypesNestedInGenericType` | Open types nested in a generic type used by registration and default settings attributes | SGIOC038 |
| SyntaxNode (Attribute) | `ResolveCsharpKeyTypes` | Resolve nameof-based key type symbols | Enables SGIOC013/015 |
| CompilationEnd | `AnalyzeAllDependencies` | Default settings, registration resolution, full-graph dependency/key/async-init/decorator/expanding generic analysis, open generic registrations whose open registration a Factory omits | SGIOC001-005, 011-015, 030, 033-039, 042 |

Handlers of the same symbol kind run in one registered action (one for named types, one for methods, one for properties and fields), and the attribute syntax handlers run in `AnalyzeIocAttributeSyntax`. That action binds an attribute only when `IocAttributeNameFilter` accepts its name: an IoC attribute name, with or without the `Attribute` suffix and optionally qualified, or a using alias in any file whose target is named like an IoC attribute.

The descriptors of the diagnostics reported at compilation end are tagged `CompilationEnd`; SGIOC001, 014 and 038, also reported by file actions, use a second, tagged descriptor there (see [CompilationEnd Diagnostics](SPEC.spec.md#compilationend-diagnostics)).

## CompilationEnd: AnalyzeAllDependencies

1. Collect the default settings (`CollectDefaults`, see [Default Settings](#default-settings)) and report duplicated defaults of the compiled assembly (SGIOC012)
2. Resolve every pending registration against the default settings, in source order (`ResolveRegistrations` → `RegisterService`): effective lifetime, service types, decorators and best default index; report duplicated registrations with their effective tags (SGIOC011), and the open types nested in a generic type that an open implementation would be registered as (SGIOC038)
3. Per `ServiceInfo`: key type mismatch (SGIOC013/014), KVP key mismatch (SGIOC015), Func with arguments lifetime (SGIOC033), dependency DFS (SGIOC002-005)
4. Decorated parameters of the decorators matched while resolving registrations (SGIOC034/035)
5. Expanding dependencies of open generic registrations and their decorators (SGIOC036/037), service types that the mapping of a generic factory does not match (SGIOC039), open generic registrations whose open registration is not registered because their closed types call the Factory (SGIOC042), and implementation types that cannot be constructed without a Factory or Instance (SGIOC001)
6. Build async-init-only pairs → sync dependency analysis of services and applied decorators (SGIOC030)

## Default Settings

Type-level `[IocRegisterDefaults]` (class, struct, interface) and `[IocImportModule]` are only known once every symbol action ran, so registrations are collected as pending registrations and resolved against the complete default settings at compilation end. The settings are ordered like the generator's `DefaultSettingsMap`, where the first settings for a target type win:

- The `[IocRegisterDefaults]` of the compiled assembly, assembly-level and type-level: non-generic attributes, then generic attributes, each in source order. Every settings is kept (a duplicate still registers its `ImplementationTypes` and the open generic target of its `Factory`); SGIOC012 reports the later duplicates.
- Then the settings of each imported module (`[IocImportModule]` in source order, non-generic attributes first): those on the module type, then the assembly-level settings of its assembly (`AnalyzerHelpers.EnumerateImportedDefaultSettingsAttributes`, shared with `ContainerAnalyzer`). A module of the compiled assembly is skipped, since its settings already come first. Like the generator, which reads them without a semantic model, their `Factory` is absent and their `ImplementationTypes` add no registration; they are not reported by SGIOC012.

A registration matches the settings of its base classes, then its interfaces (exactly, then by generic type definition); the first match is the best default, which supplies the lifetime (unless the registration sets one or has an `Instance`), `Tags` (unless the registration sets some), `ServiceTypes`, `RegisterAllInterfaces` / `RegisterAllBaseClasses`, `Decorators` and `Factory`, like the generator's `ProcessSingleRegistration`. A registration that neither sets a lifetime nor matches default settings has the MSBuild `SourceGenIocDefaultLifetime` (read from `build_property.SourceGenIocDefaultLifetime` with the generator's `ReadDefaultLifetime`; Transient when unset or unrecognized), like the generator's `DefaultSettingsMap.FallbackLifetime`. An `Instance` registration without a lifetime is Singleton: neither its default settings nor `SourceGenIocDefaultLifetime` apply to its lifetime, like the generator's `MergeSettings`. This effective lifetime is used by SGIOC003-005 and 033; SGIOC009 needs only the explicit lifetime of the attribute.

## Partial Files

| File | Responsibility |
| :--- | :------------- |
| `RegisterAnalyzer.ServiceCollection.cs` | `AnalyzerContext`, `ServiceInfo`, pending registrations, default settings collection, registration resolution/indexing, assembly-level scan |
| `IocAttributeNameFilter.cs` | Syntactic attribute name pre-check for the attribute syntax action |
| `RegisterAnalyzer.DependencyAnalysis.cs` | `DependencyGraph` (cached constructor dependencies + Tarjan SCC pruning), circular dependency DFS within an SCC, lifetime conflicts, `TryUnwrapServiceType` (private wrapper unwrap) |
| `RegisterAnalyzer.DuplicatedRegistration.cs` | Duplicate registration detection (SGIOC011); SGIOC012 is reported by `CollectDefaults` |
| `RegisterAnalyzer.AttributeUsage.cs` | Inject/factory/instance/generic factory/inject-members validation |
| `RegisterAnalyzer.ExpandingDependencies.cs` | Open generic registrations, including the `ImplementationTypes` of default settings (registered with the generator's `CreateRegistrationDataFromDefaultSettings`), analyzed with the generator's `FindExpandingDependencies` (SGIOC036, 037), the service types of generic factory registrations (SGIOC039), and the open generic registrations whose Factory omits their open registration (SGIOC042) |
| `RegisterAnalyzer.UnresolvableMembers.cs` | ServiceKey type matching, KeyValuePair/Dictionary key analysis, `KeyedServiceIndex` (keyed registrations by assignable type) |
