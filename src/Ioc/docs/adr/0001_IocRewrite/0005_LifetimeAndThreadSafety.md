---
status: accepted
date: 2026-11-23
---

# Lifetime and thread safety

## Context and problem statement

These decisions define what the generated container allows between lifetimes, how it protects the first creation of an instance, and how the root and its scopes relate.

The previous design had five `ThreadSafeStrategy` values, reported "singleton depends on transient" and "scoped depends on transient" as errors, and had an option to look up services with a generated `switch`.

## Decision outcome

1. **A disposable transient service is an error in the container.** The container does not track transient instances. The rule also covers the `T` of `Func<..., T>`, a transient definition with a disposable decorator, and a transient factory whose return type is known to be disposable. The service collection extension does not report it, because MS.DI disposes the instance.
2. **A captive dependency is an error.** A singleton cannot depend on a scoped service, also through `Lazy<T>`, `Func<T>`, or a transient service.
3. **A singleton or scoped service that holds a transient service causes a Warning.** `SGIOC502` is for a singleton and `SGIOC503` for a scoped service. A dependency through `Func<T>` or `Func<..., T>` causes no warning, because the holder keeps only the factory.
4. **`ThreadSafeStrategy` has three values:** `None`, `Lock` (default), and `CompareExchange`. `SemaphoreSlim` and `SpinLock` are removed. `CompareExchange` has the value `1 << 1`. In the previous version, `SemaphoreSlim` was `1 << 1`, `SpinLock` was `1 << 2`, and `CompareExchange` was `1 << 3`. `KeyType` and `EagerResolveOptions` keep their previous values.
5. **Lock layout.** A singleton has one `System.Threading.Lock` for each closed definition, in the state object that the root and all scopes share. Scoped services share one lock for each container instance. The lock protects only the first creation. A read after creation takes no lock.
6. **The root and a scope are instances of the same class.** `CreateScope()` returns the container type, so partial accessors and strongly typed methods work on a scope. The root does not track its scopes.
7. **Nested scopes are not supported.** `CreateScope()` on a scope creates another scope of the root.
8. **The container uses lookup tables only.** It does not generate service lookup by `switch`, and there is no user option.

## Rejected options

- **Track and dispose transient services.** The .NET dependency injection guidance itself advises against a transient `IDisposable` registration. The registrations of the framework are in the fallback provider and are not affected.
- **Error for "holds a transient".** Many projects register stateless services as transient. Those are safe to hold, and the generator cannot tell them from the unsafe ones by type. An error cannot be disabled, so the user would need `Func<T>` or a different lifetime for no reason.
- **No check for "holds a transient".** A transient service held by a singleton is shared by all threads and lives until the process ends. That is a real risk when the service has state.
- **`SemaphoreSlim`.** It is not reentrant. Its advantage over `Lock` is that code can hold it across `await`. The container never awaits inside a lock, so only the cost remains.
- **`SpinLock`.** A waiting thread spins. That fits a critical section of some tens of nanoseconds. Here the critical section is a user constructor of unknown length. `SpinLock` is also not reentrant and is a mutable struct. Microsoft guidance says not to call unknown code or allocate memory while holding it.
- **One lock for each scoped service.** Each `CreateScope()` would need a lock for every scoped service, or lazy allocation of one more object for each created service. One lock for each scope matches MS.DI and costs one object.
- **One lock for the whole container, including singletons.** A slow singleton constructor would block the first creation of every other singleton.
- **Nested scopes.** Without a new lifetime that shares an instance with the parent scope, a nested scope behaves like a flat scope. The only difference would be that a parent disposes its children, which needs a thread-safe list of children. The injected `IServiceScopeFactory` is the root, so `CreateScope()` would mean two things. The scopes of an MS.DI fallback provider are flat.
- **Lookup by `switch`.** It is deferred until the core is complete and measurements exist.
- **Target-typed `new` for the entries of a lookup table.** The binding cost of the C# compiler must stay linear, so the entries use explicit types (see Evidence).

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10.0.15.

The findings about locks come from behavior. The MS.DI source code was not read.

- Scoped services use one lock for each scope:
  - While thread A runs a slow constructor of a scoped service, the first creation of a different scoped service in the same scope on thread B waits.
  - A repeated resolution of a scoped service that already exists also waits.
  - Resolution in another scope does not wait.
  - The result is the same for the runtime resolver and for compiled call sites.
- Resolution of a disposable transient service in a scope also takes the lock of that scope. A transient service that is not disposable does not.
- Singletons are locked for each service. The first creation of another singleton does not wait. A second request for the same singleton waits and gets the one instance.
- With the root provider used as a scope, scoped services are locked for each call site.
- MS.DI itself does not check whether a singleton or scoped service depends on a transient service. `ValidateScopes` reports only a singleton that depends on a scoped service.

A hand-written minimal container with a scoped field, double-checked `System.Threading.Lock`, and singletons in a shared state object was used for the tests in [Holders and asynchronous services](0008_HoldersAndAsyncServices.md).

- A measurement of the previous implementation: with explicit types in place of target-typed `new`, the binding time of the compiler for 3,803 lookup table entries went from 12.9 seconds to 1.45 seconds.

Not tested: MS.DI 11.0.0. No benchmark compares the lock layouts.
