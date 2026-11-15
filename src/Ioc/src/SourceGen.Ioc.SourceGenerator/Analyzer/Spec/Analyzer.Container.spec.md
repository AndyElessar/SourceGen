# ContainerAnalyzer

Pipeline of `ContainerAnalyzer`: container class validation, resolvability and module cycle detection. Index: [Ioc Analyzer](SPEC.spec.md).

## CompilationStart Setup

1. Parse `SourceGenIocFeatures`
2. Build `IocAttributeSymbols`
3. Early exit if `IocContainerAttribute` unavailable
4. Build `ContainerAnalyzerContext`: `Registrations`, `DefaultSettings`, `ModuleImports`, `Containers`, `ImportEdges`
5. Collect assembly-level register-for registrations and register-defaults settings

## Registered Actions

| Action Type | Handler | Purpose | Diagnostics |
| :---------- | :------ | :------ | :---------- |
| Symbol (NamedType) | `AnalyzeContainerClass` | Container feature gate, partial/static/record validation, import edges, UseSwitchStatement | SGIOC019, 020, 031 |
| Symbol (NamedType) | `CollectRegistrationAttributes` | Collect registrations (IocRegister/IocRegisterFor), type-level IocRegisterDefaults and IocImportModule | Feeds 018, 021, 027, 029 |
| CompilationEnd | `AnalyzeContainerDependencies` | Async accessor contracts, container dependency resolvability | SGIOC018, 021, 027, 029 |
| CompilationEnd | `AnalyzeCircularImports` | Module import cycle detection via DFS | SGIOC025 |

The two named type handlers run in one registered action.

## CompilationEnd: AnalyzeContainerDependencies

Setup: `ResolveRegistrations` applies `[IocRegisterDefaults]` to every registration once (see [Registration Resolution](#registration-resolution)); `BuildAsyncInitServiceKeys` builds the (service type, key) pairs with an async-init registration once: an async-init implementation, or a decorator with async inject methods applied to the service type (see [Async-init Registrations](Diagnostics.AsyncInit.spec.md#async-init-registrations); empty when `AsyncMethodInject` is disabled)
Pass 1 (all containers): `AnalyzeAsyncPartialAccessors` → SGIOC027 (sync accessor on async-init), SGIOC029 (unsupported async return)
Pass 2 (IntegrateServiceProvider=false only): `ContainerScopeBuilder` computes each container's registrations (see [Container Scope](#container-scope)); `CollectUnresolvedDependencies` checks them (SGIOC018) and `AnalyzePartialAccessorDependencies` checks the partial accessors (SGIOC021). Dependencies of each implementation type are inspected once (`DependencyCache`), and containers that use every registration of the compilation share one SGIOC018 result.

## Registration Resolution

Mirrors the generator's `ProcessSingleRegistration`:

- Defaults order: current-assembly `[IocRegisterDefaults]` (non-generic attributes, then generic attributes, each in source order), then the defaults of imported module assemblies (settings on the module type, then assembly-level settings). The first settings for a target type win.
- Defaults match the implementation's base classes, then its interfaces, exactly or by generic type definition. Each matched type becomes a service type; the first match is the best default.
- The best default adds its `ServiceTypes`, and supplies `Tags`, `RegisterAllInterfaces` and `RegisterAllBaseClasses` when the registration does not set them.
- An open generic service type (explicit or from defaults) of a closed implementation is replaced by each closed form the implementation implements or inherits, like the generator's `AddExplicitServiceType`.
- `[IocRegisterDefaults(ImplementationTypes = ...)]` of the current assembly adds a registration per implementation type with the attribute's settings and no default settings lookup (`CreateRegistrationDataFromDefaultSettings`): a closed implementation is registered as the types of its hierarchy named like the target or `ServiceTypes` (ignoring generic arguments), or as the target and `ServiceTypes` when none matches. `ImplementationTypes` of imported module defaults add no registration.

## Container Scope

| Container | Own registrations |
| :-------- | :---------------- |
| `ExplicitOnly = true` | `[IocRegisterFor]` on the container class |
| `IncludeTags` non-empty | Registrations with at least one matching (effective) tag |
| Otherwise | All registrations of the compilation |

The container's service types are the service types of its own registrations plus the service types of its imported modules (same-compilation modules use their own scope, recursively). A module from another assembly cannot be enumerated: its container is marked as having an external module.
