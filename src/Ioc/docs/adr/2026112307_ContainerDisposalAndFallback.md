---
status: accepted
date: 2026-11-23
---

# Container disposal and fallback

## Context and problem statement

The generated container disposes the singleton and scoped instances that it creates. It can also have a fallback provider: an `IServiceProvider`, usually from MS.DI, that answers requests for which the container has no registration.

These decisions define the disposal order, what happens when disposal throws, who disposes the fallback side, and what the container answers for a type that it does not know.

## Decision outcome

1. **The generator decides the disposal order at compile time.** The container does not record the creation order at run time.
   1. A scope disposes its scoped instances, then its fallback scope.
   2. The root disposes its singleton instances, then a fallback provider that the container infrastructure created.
   3. Inside one lifetime, the order is the reverse topological order of the dependency graph. A service is disposed before the services that it depends on. Deferred edges (`Lazy`, `Func`) do not count. Unrelated services use the reverse of the ordering rules.
   4. Inside one closed definition, each decorator chain goes from the outermost layer to the innermost layer, and the implementation instance is last.
2. **Disposal continues after an exception.** When an instance throws during disposal, the container disposes the others. At the end it throws the one exception as it is, or an `AggregateException` for several.
3. **The dispose-exception callback receives only exceptions that cannot be thrown.** `IocContainerGlobalOptions.OnDisposeException` is `null` by default, and those exceptions are then dropped. The callback is never a replacement for a thrown exception.
4. **The container disposes what it creates and never what the user gives.**

   | Object | Created by | Disposed by |
   | --- | --- | --- |
   | Fallback provider passed to the constructor | User | User |
   | Fallback provider created by `ServiceProviderFactory.CreateServiceProvider` | Container infrastructure | The root that the call returns, as its last step |
   | Fallback scope | A container scope, on first need | That container scope, as its last step |

5. **A scope resolves from a fallback scope, not from the fallback root.** The scope creates its fallback scope the first time that it needs the fallback provider.
6. **A value-type dependency without a registration goes to the fallback provider.** The rules are those of a reference type, with one more unbox step. The generated code calls the non-generic `GetService(Type)`.
7. **A request by `Type` for an unknown `IEnumerable<T>` returns `null` when there is no fallback provider.** "Unknown" means that `T` has no registration and no demand at compile time. `sp.GetServices<T>()` therefore throws. This is a documented difference.

## Rejected options

- **Record the creation order at run time**, as MS.DI does. Each created instance would need an entry in a list that is safe for concurrent use. For services with a dependency relation the compile-time order gives the same result, because a dependency is always created first. Only unrelated services can differ.
- **Stop at the first exception**, as MS.DI does. The services after the failing one are then never disposed.
- **Send disposal exceptions to the callback and do not throw.** With the default `null` callback, a failure would be silent.
- **`OnDisposeException` throws by default.** The callback runs in places that cannot throw: a continuation after a synchronous `Dispose`, or a thread that already throws another exception. An exception there is unobserved or hides the original one.
- **Never dispose a fallback provider, or always dispose it.** A host disposes only the container that `ServiceProviderFactory` returns, so a provider created there would leak. A provider that the user passes in can be shared with other code, so the container must not dispose it.
- **Resolve from the fallback root in a scope.** A disposable transient service of the fallback provider would then stay in the fallback root until the process ends.
- **Return an empty array for an unknown `IEnumerable<T>`.** `Array.CreateInstance` makes this possible for reference types under NativeAOT. It was rejected to keep one rule: the container answers only for types that it knows at compile time. With a fallback provider, the whole request goes to it.
- **Return an `object[]` reinterpreted with `Unsafe.As`.** The array would have the wrong run-time type.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10.0.15, JIT and NativeAOT.

- MS.DI disposes the objects that it tracks in the reverse order of their creation.
- MS.DI tracks only objects that a factory delegate or its own activation returns. It does not dispose an object that was registered as an instance.
- `Array.CreateInstance(elementType, 0)` works under NativeAOT for an element type that is a reference type.
- Under NativeAOT, MS.DI refuses to build an `IEnumerable<T>` whose element is a value type.
- For a value type `T` without a registration, `GetService(typeof(T))` returns `null` and the generic `GetService<T>()` throws `NullReferenceException`. See [Registration rules](2026112305_RegistrationRules.md).

Not tested: a fallback provider that is not MS.DI. The concurrent creation and disposal of a fallback scope has no probe yet.
