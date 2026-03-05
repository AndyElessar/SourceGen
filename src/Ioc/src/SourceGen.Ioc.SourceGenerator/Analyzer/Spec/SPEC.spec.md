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
- method that does not return void
- method that is generic (has type parameters)
- method that is not an ordinary method (e.g., constructor, operator)

**Analysis:**

- Checks members (properties, fields, methods) marked with `[IocInject]` or `[Inject]`.
- Reports when:
  - Member is static.
  - Member is not `public`, `internal`, or `protected internal` (private, protected, or private protected members are rejected because generated code runs in a public static context).
  - Property has no setter or setter is private.
  - Field is readonly.
  - Method does not return void.
  - Method is generic (has type parameters).
  - Method is not an ordinary method (i.e., constructors, operators, and other special methods are rejected).

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
- Checks if the return type of each non-nullable partial accessor is a registered service type.
- Reports when a non-nullable partial accessor's return type is not found among registered services.

**Rationale:**

When `IntegrateServiceProvider = false`, there is no fallback to an external `IServiceProvider`. If a partial accessor references a service type that is not registered, it cannot be resolved at runtime. Nullable accessors are exempt because they can safely return `null`.

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
  - `IMethodSymbol` requires `MethodInject`
- Reports when the required feature flag is not enabled.

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
  - method that doesn't return void or is generic
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

**Message format:** `Container '{ContainerType}' has a circular module import dependency: {CyclePath}`
