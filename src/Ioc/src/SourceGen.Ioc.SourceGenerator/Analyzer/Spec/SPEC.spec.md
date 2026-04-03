# Ioc Register Analyzer

## Diagnostics

Format: ID - Level - Category - Description

### SGIOC001 - Error - Usage - Invalid Attribute Usage

Report when IoCRegisterAttribute or IoCRegisterForAttribute is mark on private or abstract class.

**Analysis:**

- Collects all types marked with `[IoCRegister]` or `[IoCRegisterFor]` (both assembly-level and type-level).
- Checks the target type's accessibility modifier and abstract state.
- Reports when the type is `private` or is an `abstract` class (not interface).

---

### SGIOC002 - Error - Design - Circular Dependency Detected

Report when circular dependencies are detected among registered services.

**Analysis:**

- Collects all registered services and their constructor parameter dependencies.
- Builds a service type index (interface/base class → implementation type) for fast dependency lookup.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types to extract the inner service type for dependency resolution.
- Uses depth-first search to traverse the dependency graph.
- Reports when starting from a service and following the dependency path leads back to the same service.

---

### SGIOC003 - Error - Design - Service Lifetime Conflict Detected

Report when there are singleton service depending on scoped service.

**Analysis:**

- Collects all registered services with their lifetimes (Singleton/Scoped/Transient).
- Analyzes each service's constructor parameters and queries the dependency's lifetime.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types to detect captive dependencies through wrappers.
- Reports when a Singleton service depends on a Scoped service.

---

### SGIOC004 - Error - Design - Dangerous Service Lifetime Dependency Detected

Report when there are singleton service depending on transient service.

**Analysis:**

- Same as SGIOC003, analyzes lifetime relationships between services.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types.
- Reports when a Singleton service depends on a Transient service.

---

### SGIOC005 - Error - Design - Dangerous Service Lifetime Dependency Detected

Report when there are scoped service depending on transient service.

**Analysis:**

- Same as SGIOC003, analyzes lifetime relationships between services.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types.
- Reports when a Scoped service depends on a Transient service.

---

### SGIOC006 - Warning - Usage - Duplicated Attribute Usage

Report when `FromKeyedServicesAttribute` and `IocInjectAttribute`/`InjectAttribute` mark on one parameter. `FromKeyedServicesAttribute` takes precedence.

**Analysis:**

- Checks attributes on method parameters.
- Reports when a parameter has both `[FromKeyedServices]` and `[IocInject]`/`[Inject]`.

---

### SGIOC007 - Error - Usage - Invalid Attribute Usage

Report when `IocInjectAttribute`/`InjectAttribute` is mark on:

- static member
- member without public, internal, or protected internal accessibility
- property without setter or with private setter
- readonly field
- method with an unsupported return type
- method that is generic (has type parameters)
- method that is not an ordinary method (e.g., constructor, operator)

**Analysis:**

- Checks members (properties, fields, methods) marked with `[IocInject]` or `[Inject]`.
- Reports when:
  - Member is static.
  - Member is not `public`, `internal`, or `protected internal` (private, protected, or private protected members are rejected because generated code runs in a public static context).
  - Property has no setter or setter is private.
  - Field is readonly.
  - Method returns a type other than `void` or supported non-generic `Task`. `async void` is handled separately by `SGIOC028`.
  - Method is generic (has type parameters).
  - Method is not an ordinary method (i.e., constructors, operators, and other special methods are rejected).

#### Method Return-Type Truth Table

|Method shape|`AsyncMethodInject` enabled|Diagnostic result|
|:-----------|:--------------------------|:----------------|
|`void Initialize(...)`|No/Yes|No return-type diagnostic.|
|`void Initialize(...)` declared as `async void`|No/Yes|`SGIOC028` MUST report. `SGIOC007` SHOULD NOT duplicate the return-type diagnostic.|
|`Task InitializeAsync(...)`|Yes|No `SGIOC007` return-type diagnostic.|
|`Task InitializeAsync(...)`|No|`SGIOC022` MUST report the disabled feature. `SGIOC007` MUST NOT report a duplicate return-type diagnostic.|
|`Task<T> InitializeAsync(...)`|No/Yes|`SGIOC007` MUST report.|
|`ValueTask InitializeAsync(...)` or `ValueTask<T> InitializeAsync(...)`|No/Yes|`SGIOC007` MUST report.|
|Any other non-void return type|No/Yes|`SGIOC007` MUST report.|

---

### SGIOC008 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute`/`IoCRegisterForAttribute`/`IoCRegisterDefaultsAttribute` has specify `Factory` or `Instance` and use nameof(), but field/property/method in nameof() is not static or is inaccessible.

**Analysis:**

- Checks the `Factory` or `Instance` parameter of `[IoCRegister]`/`[IoCRegisterFor]`/`[IoCRegisterDefaults]` attributes.
- When `nameof()` is used to specify a member, resolves that member symbol.
- Reports when:
  - Member is not static.
  - Member is private or protected.
  - Member's containing type is private.

---

### SGIOC009 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Instance` and `Lifetime` is not Singleton.

**Analysis:**

- Checks registration attributes that specify `Instance`.
- Gets the `Lifetime` setting from the attribute.
- Reports when `Lifetime` is explicitly set to a value other than Singleton.

---

### SGIOC010 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Factory` and `Instance` at same attribute. `Factory` takes precedence.

**Analysis:**

- Checks named arguments of registration attributes.
- Reports when both `Factory` and `Instance` are specified on the same attribute.

---

### SGIOC011 - Warning - Design - Duplicated Registration Detected

Report when there are duplicated registrations for same implement type, same key, and at least one matching tag.
When TagOnly=false, the registration is considered to have an empty tag for comparison.

**Analysis:**

- Collects all registrations and builds an index of `(FullyQualifiedTypeName, Key, Tag)`.
- When `TagOnly=false`, automatically adds an empty string as an additional Tag.
- Checks each effective Tag; reports if the same combination already exists.

---

### SGIOC012 - Warning - Design - Duplicated Registration Detected

Report when there are duplicated `IoCRegisterDefaults` for same target type and at least one matching tag.
When TagOnly=false, the setting is considered to have an empty tag for comparison.

**Analysis:**

- Collects all `[IoCRegisterDefaults]` (both assembly-level and type-level).
- Builds an index of `(TargetTypeName, Tag)`.
- When `TagOnly=false`, automatically adds an empty string as an additional Tag.
- Checks each effective Tag; reports if the same combination already exists.

---

### SGIOC013 - Error - Usage - Key type is unmatched

Report when `ServiceKeyAttribute` is mark on parameter, but the key type is not matched with the registered key type from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Collects each service registration's Key type (from generic parameter or `KeyType` property of `[IoCRegister]` or `[IoCRegisterFor]`).
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`.
- Reports when the parameter type is not compatible with the registered Key type.
- Exception: Skips type checking when `KeyType` is `Csharp`.

---

### SGIOC014 - Warning - Usage - Key does not exists

Report when `ServiceKeyAttribute` is mark on parameter, but there is no specified `Key` from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Checks whether the service registration has a `Key` specified.
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`.
- Reports when `[ServiceKey]` is used but the registration has no Key specified.

---

### SGIOC015 - Warning - Usage - KeyValuePair's Key type is unmatched

Report when a `KeyValuePair<K, V>` is injected but the registered key type of `V` does not match `K`.

**Analysis:**

- Runs during CompilationEnd phase after all services are collected.
- For each registered service, scans constructor parameters and `[IocInject]`/`[Inject]` members (method parameters, properties, fields).
- Detects parameters/members whose type contains key-value semantics:
  - `KeyValuePair<K, V>` directly
  - `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`
  - Collection types wrapping `KeyValuePair<K, V>`: `IEnumerable<KeyValuePair<K, V>>`, `IReadOnlyCollection<KeyValuePair<K, V>>`, `ICollection<KeyValuePair<K, V>>`, `IReadOnlyList<KeyValuePair<K, V>>`, `IList<KeyValuePair<K, V>>`, `KeyValuePair<K, V>[]`
- For each detected key-value parameter with key type `K` and value type `V`:
  - Iterates all registered services to find those with `HasKey = true` and whose `Type` implements interface `V`, has `V` as a base class, or equals `V`.
  - If at least one keyed registration for `V` exists but **none** have a key type compatible with `K`, reports the diagnostic.
- Key type compatibility rules:
  - `K = object` is always compatible with any key type (no diagnostic).
  - If a registration uses `KeyType = Csharp` with `nameof()`, key type is resolved from the referenced symbol and checked for compatibility.
  - If a registration uses `KeyType = Csharp` with a string literal key (not `nameof()`), `KeyTypeSymbol` is `null` and compatibility analysis is skipped (no diagnostic).
  - Otherwise, reports if the registration's `KeyTypeSymbol` is not assignable to `K`.
- Skips parameters that have `[FromKeyedServices]` attribute (those resolve specific keyed services, not KVP aggregation).

**Message format:** `KeyValuePair parameter '{0}' has key type '{1}' but no keyed service for '{2}' has a matching key type`

---

### SGIOC016 - Error - Design - Factory Method is unmatched

Report when:

- `[IocRegister]`/`[IoCRegisterFor]`/`[IoCRegisterDefaults]` has specificed `Factory` but the factory method is generic and does not mark with `[IocGenericFactory]`.

**Analysis:**

- Checks Factory member specified via `nameof()` on `[IocRegister]`, `[IoCRegisterFor]`, or `[IoCRegisterDefaults]` attributes.
- When the Factory references a method symbol, checks if the method is generic (has type parameters).
- If the method is generic, checks if it has `[IocGenericFactory]` attribute.
- The diagnostic does NOT fire if `GenericFactoryTypeMapping` is provided on the registration attribute (`IocRegisterForAttribute` or `IocRegisterDefaultsAttribute`) AND the number of placeholder types (mapping array length minus 1) equals the factory method's type parameter count.
- Reports when the factory method is generic but neither `[IocGenericFactory]` attribute on the method NOR a valid `GenericFactoryTypeMapping` on the registration attribute provides the type mapping.

---

### SGIOC017 - Error - Design - Generic Factory Method's type parameters are duplicated

Report when:

- `[IocGenericFactory]`'s type parameters from second to end have duplicated types, they must be unique.
- `GenericFactoryTypeMapping` property on `[IocRegisterFor]` or `[IocRegisterDefaults]` attribute contains duplicate placeholder types.

**Analysis:**

- Checks methods marked with `[IocGenericFactory]` attribute.
  - Extracts the type array from the attribute's constructor arguments.
  - Starting from the second type (index 1), checks if any type appears more than once.
  - Reports when duplicate placeholder types are found, as each type must uniquely map to a factory method type parameter.
- Checks the `GenericFactoryTypeMapping` property on `[IocRegisterFor]` or `[IocRegisterDefaults]` attributes.
  - Validates that all placeholder types in the mapping are unique.
  - Reports when duplicate placeholder types are detected in the mapping.

---

### SGIOC018 - Error - Design - Unable to resolve service

Report when a container has `IntegrateServiceProvider = false` and a constructor dependency cannot be resolved from registered services.

**Analysis:**

- Applies only when `[IocContainer(IntegrateServiceProvider = false)]` is used.
- Analyzes all registered services in the container scope.
- For each service's dependencies:
  - Constructor parameters: Checks if the dependency type is registered in the container.
  - Properties/Fields with `[IocInject]` or `[Inject]`: Checks if the dependency type is registered in the container.
  - Method parameters with `[IocInject]` or `[Inject]`: Checks if the dependency type is registered in the container.
  - Checks if the dependency type is a built-in service type (e.g., `IServiceProvider`, `IServiceScopeFactory`).
- Reports when a dependency cannot be resolved and there is no fallback to external `IServiceProvider`.

**Message format:** `Unable to resolve service '{ServiceType}' for container '{ContainerType}'.`

---

### SGIOC019 - Error - Usage - Container class must be partial and can not be static

Report when a class marked with `[IocContainer]` is not declared as `partial` or is declared as `static`.

**Analysis:**

- Checks the syntax declaration of classes marked with `[IocContainer]` attribute.
- Verifies that the class has the `partial` modifier.
- Verifies that the class does not have the `static` modifier.
- Reports when the `partial` modifier is missing or the `static` modifier is present.

**Message format:** `Container class '{ClassName}' must be declared as partial.`

---

### SGIOC020 - Warning - Usage - UseSwitchStatement is ignored when importing modules

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

### SGIOC021 - Error - Design - Unable to resolve partial accessor service

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
- Reports when a non-nullable partial accessor's effective service type is not found among registered services.
- Nullable accessors are exempt (can safely return `null`).

**Rationale:**

When `IntegrateServiceProvider = false`, there is no fallback to an external `IServiceProvider`. If a partial accessor references a service type that is not registered after recursively unwrapping any Generator-supported wrapper, it cannot be resolved at runtime. Wrapper shapes that the Generator downgrades to `IServiceProvider` fallback are also unresolvable in this mode. Unsupported return types cannot be generated regardless of registration state. If the innermost unwrapped service type is async-init, the accessor is excluded from `SGIOC021` because `SGIOC029` owns async-init partial accessor return-type mismatches for all containers.

**Message format:** `Unable to resolve service '{ServiceType}' for partial accessor '{MemberName}' in container '{ContainerType}'.`

---

### SGIOC022 - Warning - Usage - Inject attribute ignored due to disabled feature

Report when a member has `[IocInject]`/`[Inject]` but its corresponding feature is disabled by `SourceGenIocFeatures`.

**Analysis:**

- Reads `SourceGenIocFeatures` from `AnalyzerConfigOptionsProvider.GlobalOptions` once during `CompilationStart`.
- Parses comma-separated feature names case-insensitively, trimming whitespace; missing/empty value defaults to `Register,Container,PropertyInject,MethodInject`.
- Checks members marked with `[IocInject]`/`[Inject]`:
  - `IPropertySymbol` requires `PropertyInject`
  - `IFieldSymbol` requires `FieldInject`
  - `IMethodSymbol` returning `void` requires `MethodInject`
  - `IMethodSymbol` returning non-generic `Task` requires `AsyncMethodInject`
- Reports when the required feature flag is not enabled.

#### Feature Gate Mapping

|Member shape|Required feature|Notes|
|:-----------|:---------------|:----|
|Property|`PropertyInject`|Unchanged.|
|Field|`FieldInject`|Unchanged.|
|Method returning `void`|`MethodInject`|Covers synchronous method injection.|
|Method returning non-generic `Task`|`AsyncMethodInject`|`MethodInject` remains a project-level prerequisite when `AsyncMethodInject` is enabled; invalid combinations are reported by `SGIOC026`.|

**Message format:** `'{MemberName}' has [IocInject] but {FeatureName} feature is not enabled. Add '{FeatureName}' to <SourceGenIocFeatures> in your project file.`

---

### SGIOC023 - Error - Usage - Invalid InjectMembers element format

Report when an element in the `InjectMembers` array is not in a recognized format.

**Analysis:**

- Checks the `InjectMembers` named argument array on `[IocRegisterFor]` registration attributes.
- Validates each array element:
  - Must be one of:
    - A `nameof()` expression resolving to a valid member
    - A `new object[]` with exactly 2 or 3 elements where:
      - First element is a `nameof()` expression
      - Second element is a key (of any type)
      - Optional third element is a `KeyType` enum constant (`KeyType.Value` or `KeyType.Csharp`)
  - Arrays with more than 3 elements are rejected.
  - If 3 elements, the third must be a valid `KeyType` enum constant.
- Reports when an element does not match one of the recognized formats.

---

### SGIOC024 - Error - Usage - Member specified in InjectMembers cannot be injected

Report when a member resolved from `nameof()` in `InjectMembers` cannot be injected.

**Analysis:**

- Checks members specified via `nameof()` in the `InjectMembers` array.
- Reports when the member is:
  - static
  - not `public`, `internal`, or `protected internal` (private, protected, or private protected members are rejected because generated registration code runs in a public static context)
  - property without setter or with private setter
  - readonly field
  - method that doesn't return `void` (or non-generic `Task` when `AsyncMethodInject` is enabled)
  - generic method
  - method that is not an ordinary method (i.e., constructors, operators, and other special methods are rejected)
- This validation reuses the same logic as SGIOC007 but specifically for members specified via `InjectMembers`.

---

### SGIOC025 - Error - Design - Circular module import detected

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

### SGIOC026 - Error - Usage - Invalid feature combination

Report when `SourceGenIocFeatures` enables `AsyncMethodInject` without also enabling `MethodInject`.

**Analysis:**

- Reads and parses `SourceGenIocFeatures` during `CompilationStart`.
- If `AsyncMethodInject` is enabled and `MethodInject` is disabled, the analyzer MUST report `SGIOC026`.
- The diagnostic SHOULD be reported once per compilation because the problem is project-wide rather than member-specific.

**Message format:** `'AsyncMethodInject' feature requires 'MethodInject' to be enabled.`

---

### SGIOC027 - Error - Design - Partial accessor must return `Task<T>` for async-init service

Report when a partial accessor returns the direct synchronous service type with no wrapper even though the matched implementation has async inject methods.

**Analysis:**

- Applies to partial methods and partial properties declared in `[IocContainer]` types.
- Applies regardless of the `IntegrateServiceProvider` setting because returning the wrong type for an async-init service is a semantic error, not a fallback-resolution issue.
- Matches the accessor's service type and key against container registrations.
- If the matched implementation contains async inject methods and the accessor returns the direct synchronous service type `TService` with no wrapper instead of `Task<TService>`, the analyzer MUST report `SGIOC027`.
- `SGIOC029` owns all non-`Task<T>` return types for async-init services, including wrappers, arrays, and unsupported async shapes; `SGIOC027` is only for the direct synchronous `TService` case.

**Message format:** `Partial accessor '{MemberName}' returns '{ServiceType}' but the implementation has async inject methods. Use 'Task<{ServiceType}>'.`

---

### SGIOC028 - Warning - Usage - `async void` injection method cannot be awaited

Report when an `[IocInject]`/`[Inject]` method is declared as `async void`.

**Analysis:**

- Checks methods marked with `[IocInject]` or `[Inject]`.
- If a method is declared `async` and returns `void`, the analyzer MUST report `SGIOC028` because the generator cannot await it.
- `SGIOC028` SHOULD be the user-facing diagnostic for this case; `SGIOC007` SHOULD NOT add a duplicate return-type diagnostic for the same method.

**Message format:** `[IocInject] method '{MethodName}' is 'async void' which cannot be awaited. Change return type to 'Task'.`

---

### SGIOC029 - Error - Design - Unsupported async partial accessor type

Report when a partial accessor targets an async-init service but returns any non-`Task<TService>` shape, including wrappers, arrays, or unsupported async shapes.

**Analysis:**

- Applies to partial methods and partial properties declared in `[IocContainer]` types.
- Applies regardless of the `IntegrateServiceProvider` setting because returning any non-`Task<TService>` shape for an async-init service is a semantic error, not a resolution fallback issue.
- Checks accessors that target registrations whose implementation contains async inject methods.
- For generic wrappers and arrays, recursively unwraps wrapper element types to find the innermost service type. This diagnostic-only analysis intentionally ignores Generator downgrade rules so that shapes such as `Task<Lazy<T>>`, `Lazy<Task<T>>`, nested wrappers, and arrays are still classified by their innermost service type.
- If the innermost unwrapped service type is an async-init service and the declared return type is not exactly `Task<TService>`, the analyzer MUST report `SGIOC029`.
- The only supported async accessor return shape is `Task<TService>`.
- `Task<TService>` is supported and does not produce a diagnostic.
- `SGIOC027` owns only the direct synchronous `TService` return case with no wrapper.
- `SGIOC029` owns all remaining non-`Task<TService>` return types for async-init services, including `ValueTask<TService>`, wrapper types, collection wrappers, arrays, nested wrappers, and downgraded async-shaped returns.

**Message format:** `Partial accessor '{MemberName}' returns '{ReturnType}' which is not a supported async type. Only 'Task<T>' is supported.`

---

### SGIOC030 - Error - Usage - Synchronous dependency requested for async-init service

Report when a consumer requests `TService` but the matched registration can only be resolved asynchronously.

**Analysis:**

- Applies to constructor parameters, injected properties, injected fields, and parameters of `[IocInject]`/`[Inject]` methods.
- Matches the requested service type and key against available registrations.
- If the matched service has async inject methods and there is no synchronous registration for the same service type/key, the analyzer MUST report `SGIOC030`.
- Consumers SHOULD request `Task<TService>` instead of `TService` in this scenario.
- Partial accessors are handled separately by `SGIOC027` and `SGIOC029`.

**Message format:** `'{MemberName}' requires '{ServiceType}' but this service has async inject methods and no synchronous registration exists. Use 'Task<{ServiceType}>'.`

---

### Known Limitations

None.
