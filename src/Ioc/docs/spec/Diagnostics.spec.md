# Diagnostic list

> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals.

## Overview

The generator reports all IoC semantic diagnostics. It uses the same analysis result for diagnostics and for generation. This document lists the ID, message, level, consequence, and location of each diagnostic. The rules themselves are defined in the other specifications. This document is only an index with one common format.

The [Diagnostic policy](../Constitution.md#7-diagnostic-policy) of the constitution gives the two kinds of errors, the levels, the three consequences and the two levels of checks. The [common rules](#common-rules) add the details.

Errors that the C# compiler reports (an inaccessible member, an incompatible type, a missing `partial`, and so on) are not in this list. The generator still generates code, and the compile error informs the user.

## Common rules

### Numbering

The numbers are in ranges by category and start at `SGIOC0xx`. Each range has one consequence. The table also shows where the generator reports the diagnostics of each range.

| Range | Category | Consequence | Where the generator reports it |
| --- | --- | --- | --- |
| `SGIOC0xx` | Errors in a single declaration | Skip the declaration | The assembly that has the declaration |
| `SGIOC1xx` | Configuration errors of a container, a module, or a service collection extension | Do not generate the output | That assembly |
| `SGIOC2xx` | Import problems | Do not generate the outputs that depend on the import | The importer |
| `SGIOC3xx` | Errors of the dependency graph (both levels of checks find them) | Do not generate the output | Definite check: each assembly. Full check: the container |
| `SGIOC4xx` | Errors that only the composition root can find | Do not generate the output | `SGIOC401` to `SGIOC404`: the class that has `IocContainerAttribute`. `SGIOC405`: the assembly of the composition root |
| `SGIOC5xx` | Diagnostics that do not affect generation (Warning and Info) | None | `SGIOC501`: the class that has `IocContainerAttribute`. `SGIOC502` and `SGIOC503`: each assembly for the definite check, and the container for the full check. `SGIOC504`: the assembly that generates the service collection extension |

### Levels

When a specification says that something "is an error", it means an IoC semantic error. The generator always reports it at the Error level.

- Error. Configuration files and `#pragma` MUST NOT be able to lower or disable an Error. `#pragma` works on the Warning and Info levels, never on Error.
  - Mechanism: the descriptor of an Error has `WellKnownDiagnosticTags.NotConfigurable`. Then `#pragma`, `NoWarn`, `.editorconfig`, a ruleset and a globalconfig cannot disable it or lower it (tested, see the ADRs).
  - Known effect, which cannot be avoided: when the user suppresses such an Error with `[SuppressMessage]`, the build still fails. The original error message does not show. The user sees only MSB3030 (the DLL to copy is not found).
- Warning. A Warning is for a situation that can be a problem but is not always wrong. It does not affect generation. The descriptor does not have `NotConfigurable`.
  - The user MAY change its level or disable it. `.editorconfig` (`dotnet_diagnostic.SGIOC502.severity`), `#pragma warning disable`, `NoWarn`, `[SuppressMessage]`, and `TreatWarningsAsErrors` all work.
  - The generator does not read the changed level. The generated result is the same when the Warning is disabled and when it is raised to an error. When it is raised to an error, the compiler makes the build fail.
  - These facts hold when the location is built on a `SyntaxTree`, see [Two pipelines](#two-pipelines) (tested, see the ADRs).
- Info. The console output of `dotnet build` does not show an Info. An Info appears only in the IDE and in the SARIF log (tested, see the ADRs).
  - The descriptor of an Info MUST NOT have `WellKnownDiagnosticTags.NotConfigurable`. If it has the tag, `#pragma` cannot suppress the diagnostic.

### Descriptors

| Item | Error | Warning | Info |
| --- | --- | --- | --- |
| `DiagnosticSeverity` | `Error` | `Warning` | `Info` |
| `isEnabledByDefault` | `true` | `true` | `true` |
| `customTags` | `WellKnownDiagnosticTags.NotConfigurable` | None | None (otherwise `#pragma` cannot suppress it) |
| Category (`category`) | `SourceGen.Ioc` | `SourceGen.Ioc` | `SourceGen.Ioc` |

Titles and messages are in English.

### Location

- The model stores only the diagnostic ID, the message arguments, the file path, and the span. At output time, the generator uses `CompilationProvider` to find the `SyntaxTree` again and then creates the `Location` (see [Two pipelines](#two-pipelines)).
- When the problem is in a declaration that is not part of the current compilation (an imported instance definition, a type in metadata), the diagnostic is reported at the place that brings it in: the `IocImportModuleAttribute`, the registration attribute that points to it, or the container class.
- The full check always reports on the class that has `IocContainerAttribute` (on the identifier of the class name).
- When one assembly has more than one container, the generator reports the full check for each container. It reports the definite check one time only for each assembly.

### Names in messages

A diagnostic message about the dependency graph MUST name the module and the instance definition that has the problem.

- Instance definition: the sort name (see [Analysis model: Ordering](Analysis.Model.spec.md#ordering)). When it has a key, ` (Key: {Key})` follows. When it comes from an imported module, ` [module {module class}]` follows.
- Type: the C# display format, without `global::`.
- Dependency site: `{owning instance definition} → {name of the parameter or member}`. For a partial accessor, the owner is the container class (`App.AppContainer → Clock`).

### Consequences

The consequences follow the [Diagnostic policy](../Constitution.md#7-diagnostic-policy) of the constitution:

- **Skip the declaration.** The complete instance definition that the declaration belongs to is skipped, also when its other declarations are valid. The instance definition stays in the dependency graph as an error node. A party that depends on it does not report a missing dependency as a result, and does not go to the fallback provider. Other outputs are processed as usual.
  - When the key of a declaration is invalid (`SGIOC007`, `SGIOC011`, `SGIOC022`), the key is unknown. The declaration then belongs to no instance definition that another declaration has, and only that declaration is skipped. A valid declaration of the same implementation type, for example one without a key, is not affected. Because the key is unknown, a dependency on the type of the skipped declaration with any key that has no other provider does not report a missing dependency.
- **Do not generate the output.** The affected container, module output, or service collection extension is not generated at all. Other outputs are not affected.
- **Inform only.** Warning and Info. Generation is not affected.

### Two pipelines

Diagnostic reporting MUST NOT break incremental caching. The generator MUST use two pipelines:

- Source generation pipeline: only comparable models flow through it. The models do not contain objects such as `Diagnostic`, `Location` or `SyntaxTree`.
- Diagnostic pipeline: the model keeps only comparable diagnostic information (diagnostic ID, message arguments, file path, span). In the output stage, the generator combines the model with `CompilationProvider`. It finds the `SyntaxTree` by the file path, creates the `Location`, and then reports the diagnostic.
  - The generator MUST NOT create the `Location` from the file path and the span alone. `#pragma`, `[SuppressMessage]` and `.editorconfig` do not affect such a location, and a change of severity also has no effect (tested, see the ADRs).
  - The generator MUST NOT put a `Location` in the model. If it does, each edit to the file of the target runs all items of that file again, also when the position does not change (tested, see the ADRs).
  - With this design, the generation pipeline does not run again when a code position moves. The diagnostic pipeline runs again on each change of the compilation, but only its last combine step and its output run (tested, see the ADRs).

A separate analyzer is not necessary. Only a check that does not affect generation and is only advice, such as an optional type check, is a candidate for an analyzer.

## Errors in a single declaration

Error. Skip the declaration. Reported in the assembly that has the declaration.

| ID | Title | Message | Location | Rule |
| --- | --- | --- | --- | --- |
| `SGIOC001` | Inconsistent registration settings | `'{0}' is registered with different {1}; all attributes for the same implementation and key must agree` (`{1}`: `Lifetime` / `Decorators` / `Tags`) | Each attribute that takes part in the merge | [Analysis model: Merging](Analysis.Model.spec.md#merging) |
| `SGIOC002` | Constructor cannot be determined | `'{0}' has {1}; mark exactly one constructor with [IocInject]` (`{1}`: `multiple marked constructors` / `multiple public constructors and no primary constructor` / `no public constructor`) | Registration attribute | [Annotations: Constructor selection](Annotations.spec.md#constructor-selection) |
| `SGIOC003` | Invalid implementation type | `'{0}' cannot be an implementation or decorator type: {1}` (`{1}`: `it is not a class` / `it is abstract` / `it is static` / `it uses a file type` / `it is a container class`) | Type argument of the attribute. For `IocRegisterAttribute`, which has no type argument, the attribute | [Annotations: Checks of the implementation type](Annotations.spec.md#checks-of-the-implementation-type) |
| `SGIOC004` | Invalid service type | `'{0}' cannot be a service type of '{1}': {2}` (`{2}`: `it is a value type` / `it is a pointer or void` / `it is a built-in service` / `it is a container class` / `the implementation type has no base type or interface of this generic type` / `it does not use type parameter '{name}'` / `the type arguments cannot come from a closed service type`) | Type argument of the attribute | [Annotations: Expansion of service types](Annotations.spec.md#expansion-of-service-types) |
| `SGIOC005` | Nested open generic is not supported | `'{0}' is an open generic nested in an open generic type` | Type argument of the attribute | Same as above |
| `SGIOC006` | Wrapper type cannot be registered | `'{0}' is generated automatically and cannot be registered; wrap it in a class or depend on it directly` | Type argument of the attribute | Same as above |
| `SGIOC007` | AnyKey cannot be used here | `KeyedService.AnyKey cannot be used {0}` (`{0}`: `as a registration key` / `to resolve a single service`) | `Key` argument | [Annotations: Keys](Annotations.spec.md#keys), [Key specification](Annotations.spec.md#key-specification) |
| `SGIOC008` | Batch implementation does not implement service type | `'{0}' does not implement '{1}'` | `IocRegisterBatchAttribute` | [Annotations: Expansion of service types](Annotations.spec.md#expansion-of-service-types) |
| `SGIOC009` | Generic factory cannot specify ServiceTypes | `Generic factory '{0}' always registers its return type; remove ServiceTypes` | `ServiceTypes` argument | [Annotations: Service types of a factory](Annotations.spec.md#service-types-of-a-factory) |
| `SGIOC010` | nameof refers to overloaded members | `'{0}' has multiple overloads; nameof cannot choose one` | `nameof` expression | [Annotations: How the generator reads `nameof()`](Annotations.spec.md#how-the-generator-reads-nameof) |
| `SGIOC011` | nameof target not found | `{0} must be a nameof() expression that refers to {1}` | Argument | Same as above |
| `SGIOC012` | Decorator applies to no service type | `Decorator '{0}' has no constructor parameter of any service type of '{1}'` | Registration attribute | [Analysis model: Mapping at the definition level](Analysis.Model.spec.md#mapping-at-the-definition-level) |
| `SGIOC013` | Decorator has multiple inner parameters | `Decorator '{0}' has more than one constructor parameter of type '{1}'` | Registration attribute | Same as above |
| `SGIOC014` | Decorator type parameter cannot be inferred | `Type parameter '{0}' of decorator '{1}' does not appear in its inner parameter` | Registration attribute | Same as above |
| `SGIOC015` | Decorators require a service type | `'{0}' specifies Decorators but has no service type other than itself` | Registration attribute | [Annotations: Other checks](Annotations.spec.md#other-checks) |
| `SGIOC016` | Invalid injection method | `Method '{0}' cannot be injected: {1}` | Method or `IocInjectMemberAttribute` | [Annotations: Checks of an injection method](Annotations.spec.md#checks-of-an-injection-method) |
| `SGIOC017` | LookupMode conflicts with Key | `LookupMode '{0}' cannot be combined with Key` | `[IocInject]` | [Annotations: Key specification](Annotations.spec.md#key-specification) |
| `SGIOC018` | Conflicting key annotations | `'{0}' has both {1} and {2}` (`{1}`, `{2}`: two of `[IocInject]`, `[FromKeyedServices]`, `[ServiceKey]`) | `[IocInject]`, or `[ServiceKey]` when the position has no `[IocInject]` | Same as above |
| `SGIOC019` | Key specified on constructor or method | `[IocInject] on a {0} cannot specify Key or LookupMode; annotate the parameters instead` | `[IocInject]` | Same as above |
| `SGIOC020` | IocInjectMember key on method | `Key cannot be specified for method '{0}'; use a factory registration` | `IocInjectMemberAttribute` | [Annotations: Types that the user does not own](Annotations.spec.md#types-that-the-user-does-not-own) |
| `SGIOC021` | IocInjectMember without registration in scope | `'{0}' has no registration attribute in the same assembly and scope as this [IocInjectMember]` | `IocInjectMemberAttribute` | Same as above |
| `SGIOC022` | Unsupported key value | `A key of type '{0}' is not supported` | `Key` argument | [Annotations: The three forms of a key](Annotations.spec.md#the-three-forms-of-a-key) |
| `SGIOC023` | ServiceKey on non-keyed registration | `Parameter '{0}' has [ServiceKey] but '{1}' is registered without a key` | Registration attribute. For a parameter of a method that `IocInjectMemberAttribute` names, that attribute | [Annotations: Key specification](Annotations.spec.md#key-specification) |
| `SGIOC024` | Member has two injection settings | `Member '{0}' already has another injection setting: {1}` (`{1}`: `[IocInject]` / `another [IocInjectMember]`) | `IocInjectMemberAttribute`. For two attributes, each of them | [Annotations: Types that the user does not own](Annotations.spec.md#types-that-the-user-does-not-own) |
| `SGIOC025` | Attribute on a class that is neither module nor container | `[{0}] on a class has effect only when the class has [IocModule] or [IocContainer]` | Attribute | [Annotations: Attribute declarations](Annotations.spec.md#attribute-declarations) |
| `SGIOC026` | Invalid factory or instance member | `'{0}' cannot be a {1}: {2}` (`{1}`: `factory` / `instance`. `{2}`: `it is not static` / `its containing type is an open generic`) | Registration attribute | [Annotations: Checks of a factory or instance member](Annotations.spec.md#checks-of-a-factory-or-instance-member) |

## Configuration errors of a container, a module, or a service collection extension

Error. Do not generate the output. Reported in that assembly.

| ID | Title | Message | Location | Not generated |
| --- | --- | --- | --- | --- |
| `SGIOC101` | ExplicitOnly with IncludeTags | `ExplicitOnly and IncludeTags cannot both be set on '{0}'` | `IocContainerAttribute` | That container |
| `SGIOC102` | Module is an ExplicitOnly container | `'{0}' cannot be both a module and an ExplicitOnly container` | `IocModuleAttribute` | That container and the module output |
| `SGIOC103` | Multiple modules in one assembly | `Assembly '{0}' has more than one [IocModule] class: {1}` | Each `IocModuleAttribute` | The module output |
| `SGIOC104` | ExplicitOnly container imports its own module | `ExplicitOnly container '{0}' cannot import module '{1}' of its own assembly` | `IocImportModuleAttribute` | That container |
| `SGIOC105` | Inaccessible key in module output | `Key '{0}' of '{1}' is not accessible from other assemblies` | `Key` argument | The module output |
| `SGIOC106` | Inaccessible constraint in module output | `Constraint '{0}' of '{1}' is not accessible from other assemblies` | Registration attribute | The module output |
| `SGIOC107` | Unsupported module or container class | `'{0}' cannot be a {1}: {2}` (`{1}`: `module` / `container`. `{2}` for a container: `it is generic` / `it is nested in a generic type`) | Attribute | That output |
| `SGIOC108` | Invalid Add method name | `'{0}' does not give a valid name for the Add method: {1}` (`{0}`: the `Name` of the attribute, or the assembly name when `Name` is not set. `{1}`: `no identifier characters remain` / `the name cannot start an identifier`) | `IocServiceCollectionExtensionsAttribute` | The service collection extension |

## Import problems

Error. Reported on the `IocImportModuleAttribute` of the importer. The outputs that depend on the import are not generated. An import with assembly scope affects the module output, the service collection extension, and all ordinary containers. An import on an `ExplicitOnly` container affects only that container. Each problem is reported one time only, and it does not cause more diagnostics.

| ID | Title | Message | Rule |
| --- | --- | --- | --- |
| `SGIOC201` | Module information is missing | `'{0}' has no module information; it is not an [IocModule] class or its project has errors` | [Module output: Read sequence](Module.Output.spec.md#read-sequence) |
| `SGIOC202` | Unrecognized module information version | `Module '{0}' uses module information version '{1}', which this version of SourceGen.Ioc cannot read; rebuild it with the same version` | [Module output: Read rules](Module.Output.spec.md#read-rules) |
| `SGIOC203` | Circular module import | `Modules import each other in a cycle: {0}` | [Module output: Read sequence](Module.Output.spec.md#read-sequence) |
| `SGIOC204` | Missing assembly reference | `Module '{0}' needs '{1}' from assembly '{2}', which is not referenced` | Same as above, and [Module output: How the importer resolves types](Module.Output.spec.md#how-the-importer-resolves-types) |
| `SGIOC205` | Registration declared in multiple assemblies | `'{0}' is registered in both '{1}' and '{2}'` | [Analysis model: Merging](Analysis.Model.spec.md#merging) |
| `SGIOC206` | Malformed module information | `Module '{0}' has module information that cannot be read: {1}; rebuild it with the same version of SourceGen.Ioc` (`{1}`: the reason that the reader reports) | [Module output: Read rules](Module.Output.spec.md#read-rules) |

Two conditions occur in the IDE:

- Missing module info: the referenced module project can have errors, or its generator has not run yet. The importer then sees a class that has `IocModuleAttribute` but no module info. The generator handles this as an import problem (`SGIOC201`).
- Stale module info: the generator output of the referenced module project is possibly not the newest. For example, the generator is set to run only on save or on build. The importer then generates from the module info that it reads and does not detect the condition. A command-line build does not have this condition.

## Errors of the dependency graph

Error. Do not generate the output. The definite check reports one time in each assembly, at the dependency site. The full check reports on the container class. See [Analysis model: Two levels of checks](Analysis.Model.spec.md#two-levels-of-checks).

| ID | Title | Message | Rule |
| --- | --- | --- | --- |
| `SGIOC301` | Circular dependency | `Circular dependency: {0}` (`{0}`: the instance definitions joined with ` → `, where the first and the last are the same) | [Analysis model: Circular dependencies](Analysis.Model.spec.md#circular-dependencies) |
| `SGIOC302` | Captive dependency | `Singleton '{0}' depends on scoped '{1}'{2}` (`{2}`: empty when the Singleton depends on the Scoped service directly. Otherwise ` through ` and the path from the Singleton to the Scoped service, with the closed definitions joined with ` → `) | [Analysis model: Lifetime checks](Analysis.Model.spec.md#lifetime-checks) |
| `SGIOC303` | Asynchronous service injected synchronously | `'{0}' is initialized asynchronously; '{1}' must depend on Task<{2}>` | [Analysis model: Asynchronous classification](Analysis.Model.spec.md#asynchronous-classification) |
| `SGIOC304` | Unsupported wrapper combination | `'{0}' is not a supported combination of wrapper types` | [Analysis model: Grammar of wrapper types](Analysis.Model.spec.md#grammar-of-wrapper-types) |
| `SGIOC305` | Invalid Func with arguments | `'{0}' cannot be generated: {1}` | [Analysis model: Mapping of `Func<..., T>`](Analysis.Model.spec.md#mapping-of-func-t) |
| `SGIOC306` | Infinitely recursive generic | `Closing '{0}' requires '{1}', which nests type arguments without end` | [Analysis model: Infinitely recursive generics](Analysis.Model.spec.md#infinitely-recursive-generics) |

## Errors that only the composition root can find

Error. Do not generate the output. Only a container reports `SGIOC401` to `SGIOC404`, on the container class, and that container is not generated. `SGIOC402` applies only to a container without a fallback provider. A container reports `SGIOC405`, and so does an assembly that generates a service collection extension (an assembly that has `IocServiceCollectionExtensionsAttribute`). The generator reports `SGIOC405` in the assembly of the composition root, and that container or service collection extension is not generated.

| ID | Title | Message | Rule |
| --- | --- | --- | --- |
| `SGIOC401` | Ambiguous dependency | `'{0}' requires a single '{1}' but {2} registrations provide it: {3}; use a key or depend on a collection` | [Analysis model: Single service](Analysis.Model.spec.md#single-service) |
| `SGIOC402` | Missing dependency | `'{0}' requires '{1}', which is not registered` | Same as above |
| `SGIOC403` | Disposable Transient | `Transient '{0}' is disposable ({1}); the container does not track transient services` (`{1}`: `IDisposable`, `IAsyncDisposable`, or `IDisposable, IAsyncDisposable`) | [Analysis model: Disposable Transient](Analysis.Model.spec.md#disposable-transient) |
| `SGIOC404` | Duplicate dictionary key | `Dictionary of '{0}' has more than one registration for key '{1}': {2}` | [Analysis model: Multiple services](Analysis.Model.spec.md#multiple-services) |
| `SGIOC405` | Open generic cannot be closed here | `'{0}' must be closed with '{1}', which is not accessible from this assembly` | [Module output: Closing entry](Module.Output.spec.md#closing-entry) |

## Diagnostics that do not affect generation

### Info

Generation is not affected. Reported on the container class, and only for a container that has a fallback provider. The user can suppress it with `#pragma`. The console of `dotnet build` does not show Info diagnostics (see [Levels](#levels)). The dependency graph gives the complete list.

| ID | Title | Message | Rule |
| --- | --- | --- | --- |
| `SGIOC501` | Dependency resolved from the fallback provider | `'{0}' requires '{1}', which is not registered in the container; it is resolved from the fallback service provider` | [Analysis model: Single service](Analysis.Model.spec.md#single-service) |

### Warning

Generation is not affected. The user can change the level (see [Levels](#levels)). For `SGIOC502` and `SGIOC503`, the definite check reports at the location of the dependency site (parameter or member), and the full check reports on the container class. The two do not report the same problem twice. See [Analysis model: Two levels of checks](Analysis.Model.spec.md#two-levels-of-checks). `SGIOC504` is reported only in an assembly that generates a service collection extension.

| ID | Title | Message | Rule |
| --- | --- | --- | --- |
| `SGIOC502` | Singleton holds a transient service | `Singleton '{0}' holds transient '{1}' for the lifetime of the container; depend on Func<{2}> to create instances on demand` | [Analysis model: Warning for a held Transient](Analysis.Model.spec.md#warning-for-a-held-transient) |
| `SGIOC503` | Scoped service holds a transient service | `Scoped '{0}' holds transient '{1}' for the lifetime of the scope; depend on Func<{2}> to create instances on demand` | [Analysis model: Warning for a held Transient](Analysis.Model.spec.md#warning-for-a-held-transient) |
| `SGIOC504` | Ambiguous registrations in Add method | `'{0}' requires a single '{1}' but {2} untagged registrations provide it: {3}; {4} resolves '{5}'` (`{4}`: the method name. `{5}`: the candidate that wins when no tag is given) | [Service collection extension output: Definite ambiguity](ServiceCollection.Output.spec.md#definite-ambiguity) |

## Diagnostics by problem

| Problem | Diagnostic |
| --- | --- |
| Lifetime / Decorators / Tags do not agree | `SGIOC001` |
| More than one constructor and none is marked | `SGIOC002` |
| Invalid implementation type or service type | `SGIOC003`, `SGIOC004` |
| Nested open generic | `SGIOC005` |
| The user registers a wrapper type | `SGIOC006` |
| Registration with `AnyKey`, or `AnyKey` at a site that asks for one service | `SGIOC007` |
| The implementation type of `IocRegisterBatchAttribute` does not implement the service type | `SGIOC008` |
| A generic factory specifies `ServiceTypes` | `SGIOC009` |
| `nameof()` points to overloaded members / the member is not found | `SGIOC010`, `SGIOC011` |
| Decorator: no service type that it applies to / more than one inner parameter / a type parameter that cannot be inferred / no service type other than itself | `SGIOC012` to `SGIOC015` |
| Injection method: a generic method, a parameter with `ref`, `out` or `in`, or a return type of `Task<X>`, `ValueTask` or `async void` | `SGIOC016` |
| `IocInjectAttribute`: `LookupMode` conflicts with `Key` / used together with `[FromKeyedServices]` or `[ServiceKey]` / a key specified on a constructor or method | `SGIOC017` to `SGIOC019` |
| `IocInjectMemberAttribute`: a key used for a method / not in the same assembly or registration scope | `SGIOC020`, `SGIOC021` |
| `ExplicitOnly` and `IncludeTags` are both set | `SGIOC101` |
| One class is both a module and an `ExplicitOnly` container | `SGIOC102` |
| One assembly has more than one module | `SGIOC103` |
| An `ExplicitOnly` container imports the module of its own assembly | `SGIOC104` |
| A key or a generic constraint in the module output refers to an inaccessible type | `SGIOC105`, `SGIOC106` |
| Module info is missing, has a format version that is not recognized, or is malformed | `SGIOC201`, `SGIOC202`, `SGIOC206` |
| Circular import | `SGIOC203` |
| A missing assembly reference for an indirect import | `SGIOC204` |
| The same instance definition occurs in more than one assembly | `SGIOC205` |
| Circular dependency | `SGIOC301` |
| Captive dependency | `SGIOC302` |
| An asynchronous service is injected synchronously | `SGIOC303` |
| Invalid combination of wrapper types | `SGIOC304` |
| The rules of `Func<..., T>` | `SGIOC305` |
| Infinitely recursive generic | `SGIOC306` |
| Ambiguity | `SGIOC401` |
| Missing dependency (no fallback provider) | `SGIOC402` |
| Disposable Transient | `SGIOC403` |
| Duplicate key of a dictionary | `SGIOC404` |
| An open generic must be closed with a type that the composition root cannot write | `SGIOC405` |
| The type of a key value is not supported | `SGIOC022` |
| `[ServiceKey]` on a parameter of a registration without a key | `SGIOC023` |
| A member has two injection settings | `SGIOC024` |
| An attribute on a class that is neither a module nor a container | `SGIOC025` |
| A class that cannot be a module or a container | `SGIOC107` |
| A factory or instance member that is not static, or whose containing type is an open generic | `SGIOC026` |
| The method name `Add{Name}` is not a valid identifier | `SGIOC108` |
| Missing dependency (with a fallback provider) | `SGIOC501` |
| Singleton depends on Transient / Scoped depends on Transient | `SGIOC502`, `SGIOC503` |
| Definite ambiguity in the service collection extension | `SGIOC504` |

## Tests

Each diagnostic needs at least:

- One case that triggers it. The case verifies the ID, the location, and the message arguments.
- One case that is near it but valid. The case verifies that there is no false report.
- Warning: verify that generation is not affected, and that `#pragma` and `.editorconfig` can disable it or change its level.
- Error: verify the consequence. A skipped declaration causes no further diagnostics. An output that is not generated has no output, and the other outputs are still emitted.
- `SGIOC3xx`: verify that the definite check and the full check do not report the same problem twice.

## See also

- [Annotations](Annotations.spec.md)
- [Analysis model](Analysis.Model.spec.md)
- [Module output](Module.Output.spec.md)
