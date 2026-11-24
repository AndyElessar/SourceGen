---
status: accepted
date: 2026-11-25
---

# The container gets the interfaces of the fallback provider as services

## Context and problem statement

A container with a fallback provider asks the fallback provider when it does not have a service itself (see [Fallback provider](../../spec/Container.Output.spec.md#fallback-provider)). Some requests need an interface of the fallback provider:

- `IsService` and `IsKeyedService` need `IServiceProviderIsService` and `IServiceProviderIsKeyedService`.
- A keyed request needs `IKeyedServiceProvider`.
- A scope needs `IServiceScopeFactory` to create its fallback scope.

The specification said that the container asks the fallback provider when the fallback provider "implements the related interface". A test showed that the provider of MS.DI does not implement `IServiceProviderIsService` or `IServiceProviderIsKeyedService`. MS.DI supplies them as services. With a type test, `IsService` of the container would never ask MS.DI, and a service of MS.DI would count as not registered.

## Decision outcome

- `IsService` and `IsKeyedService` get the interface from the fallback provider as a service: `GetService(typeof(IServiceProviderIsService))` and `GetService(typeof(IServiceProviderIsKeyedService))`. When the result is `null`, the container answers `false`.
- A keyed request uses a type test for `IKeyedServiceProvider`. The provider and the scope of MS.DI implement it. The `GetKeyedService` extension method of MS.DI uses the same type test.
- A scope gets `IServiceScopeFactory` as a service, as the specification already said ("supplies").

### Consequences

- Good: `IsService` of the container gives the same answer as MS.DI for a service that only MS.DI has.
- Bad: each call of `IsService` for a type that the container does not have resolves `IServiceProviderIsService` from the fallback provider. `IsService` is not on a resolution path, so the performance guarantee does not cover it.

## Rejected options

- **A type test for each interface.** It is simple, but it does not work with MS.DI, which is the main fallback provider.
- **Get the interfaces one time in the constructor of the root.** It saves the lookups, but the root then uses the fallback provider before the user resolves anything. The lookups are cheap, and `IsService` is rare.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on net10.0 (.NET SDK 11.0.100):

| Object | `IKeyedServiceProvider` | `IServiceProviderIsService` | `IServiceProviderIsKeyedService` | `ISupportRequiredService` | `IAsyncDisposable` |
| --- | --- | --- | --- | --- | --- |
| `ServiceProvider` (the root) | Implements | Does not implement | Does not implement | Does not implement | Implements |
| `ServiceProviderEngineScope` (`scope.ServiceProvider`) | Implements | Does not implement | Does not implement | Does not implement | Implements |

- `GetService(typeof(IServiceProviderIsService))` returns an object that implements both `IServiceProviderIsService` and `IServiceProviderIsKeyedService`.
- `GetService(typeof(IServiceScopeFactory))` returns an object.
- For a type without a registration, `IsService(typeof(IEnumerable<T>))` is `true`, and `GetService(typeof(IEnumerable<T>))` returns an empty collection.

The NativeAOT tests of the container (`TestAot/Container/FallbackTests.cs`) check `IsService` and `IsKeyedService` with MS.DI as the fallback provider.
