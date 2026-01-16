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
- Uses depth-first search to traverse the dependency graph.
- Reports when starting from a service and following the dependency path leads back to the same service.

---

### SGIOC003 - Error - Design - Service Lifetime Conflict Detected

Report when there are singleton service depending on scoped service.

**Analysis:**

- Collects all registered services with their lifetimes (Singleton/Scoped/Transient).
- Analyzes each service's constructor parameters and queries the dependency's lifetime.
- Reports when a Singleton service depends on a Scoped service.

---

### SGIOC004 - Error - Design - Dangerous Service Lifetime Dependency Detected

Report when there are singleton service depending on transient service.

**Analysis:**

- Same as SGIOC003, analyzes lifetime relationships between services.
- Reports when a Singleton service depends on a Transient service.

---

### SGIOC005 - Error - Design - Dangerous Service Lifetime Dependency Detected

Report when there are scoped service depending on transient service.

**Analysis:**

- Same as SGIOC003, analyzes lifetime relationships between services.
- Reports when a Scoped service depends on a Transient service.

---

### SGIOC006 - Warning - Usage - Duplicated Attribute Usage

Report when `FromKeyedServicesAttribute` and `IocInjectAttribute`/`InjectAttribute` mark on one parameter. `FromKeyedServicesAttribute` takes precedence.

**Analysis:**

- Checks attributes on method parameters.
- Reports when a parameter has both `[FromKeyedServices]` and `[IocInject]`/`[Inject]`.

---

### SGIOC007 - Error - Usage - Invalid Attribute Usage

Report when `InjectAttribute` is mark on static member, or member can not assign/invoke (private setter, setter not exists, private field, readonly field, private method), or mark on method and it is not return void.

**Analysis:**

- Checks members (properties, fields, methods) marked with `[IocInject]` or `[Inject]`.
- Reports when:
  - Member is static.
  - Property has no setter or setter is private.
  - Field is readonly or private.
  - Method is private or does not return void.

---

### SGIOC008 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Factory` or `Instance` and use nameof(), but field/property/method in nameof() is not static or is inaccessible.

**Analysis:**

- Checks the `Factory` or `Instance` parameter of `[IoCRegister]` or `[IoCRegisterFor]` attributes.
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

### SGIOC014 - Error - Usage - Key does not exists

Report when `ServiceKeyAttribute` is mark on parameter, but there is no specified `Key` from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Checks whether the service registration has a `Key` specified.
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`.
- Reports when `[ServiceKey]` is used but the registration has no Key specified.

---

### SGIOC015 - Error - Design - Unresolvable Member

Report when:

- Constructor contains a built-in type parameter (or collection of built-in types) that cannot be resolved by the service provider.
- Property or Field with `[IocInject]` or `[Inject]` attribute has a built-in type (or collection of built-in types) that cannot be resolved by the service provider.
- Method with `[IocInject]` or `[Inject]` attribute has a parameter of built-in type (or collection of built-in types) that cannot be resolved by the service provider.

Built-in types include: bool, char, byte, sbyte, int, uint, long, ulong, float, double, decimal, string, DateTime, Guid, TimeSpan, DateTimeOffset, DateOnly, TimeOnly, Uri, Type, Version, Half, Int128, UInt128.

**Analysis:**

- Collects `HasFactory` and `HasInstance` state from service registrations.
- Checks constructor parameters, `[Inject]` properties/fields, and `[Inject]` method parameters for their types.
- Reports when the type is a built-in type (or collection of built-in types) and cannot be resolved.

**Exceptions (when NOT reported):**

- Constructor parameter has: `[IocInject]` with key, `[ServiceKey]`, `[FromKeyedServices]`, or default value.
- Property/field has: `[IocInject]`/`[Inject]` with service key specified.
- Method parameter has: `[IocInject]` with key, `[FromKeyedServices]`, or default value.
- Service registration uses `Factory` or `Instance` (constructor analysis is skipped).
