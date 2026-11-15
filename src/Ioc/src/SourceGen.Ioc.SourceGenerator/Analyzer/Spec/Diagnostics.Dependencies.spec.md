# Dependency Diagnostics

Diagnostics for the dependency graph of registered services: circular dependencies and lifetime conflicts. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC002 - Error - Design - Circular Dependency Detected

Report when circular dependencies are detected among registered services.

**Analysis:**

- Collects all registered services and their constructor parameter dependencies.
- Builds a service type index (interface/base class → implementation type) for fast dependency lookup.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types to extract the inner service type for dependency resolution.
- Uses depth-first search to traverse the dependency graph.
- Reports when starting from a service and following the dependency path leads back to the same service.

---

## SGIOC003 - Error - Design - Service Lifetime Conflict Detected

Report when there are singleton service depending on scoped service.

**Analysis:**

- Collects all registered services with their lifetimes (Singleton/Scoped/Transient).
- Analyzes each service's constructor parameters and queries the dependency's lifetime.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types to detect captive dependencies through wrappers. A `Func` is reported too, because a Singleton resolves the Scoped service from the root scope.
- Reports when a Singleton service depends on a Scoped service.

---

## SGIOC004 - Error - Design - Dangerous Service Lifetime Dependency Detected

Report when there are singleton service depending on transient service.

**Analysis:**

- Same as SGIOC003, analyzes lifetime relationships between services.
- Unwraps `Lazy<T>`, which keeps the instance it creates. A dependency through `Func<T>` or `Func<T1, ..., TReturn>` is not reported: the factory resolves a new transient instance on every call, so none is captured.
- Reports when a Singleton service depends on a Transient service.

---

## SGIOC005 - Error - Design - Dangerous Service Lifetime Dependency Detected

Report when there are scoped service depending on transient service.

**Analysis:**

- Same as SGIOC003, analyzes lifetime relationships between services.
- Unwraps `Lazy<T>`, which keeps the instance it creates. A dependency through `Func<T>` or `Func<T1, ..., TReturn>` is not reported: the factory resolves a new transient instance on every call, so none is captured.
- Reports when a Scoped service depends on a Transient service.

---

## SGIOC033 - Warning - Design - Func with arguments ignores the service lifetime

Report when a registered service depends on `Func<T1, ..., TReturn>` with at least one input parameter and `TReturn` is registered as Singleton or Scoped. Each call creates a new instance from its arguments (see [Register Injection](../../Spec/Register.Injection.spec.md)), so the registered lifetime does not apply.

**Analysis:**

- Checks constructor parameters, and the types of `[IocInject]`/`[Inject]` properties, fields and method parameters.
- Looks up `TReturn` like the lifetime checks (SGIOC003-005): directly, through the service type index, and through the open generic definition. The service key of `[FromKeyedServices]` is not considered.
- Does not report the parameterless `Func<T>` or `Lazy<T>`, which follow the lifetime, or a `TReturn` that is not registered.
- Reports one diagnostic per dependency at the consumer's registration.

**Message format:** `'{Consumer}' depends on '{FuncType}', which creates a new '{ServiceType}' on every call although '{ServiceType}' is registered as {Lifetime}`

**Fix:** Register the service as Transient, or depend on the parameterless `Func<T>` or the service itself to follow its lifetime.
