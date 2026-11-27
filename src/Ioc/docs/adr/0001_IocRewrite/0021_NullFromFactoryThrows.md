---
status: accepted
date: 2026-11-28
---

# A factory that returns null throws

## Context and problem statement

A factory method can return `null`, and a static member of an instance registration can be `null`. A service type is a reference type, so the C# compiler does not stop either case.

MS.DI accepts `null` from a factory delegate and gives it to each caller (see [Evidence](#evidence)):

- `GetService` returns `null`, and `GetRequiredService` throws with a message that says that the service is not registered.
- A required constructor parameter receives `null` without an error.
- `GetServices` returns a collection with a `null` element.
- A Singleton factory that returns `null` runs again on some later resolutions (a constructor injection, `GetServices`), so the Singleton is created more than one time.

The container uses `null` in a field to mean "not created yet". If it accepted `null`, each resolution would call the factory again, as MS.DI does in some paths. The constitution requires that each difference from MS.DI is recorded.

## Decision outcome

`null` is not a valid instance of a service. A factory that returns `null` throws `InvalidOperationException` when it is created, in both outputs. The message names the factory. A dependency site, a wrapper type, and a collection never receive `null` from a factory.

The container applies the same rule to an instance registration: when the static member is `null`, a resolution throws `InvalidOperationException`. The service collection extension gives the static object to MS.DI directly, and MS.DI already throws `ArgumentNullException` when the registration is added.

This is a documented difference of the [constitution](../../Constitution.md#8-documented-differences-from-msdi) for both outputs.

### Consequences

- Good: the error occurs where the factory returns `null`. It does not move to a later `NullReferenceException` in the code that uses the service.
- Good: a Singleton or Scoped factory runs one time for each instance, as for an implementation type. The fast path stays one field read and one null check.
- Good: no rule is necessary for `null` inside `Lazy<T>`, `Func<T>`, a collection, or a dictionary.
- Bad: code that relies on a factory that returns `null`, for example an optional feature that is turned off, must change. It can register no service and use an optional dependency, or return a null object.

## Rejected options

- **Return `null`, as MS.DI does.** A required dependency then receives `null`, which is against explicitness and correctness. The container needs one more flag for each Singleton and Scoped definition, so that it does not call the factory again. Each wrapper type and collection needs a rule for `null`.
- **Return `null` from `GetService`, but throw at a dependency site.** The two ways of resolution then disagree, and the same instance definition is valid for one caller and invalid for another.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15, .NET SDK 11.0.100, net10.0, in the console project `temp/verify-msdi-null-factory/` (not part of the repository). The registration is `AddSingleton<IClock>(_ => null!)` with `ValidateScopes` and `ValidateOnBuild`.

| Case | Result |
| --- | --- |
| `GetService<IClock>()`, two times | `null` both times. The factory ran one time. |
| `GetRequiredService<IClock>()` | `InvalidOperationException`: "No service for type 'IClock' has been registered." |
| `Consumer(IClock clock)` resolved as Transient | Created, and `clock` is `null`. |
| `OptionalConsumer(IClock? clock = null)` | Created, and `clock` is `null`. |
| `GetServices<IClock>()` | One element, and it is `null`. |
| The number of factory calls after all cases above | 4. The Singleton factory ran again for the constructor injections and for `GetServices`. |
| `AddSingleton<IClock>((IClock)null!)` | `ArgumentNullException` (parameter `implementationInstance`) when the registration is added. |

The NativeAOT tests of SourceGen.Ioc (`FactoryTests.GetService_FactoryReturnsNull_Throws` and `ServiceCollectionExtensionTests.AddTestAot_FactoryReturnsNull_Throws`, net10.0 and net11.0) show that both outputs throw `InvalidOperationException` with the name of the factory.

Not tested: the instance registration in the generated container, because it is generated in a later slice.
