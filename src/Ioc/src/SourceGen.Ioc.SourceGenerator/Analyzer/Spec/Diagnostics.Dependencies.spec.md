# Dependency Diagnostics

Diagnostics for the dependency graph of registered services: circular dependencies and lifetime conflicts. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## Analyzed Registrations

The container constructs a registration only when it has neither `Instance` nor `Factory`. SGIOC002-005 and SGIOC033 therefore analyze the selected constructor of these constructor-activated registrations only:

| Registration | Constructor parameters | `[IocInject]`/`[Inject]` members |
| --- | --- | --- |
| Constructor-activated | Analyzed | Analyzed (SGIOC033) |
| `Factory` | Not analyzed: the factory method creates the instance | Analyzed (SGIOC033): the generator calls the inject methods of an async-init factory result |
| `Instance` | Not analyzed: the instance already exists | Not analyzed: the instance is not injected |

An implementation type can have several registrations (for example with different keys or tags). Each registration is analyzed with its own lifetime, location, key and activation, not those of the first registration of the type:

| Diagnostic | Registrations analyzed | Reported at |
| --- | --- | --- |
| SGIOC002 | The constructor, when any registration is constructor-activated | The first constructor-activated registration, once per cycle edge of the type |
| SGIOC003-005 | Each constructor-activated registration, with its own lifetime | That registration; a conflict is reported once per location |
| SGIOC033 | The constructor when any registration is constructor-activated; the members when any registration has no `Instance` | The first registration that calls the constructor / injects the members (the diagnostic does not depend on the consumer's lifetime) |
| SGIOC013/014 | Each registration that calls the method (see [Key Diagnostics](Diagnostics.Keys.spec.md)) | The `[ServiceKey]` parameter, once per problem |

```csharp
[assembly: IocRegisterFor<Foo>(ServiceLifetime.Transient, Key = "a")]

public interface IScopedDep;

[IocRegister<IScopedDep>(Lifetime = ServiceLifetime.Scoped)]
public class ScopedDep : IScopedDep;

// SGIOC003 at this registration: it captures IScopedDep, although the first registration of Foo is Transient
[IocRegister(Lifetime = ServiceLifetime.Singleton)]
public class Foo(IScopedDep dep);
```

As a dependency, an implementation type still has the lifetime of its first registration: the lookup of a dependency (directly, through the service type index, and through the open generic definition) does not select the registration the container resolves for the requested service type and key.

An `Instance` or `Factory` registration is still a dependency of the services that depend on it. For example, `List<T>` registered with `Instance` and the service type `IEnumerable<T>` is not a circular dependency, although its `List(IEnumerable<T>)` constructor resolves to the registration itself:

```csharp
public static class Collections
{
    public static readonly List<Lazy<IHandler>> Lazies = [];
}

// No SGIOC002: the List(IEnumerable<Lazy<IHandler>>) constructor is never called
[IocRegisterFor<List<Lazy<IHandler>>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Lazy<IHandler>>)], Instance = "Collections.Lazies")]
[IocContainer(ExplicitOnly = true)]
public sealed partial class Container;
```

A `Factory` inherited from default settings (`[IocRegisterDefaults]`) is not considered: such a registration's constructor is still analyzed.

## SGIOC002 - Error - Design - Circular Dependency Detected

Report when circular dependencies are detected among registered services.

**Analysis:**

- Collects all registered services and the constructor parameter dependencies of the constructor-activated ones (see [Analyzed Registrations](#analyzed-registrations)).
- Builds a service type index (interface/base class → implementation type) for fast dependency lookup.
- Unwraps `Func<T>`, `Func<T1, ..., TReturn>`, and `Lazy<T>` wrapper types to extract the inner service type for dependency resolution.
- Uses depth-first search to traverse the dependency graph.
- Reports when starting from a service and following the dependency path leads back to the same service.

---

## SGIOC003 - Error - Design - Service Lifetime Conflict Detected

Report when there are singleton service depending on scoped service.

**Analysis:**

- Collects all registered services with their lifetimes (Singleton/Scoped/Transient).
- Analyzes the constructor parameters of each constructor-activated registration, with that registration's own lifetime (see [Analyzed Registrations](#analyzed-registrations)), and queries the dependency's lifetime.
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

- Checks constructor parameters, and the types of `[IocInject]`/`[Inject]` properties, fields and method parameters, as listed in [Analyzed Registrations](#analyzed-registrations).
- Looks up `TReturn` like the lifetime checks (SGIOC003-005): directly, through the service type index, and through the open generic definition. The service key of `[FromKeyedServices]` is not considered.
- Does not report the parameterless `Func<T>` or `Lazy<T>`, which follow the lifetime, or a `TReturn` that is not registered.
- Reports one diagnostic per dependency at the consumer's first registration that calls the constructor or injects the member (see [Analyzed Registrations](#analyzed-registrations)).

**Message format:** `'{Consumer}' depends on '{FuncType}', which creates a new '{ServiceType}' on every call although '{ServiceType}' is registered as {Lifetime}`

**Fix:** Register the service as Transient, or depend on the parameterless `Func<T>` or the service itself to follow its lifetime.
