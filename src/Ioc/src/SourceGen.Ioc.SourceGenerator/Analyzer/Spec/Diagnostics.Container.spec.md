# Container Diagnostics

Diagnostics reported by ContainerAnalyzer for container classes: declaration, feature gating, resolvability and module imports. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC018 - Error - Design - Unable to resolve service

Report when a container has `IntegrateServiceProvider = false` and a constructor dependency cannot be resolved from registered services.

**Analysis:**

- Applies only when `[IocContainer(IntegrateServiceProvider = false)]` is used.
- Each container is analyzed with its own registrations ([Container Scope](Analyzer.Container.spec.md#container-scope)): `ExplicitOnly`, `IncludeTags` and imported modules decide which implementation types are analyzed and which service types resolve a dependency.
- Analyzes every implementation type of the container's own registrations. Implementations of imported modules are analyzed as part of their own container. Service types that are only exposed by a registration (interfaces, base classes from `RegisterAllBaseClasses`, explicit `ServiceTypes`) are NOT analyzed as implementations, because the generator only constructs the implementation type and injects the members declared on it.
- The dependencies checked depend on how the container creates the instance of a registration:
  - Constructor (default): the constructor parameters, the properties, fields and method parameters with `[IocInject]` or `[Inject]`, and the members listed in `InjectMembers` (`[IocInject]` on a member takes precedence). `InjectMembers` dependencies are reported on their attribute element, so they are checked even when the implementation type is declared in another assembly. Like the generator, an `InjectMembers` member is resolved on the registered implementation type whatever type arguments its `nameof()` uses (`nameof(Node<int>.Repo)` of `Node<T>` checks `IRepo<T>`, which is not analyzed because it uses a type parameter).
  - `Factory` (of the registration, otherwise of the matching `[IocRegisterDefaults]`): only the factory method parameters, except `IServiceProvider` and `[ServiceKey]`. The constructor and injected members are not checked. A factory that cannot be resolved (a string, or settings imported from another assembly) is not analyzed. Factory methods declared in another assembly are not analyzed.
  - `Instance`: no dependencies.
- For each dependency:
  - Checks if the dependency type is a service type of the container (including its imported modules). Like the generator, service types are compared without tuple element names, which are not part of the runtime type (`IHandler<(Entity A, string B)>` is resolved by a registration of `IHandler<(Entity X, string Y)>`); an open generic registration is closed with the type arguments of a dependency, whose element names constraint checks keep (see [Generic Registration](../../Spec/Register.Generics.spec.md)). A dependency is resolved when any spelling of its runtime type among the dependencies of the container's registrations (and the element types of supported wrappers, like the generator's `CollectClosedGenericDependencyFromType`; not the type arguments of other generic types) can close the registration: the generator closes the registration with that spelling, and the container resolves every spelling of the runtime type (`IHandler<(int A, int B), (int X, int Y)>` is resolved through `IHandler<(int X, int Y), (int X, int Y)>` for `Handler<T, U> where T : U`).
  - Supported wrappers resolve their inner service with the same key, like the generator: collections (`IEnumerable<T>`, `IReadOnlyCollection<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IList<T>`), `Lazy<T>`, `Func<T>` and `KeyValuePair<TKey, T>` unwrap recursively (e.g. `IEnumerable<Lazy<T>>`); `Task<T>` and `Func<..., T>` with input parameters need a registration of `T` itself; `IDictionary<TKey, T>` (and `IReadOnlyDictionary`, `Dictionary`) and collections of `KeyValuePair<TKey, T>` need a non-keyed dependency and a keyed registration of `T`. `ValueTask<T>` and unsupported nesting (e.g. `Task<Lazy<T>>`) are plain service types. Array dependencies are not checked.
  - Injected `IEnumerable<T>` properties are not checked.
  - Checks if the dependency type is a built-in service type (e.g., `IServiceProvider`, `IServiceScopeFactory`).
  - Parameters with a default value or `[ServiceKey]`, and `[FromKeyedServices]` without a key, are not checked.
- Keyed dependencies: the key of a parameter is the key of `[FromKeyedServices(key)]`, otherwise the key of `[IocInject]` / `[Inject]` on the parameter, otherwise the key of its injected method (`[IocInject(Key = ...)]` on the method, or the key of its `InjectMembers` element); the key of a property or field is the key of its `[IocInject]` / `[Inject]` or `InjectMembers` element.
  - A keyed dependency is resolved by a registration of the same service type with the same key; a non-keyed dependency only by a non-keyed registration. Generated containers look keyed services up by key value (`ServiceIdentifier`), so keys are compared by constant value and type (e.g. the enum `Region.West` does not match the integer `1`).
  - A key given as C# code (`KeyType = KeyType.Csharp`) cannot be evaluated: a registration with such a key resolves any keyed dependency of its service type, and a dependency with such a key is resolved by any registration of its service type.
  - The message shows the key: `Unable to resolve service 'IDependency (key "A")' for container 'AppContainer'`.
- Reports when a dependency cannot be resolved and there is no fallback to external `IServiceProvider`.
- Does NOT report for a container that imports a module from another assembly (directly or through a same-compilation module), because that module's registrations cannot be enumerated.

- Open generic registrations: the generator closes them on demand for the dependencies it discovers, so a closed generic dependency (e.g. `IRepository<User>`) is resolved by a registration of its open generic service type (`IRepository<>`) in the container's own registrations, with the same key rules, except that the generator never closes a non-keyed open generic registration for a keyed dependency: a dependency with a key given as C# code is only resolved by a keyed open generic registration (`[IocInject(nameof(MyKeys.X), KeyType = KeyType.Csharp)] IRepository<User>` with only a non-keyed `Repository<T>` is reported), with any key, and a dependency with a value key by one with the same key or a key given as C# code.
  - Like the generator, a registration of the closed type with another key does not keep the open generic registration from being closed for the dependency's key: a non-keyed `IRepository<User>` is resolved by `Repository<T>` registered for `IRepository<>` even when `IRepository<User>` also has a registration with `Key = "k"` (see [Generic Registration](../../Spec/Register.Generics.spec.md)).
  - In a container without `ExplicitOnly`, the sources are open generic registrations of the compilation, current-assembly `[IocRegisterDefaults]` with a `Factory` and an open generic target, and the open generic `[IocRegisterFor]` of every imported module (recursively; for any `[IocImportModule]` of the compilation, like the generator's `ExtractOpenGenericEntriesFromModuleRecursive`) whose implementation the compiled assembly can construct (`SGIOC001`).
  - In an `ExplicitOnly` container, only the open generic `[IocRegisterFor]` on the container class.
  - Like the generator, an open generic registration only resolves the closed forms its implementation can be closed for: the service type as the implementation declares it (itself, a base class, or an interface) must match the dependency, determine every type parameter of the implementation, and the type arguments must satisfy its `class`, `struct`, `unmanaged`, `new()` and type constraints (`notnull` is not checked). Type constraints follow the generator's conversions: covariant conversions one level deep (including to `object` for interface type arguments), without contravariant or nested variant conversions, and with tuple element names compared; `new()` is not satisfied by a type with required members unless its parameterless constructor has `[SetsRequiredMembers]` (see [Generic Registration](../../Spec/Register.Generics.spec.md)). For example, `Repository<T> : IRepository<T> where T : struct` does not resolve `IRepository<User>`. Open generic `Factory` registrations resolve every closed form.
  - Dependencies that use type parameters of an open generic implementation (e.g. `IValidator<T>` in `Repository<T>`) are not checked, because they depend on the closed forms the generator creates. Implementations declared in another assembly are not analyzed.

**Message format:** `Unable to resolve service '{ServiceType}' for container '{ContainerType}'.`

---

## SGIOC019 - Error - Usage - Container class must be partial and can not be static

Report when a class marked with `[IocContainer]` is not declared as `partial`, is declared as `static`, or is a `record`. The generator only handles `partial` class declarations.

**Analysis:**

- Checks the syntax declaration of classes marked with `[IocContainer]` attribute.
- Verifies that the class has the `partial` modifier.
- Verifies that the class does not have the `static` modifier.
- Verifies that the declaration is not a `record`.
- Reports when the `partial` modifier is missing, the `static` modifier is present, or the declaration is a `record`.

**Message format:** `Container class '{ClassName}' must be declared as partial and cannot be static or a record`

---

## SGIOC020 - Warning - Usage - UseSwitchStatement is ignored when importing modules

Report when a class marked with `[IocContainer]` specifies `UseSwitchStatement = true` and has one or more `[IocImportModule]` attributes.

**Analysis:**

- Checks classes marked with `[IocContainer]` attribute.
- Retrieves the `UseSwitchStatement` property value from the attribute (defaults to `false`).
- Scans the class for `[IocImportModule]` or `[IocImportModule<T>]` attributes.
- Reports when `UseSwitchStatement = true` is explicitly set and at least one import module attribute exists.

**Rationale:**

When a container imports modules, service registrations come from multiple sources at runtime. The switch statement optimization requires all service types to be known at compile time, which is not possible with imported modules. Therefore, `UseSwitchStatement` is silently ignored and `FrozenDictionary` is used instead.

**Message format:** `Container '{ContainerType}' specifies UseSwitchStatement = true but has imported modules; the setting will be ignored and FrozenDictionary will be used instead.`

---

## SGIOC021 - Error - Design - Unable to resolve partial accessor service

Report when a partial method or property accessor in a container class references a service type that is not registered and `IntegrateServiceProvider = false`.

**Analysis:**

- Checks classes marked with `[IocContainer]` attribute that have `IntegrateServiceProvider = false`.
- Scans the container class members for partial methods (non-void, parameterless, non-generic) and partial properties (with getter).
- For non-nullable partial accessors, determines the effective service type by:
  - Reporting SGIOC021 immediately for unsupported return types: non-generic `Task`, non-generic `ValueTask`.
  - `ValueTask<T>`: unwraps `T` and checks registration. If `T` references an async-init service, `SGIOC029` is reported instead (see SGIOC029). Otherwise `SGIOC021` is reported.
  - Recursively unwraps Generator-supported wrapper types to extract the innermost service type for resolution checking. Supported wrappers: `Task<T>`, `Lazy<T>`, `Func<T>` / `Func<T1,...,TReturn>` (extracts the last type argument as the return/service type), `IEnumerable<T>`, `IReadOnlyCollection<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IList<T>`, `T[]`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>`, `Dictionary<K,V>`, `KeyValuePair<K,V>`.
  - Mirrors the Generator downgrade rules while recursively unwrapping wrappers:
    - `Task<Wrapper<T>>` and `Wrapper<Task<T>>` are treated as unresolvable when `IntegrateServiceProvider = false`.
    - `ValueTask<T>` is not a Generator-supported wrapper; if encountered during wrapper recursion, the accessor is treated as unresolvable.
    - A top-level collection wrapper whose element type contains nested non-collection wrappers, for example `IEnumerable<Lazy<Func<T>>>`, is treated as unresolvable when `IntegrateServiceProvider = false`.
  - If the innermost unwrapped service type matches a registration (with the same service key) that has async-init implementations, `SGIOC021` skips the accessor and `SGIOC029` owns the return-type diagnostic regardless of `IntegrateServiceProvider`.
  - For supported wrapper shapes that successfully unwrap, registration lookup and the diagnostic message use the innermost service type.
  - For downgraded or unsupported wrapper shapes whose innermost service type is not async-init, the accessor is treated as unresolvable and the diagnostic message uses the full return type.
  - For non-wrapper types, the return type itself is checked directly.
- Reports when a non-nullable partial accessor's effective service type is not found among the container's registrations, including the services of its imported modules ([Container Scope](Analyzer.Container.spec.md#container-scope)), with the same service key. The generated accessor resolves services it does not register itself through the merged resolvers, which include the services of imported modules.
- When the container imports a module of another assembly (directly or transitively), its services cannot be enumerated, so only unsupported and downgraded return types are reported.
- Nullable accessors are exempt (can safely return `null`).

**Rationale:**

When `IntegrateServiceProvider = false`, there is no fallback to an external `IServiceProvider`. If a partial accessor references a service type that is not registered after recursively unwrapping any Generator-supported wrapper, it cannot be resolved at runtime. Wrapper shapes that the Generator downgrades to `IServiceProvider` fallback are also unresolvable in this mode. Unsupported return types cannot be generated regardless of registration state. If the innermost unwrapped service type is async-init, the accessor is excluded from `SGIOC021` because `SGIOC029` owns async-init partial accessor return-type mismatches for all containers.

**Message format:** `Unable to resolve service '{ServiceType}' for partial accessor '{MemberName}' in container '{ContainerType}'.`

---

## SGIOC025 - Error - Design - Circular module import detected

Report when a container has a circular module import dependency (direct or transitive).

**Analysis:**

- Checks all classes marked with `[IocContainer]` attribute.
- Scans for `[IocImportModule]` or `[IocImportModule<T>]` attributes on each container class.
- Builds a directed graph of container → imported module relationships.
- Uses depth-first search to detect cycles in the import graph.
- Detects direct cycles (A imports B, B imports A) and transitive cycles (A imports B, B imports C, C imports A).
- Reports on each container participating in the cycle.

**Rationale:**

Circular module imports create static initializer deadlocks. When `_serviceResolvers` is a `private static readonly` field, the CLR's static type initializer runs on first access. If Container A's static initializer accesses `ModuleB.Resolvers` and Module B's static initializer accesses `ModuleA.Resolvers`, a deadlock occurs.

**Message format:** `Container 'TestNamespace.ModuleA' has a circular module import dependency: TestNamespace.ModuleA → TestNamespace.ModuleB → TestNamespace.ModuleA`

Both `{ContainerType}` and types in `{CyclePath}` use `NameAndContainingTypesAndNamespaces` display format (without `global::` prefix). For types in the global namespace, this format may produce simple names.

---

## SGIOC031 - Warning - Usage - Container ignored due to disabled feature

Report when a class is marked with `[IocContainer]` but `SourceGenIocFeatures` does not include `Container`.

**Analysis:**

- Reads `SourceGenIocFeatures` during `CompilationStart` (missing/empty value enables `Container`).
- Checks classes marked with `[IocContainer]`.
- Reports on the `[IocContainer]` attribute when the `Container` feature is disabled, because the generator skips every container.

**Message format:** `Container '{ContainerType}' will not be generated because the Container feature is not enabled. Add 'Container' to <SourceGenIocFeatures> in your project file.`
