# Async-init Diagnostics

Diagnostics for async-init services: the AsyncMethodInject feature combination, partial accessors and synchronous dependencies. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC026 - Error - Usage - Invalid feature combination

Report when `SourceGenIocFeatures` enables `AsyncMethodInject` without also enabling `MethodInject`.

**Analysis:**

- Reads and parses `SourceGenIocFeatures` during `CompilationStart`.
- If `AsyncMethodInject` is enabled and `MethodInject` is disabled, the analyzer MUST report `SGIOC026`.
- The diagnostic SHOULD be reported once per compilation because the problem is project-wide rather than member-specific.

**Message format:** `'AsyncMethodInject' feature requires 'MethodInject' to be enabled.`

---

## Async-init Registrations

SGIOC027, SGIOC029 and SGIOC030 treat a registered (service type, key) pair as async-init when:

- its implementation type has `[IocInject]` methods returning non-generic `Task`, or
- a decorator applied to the service type has such methods. The decorators are the registration's own `Decorators`, otherwise those of its best `[IocRegisterDefaults]` (like the generator); `Instance` registrations are not decorated. A decorator applies to the service types the generator decorates with it, matched with the generator's own rules (`TryCloseDecorator`: implemented service type, closed generic constraints), so a decorator never makes the implementation type itself, or a service type it cannot decorate, async-init.

Both require `AsyncMethodInject`.

An `Instance` registration is never async-init, whatever methods its implementation type has: like the generator, which registers the instance as is without calling its inject methods (see [Classification Rules](../../Spec/Register.Injection.spec.md#classification-rules)), the analyzer MUST NOT report SGIOC027, SGIOC029 or SGIOC030 for it. Otherwise it would report an error for an accessor or dependency that the generated code resolves synchronously.

---

## SGIOC027 - Error - Design - Partial accessor must return `Task<T>` for async-init service

Report when a partial accessor returns the direct synchronous service type with no wrapper even though the matched registration is async-init (see [Async-init Registrations](#async-init-registrations)).

**Analysis:**

- Applies to partial methods and partial properties declared in `[IocContainer]` types.
- Applies regardless of the `IntegrateServiceProvider` setting because returning the wrong type for an async-init service is a semantic error, not a fallback-resolution issue.
- Matches the accessor's service type and key against container registrations.
- If the matched registration is async-init (see [Async-init Registrations](#async-init-registrations)) and the accessor returns the direct synchronous service type `TService` with no wrapper instead of `Task<TService>`, the analyzer MUST report `SGIOC027`.
- `SGIOC029` owns all non-`Task<T>` return types for async-init services, including wrappers, arrays, and unsupported async shapes; `SGIOC027` is only for the direct synchronous `TService` case.

**Message format:** `Partial accessor '{MemberName}' returns '{ServiceType}' but the service is async-init (its implementation or a decorator has async inject methods). Use 'Task<{ServiceType}>'.`

---

## SGIOC029 - Error - Design - Unsupported async partial accessor type

Report when a partial accessor targets an async-init service but returns any non-`Task<TService>` shape, including wrappers, arrays, or unsupported async shapes.

**Analysis:**

- Applies to partial methods and partial properties declared in `[IocContainer]` types.
- Applies regardless of the `IntegrateServiceProvider` setting because returning any non-`Task<TService>` shape for an async-init service is a semantic error, not a resolution fallback issue.
- Checks accessors that target async-init registrations (see [Async-init Registrations](#async-init-registrations)).
- For generic wrappers and arrays, recursively unwraps wrapper element types to find the innermost service type. This diagnostic-only analysis intentionally ignores Generator downgrade rules so that shapes such as `Task<Lazy<T>>`, `Lazy<Task<T>>`, nested wrappers, and arrays are still classified by their innermost service type.
- If the innermost unwrapped service type is an async-init service and the declared return type is not exactly `Task<TService>`, the analyzer MUST report `SGIOC029`.
- The only supported async accessor return shape is `Task<TService>`.
- `Task<TService>` is supported and does not produce a diagnostic.
- `SGIOC027` owns only the direct synchronous `TService` return case with no wrapper.
- `SGIOC029` owns all remaining non-`Task<TService>` return types for async-init services, including `ValueTask<TService>`, wrapper types, collection wrappers, arrays, nested wrappers, and downgraded async-shaped returns.

**Message format:** `Partial accessor '{MemberName}' returns '{ReturnType}' which is not a supported async type. Only 'Task<T>' is supported.`

---

## SGIOC030 - Error - Usage - Synchronous dependency requested for async-init service

Report when a consumer requests `TService` but the matched registration can only be resolved asynchronously.

**Analysis:**

- Applies to constructor parameters, injected properties, injected fields, and parameters of `[IocInject]`/`[Inject]` methods, of registered services and of decorators. Decorators are the ones the generator applies to registrations (their own `Decorators`, otherwise those of the best `[IocRegisterDefaults]`), each analyzed once (an open generic decorator once per closed form, closed with the type arguments found by matching the decorated service type, or through its definition when they cannot all be found); the constructor parameter that receives the decorated instance (see [Decorated Parameter](../../Spec/Register.Decorators.spec.md#decorated-parameter)) is not analyzed. That parameter depends on the decorated service type, so a decorator of several service types is analyzed once per such parameter, and a dependency reported for several of them is reported once.
- Matches the requested service type and key against available registrations.
- If the matched registration is async-init (see [Async-init Registrations](#async-init-registrations)) and there is no synchronous registration for the same service type/key, the analyzer MUST report `SGIOC030`. Service types are compared without tuple element names, which are not part of the runtime type.
- `Lazy<T>`, `Func<T>` and `Func<T1, ..., T>` (also nested, e.g. `Lazy<Func<T>>`) are unwrapped first: they create `T` synchronously, so they cannot resolve an async-init service either (the Register output does not register a `Func` with input parameters for it, and the Container resolves these wrappers from the fallback provider). A `Task<T>` inside them (`Lazy<Task<T>>`) is not reported. Collections are not unwrapped, since they exclude async-init registrations.
- A registration's service types are resolved like the generator's `ProcessSingleRegistration`, including `[IocRegisterDefaults]` (assembly-level, type-level and of imported modules, see [Default Settings](Analyzer.Register.spec.md#default-settings)): each base class (first) and interface of the implementation that has default settings, the `ServiceTypes` of the best (first) match, and its `RegisterAllInterfaces` / `RegisterAllBaseClasses` when the registration does not set them.
- Consumers SHOULD request `Task<TService>` instead of `TService` in this scenario.
- Partial accessors are handled separately by `SGIOC027` and `SGIOC029`.

**Message format:** `'{MemberName}' requires '{ServiceType}' but this service is async-init (its implementation or a decorator has async inject methods) and no synchronous registration exists. Use 'Task<{ServiceType}>'.`
