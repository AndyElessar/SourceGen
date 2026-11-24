---
status: accepted
date: 2026-11-25
---

# A registration cannot use KeyedService.AnyKey as its key

## Context and problem statement

MS.DI uses `KeyedService.AnyKey` in two ways:

- **Query.** `GetKeyedServices<T>(KeyedService.AnyKey)` returns all registrations of `T` that have a key.
- **Registration.** `AddKeyedSingleton<T>(KeyedService.AnyKey, (sp, key) => ...)` registers a catch-all. A request for one service with a key that has no exact registration gets this registration, and the factory or the `[ServiceKey]` parameter receives the requested key. The keys do not have to be known in advance, for example the tenants of a multi-tenant application.

The generator supports the query (see [Lookup rules](../../spec/Container.Output.spec.md#lookup-rules)). A registration attribute with `AnyKey` reports `SGIOC007`. This ADR records why the generator does not support the catch-all registration.

## Decision outcome

A registration with `KeyedService.AnyKey` as its key is an error (`SGIOC007`) in both outputs. A dependency site can use `AnyKey` only to ask for a collection or a dictionary. The constitution lists this as a documented difference.

A user who needs a catch-all registers it in the `IServiceCollection` of the fallback provider. The container sends a keyed request that it does not have to the fallback provider. A dependency site with a key that the container does not have is resolved from the fallback provider too, with `SGIOC501`.

### Consequences

- Good: each keyed dependency site is checked at compile time. Without a fallback provider, a key with a spelling error reports `SGIOC402`. With a fallback provider, it reports `SGIOC501`.
- Good: an instance definition keeps one instance for each container or scope. The container needs no cache for each key.
- Bad: a catch-all needs a fallback provider. A container without one cannot have a catch-all.

## Rejected options

**Support the catch-all registration in the container.** These are the problems:

- **A cache for each key.** MS.DI creates one instance for each key that is requested (see Evidence). The number of keys has no limit. The container keeps each singleton or scoped instance in one field today. A catch-all needs a dictionary with a lock, its own disposal order, and a cache that can grow without a limit.
- **No compile-time check.** When a type has a catch-all, every key of that type resolves. A key with a spelling error no longer reports `SGIOC402` or `SGIOC501`. This goes against the first principle of the constitution.
- **Order with the fallback provider.** The container answers first and asks the fallback provider only when it has no candidate. A catch-all in the container would hide an exact registration of the same key in the fallback provider. MS.DI prefers the exact registration. To keep that rule, the container would ask the fallback provider before it uses its own catch-all, on each request.
- **The type of `[ServiceKey]`.** A key from a request at run time is any `object`. The generator cannot check the type of the parameter at compile time, and the cast can fail at run time.
- **Collections.** A catch-all is not in any collection of MS.DI (see Evidence). The container would need a second rule for one service and for collections.
- **Later outputs.** A module entry belongs to one instance definition with a known key. A decorator with `InheritKey` would get a key that is known only at run time.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on net10.0 (.NET SDK 11.0.100). The registrations were a catch-all singleton of `IThing` with `AnyKey`, two singletons with the key `"x"`, and one singleton without a key.

| Request | Result |
| --- | --- |
| `GetRequiredKeyedService<IThing>("x")` | The last exact registration of `"x"`. |
| `GetRequiredKeyedService<IThing>("y")` | The catch-all. Its factory receives `"y"`. |
| The same request for `"y"` two times | The same instance. |
| A request for `"y"` and a request for `"z"` | Two different instances. |
| `GetKeyedServices<IThing>("x")` | The two exact registrations. The catch-all is not included. |
| `GetKeyedServices<IThing>("y")` | Empty. The catch-all is not included. |
| `GetKeyedServices<IThing>(KeyedService.AnyKey)` | The two exact registrations of `"x"`. The catch-all is not included. |
| `GetKeyedService<IThing>(null)` | The registration without a key. |
| `GetKeyedService<IThing>(KeyedService.AnyKey)` | Throws `InvalidOperationException`. |
| `IServiceProviderIsKeyedService.IsKeyedService(typeof(IThing), "y")` | `true`. |

Not tested: a scoped or transient catch-all, and a catch-all together with an open generic registration.