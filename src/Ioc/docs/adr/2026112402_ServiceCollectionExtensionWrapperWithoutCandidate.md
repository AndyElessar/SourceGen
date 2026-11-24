---
status: accepted
date: 2026-11-24
---

# Wrapper of a service that the service collection extension does not register

## Context and problem statement

The service collection extension registers `Lazy<S>`, `Func<S>`, and the related wrapper types once for each candidate of `S` in the assembly view (see [Service collection extension output](../spec/ServiceCollection.Output.spec.md#wrapper-types-and-collections)). A dependency site can ask for a wrapper of a service that has no candidate there, for example `Func<ILogger<T>>` or `Lazy<IOptions<X>>`. These services come from the framework or from registrations of the user outside the method. Then no wrapper is registered, and MS.DI fails at run time when it creates the service that has the dependency site.

The container behaves differently. It creates the wrapper itself and resolves `S` from the fallback provider inside the wrapper (see [Container output](../spec/Container.Output.spec.md#behavior-for-each-request)). Section 1 of the [constitution](../Constitution.md) requires that the two outputs behave the same where possible.

"No candidate" is not always known at compile time. A candidate with tags exists only when the tags given to the service collection extension at run time include one of its tags.

## Decision outcome

The service collection extension does not register a wrapper for a service without a candidate. The dependency site builds the wrapper itself.

When the core service `S` of a dependency site has no candidate without tags in the assembly view, the generated argument is `sp.GetService<W>() ?? {new wrapper}`:

- When a registration of the wrapper `W` exists, it is used. This covers the candidates that the tags select at run time, and the wrappers that the user registers.
- Otherwise the dependency site creates the wrapper. The wrapper resolves `S` from `sp` when it is called.

### Consequences

- Good: a dependency site gets a wrapper of a framework service, the same as in the container.
- Good: no registration is added, so a collection of wrappers does not get an extra element, and no registration of the user is hidden.
- Good: a missing `S` fails when the wrapper is called, the same as in the container with a fallback provider.
- Bad: a resolve call in code, for example `sp.GetService<Func<ILogger>>()`, is not a dependency site. It still gets `null`, while the container returns a wrapper for a resolve call that it sees at compile time. This is documented difference 25 of the constitution. To remove it, the method would have to register the wrapper, which the first two rejected options below describe.

## Rejected options

- **Register wrappers only for candidates.** MS.DI fails at run time for a wrapper of a framework service.
- **Register a wrapper that resolves `S` from `sp` when `S` has no candidate.** MS.DI collects every registration of `W` into `IEnumerable<W>`. A collection of wrappers then has one more element than in the container, which gives an empty collection. The registration also needs a tag condition that is decided at run time.
- **Register the wrapper and also register the complete collection `IEnumerable<W>` as one service.** An explicit registration of `IEnumerable<W>` replaces the collection that MS.DI builds. Each registration of `W` that the user adds outside the method then disappears from the collection.
- **Register the wrapper and record the extra element as a documented difference.** It changes the constitution for a problem that the dependency site can avoid.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on .NET 10, JIT.

- An explicit registration of `IEnumerable<Lazy<IFoo>>` replaces the collection. With two registrations of `Lazy<IFoo>` and an empty array registered as `IEnumerable<Lazy<IFoo>>`, `GetServices<Lazy<IFoo>>()` is empty.
- Without a registration of `Lazy<IFoo>`, `GetService<Lazy<IFoo>>()` returns `null`. A wrapper that the dependency site creates resolves an `IFoo` that the user registers after the `Add` method. `GetServices<Lazy<IFoo>>()` stays empty.
- When the user registers `Lazy<IFoo>`, `GetService<Lazy<IFoo>>()` returns it, and the dependency site uses it.
- When `IFoo` has no registration, the service with the dependency site is created, and `Lazy<IFoo>.Value` throws `InvalidOperationException`.
- The keyed form (`GetKeyedService<Lazy<IFoo>>(key)` and `GetRequiredKeyedService<IFoo>(key)`) behaves the same way.

Not tested: NativeAOT, and MS.DI 11.0.0.
