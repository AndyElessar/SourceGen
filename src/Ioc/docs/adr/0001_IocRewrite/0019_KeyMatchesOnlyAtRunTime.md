---
status: accepted
date: 2026-11-26
---

# A key that matches only at run time is a warning

## Context and problem statement

A key can be a value or a symbol (see [Keys](../../spec/Annotations.spec.md#keys)). A `const` symbol is normalized to its value. A symbol that is not `const`, for example a `static readonly` field, is known only by its name at compile time.

The container matches the key of a dependency site with the key of a registration by their compile-time identity ([constitution](../../Constitution.md#8-documented-differences-from-msdi), documented difference 16). A lookup at run time by `Type` and key uses `Equals`. MS.DI uses `Equals` in both cases.

So a site with `static readonly string A = "a"` does not match a registration with the key `"a"`, but `GetKeyedService` with `"a"` or with `A` finds it. The site gets a missing dependency (`SGIOC402`), the fallback provider (`SGIOC501`), or an empty collection. In the last case, the user gets no diagnostic. This ADR records how the generator handles this case.

## Decision outcome

The container keeps the match by compile-time identity. Its full check reports `SGIOC505` (Warning) when a site key has no registration of the same identity, but the key of a registration of the same service type can be equal to it at run time. The check covers a site of one service (also in `Lazy<T>`, `Func<T>` or `Task<T>`) and a site of a collection. The other wrapper types are added to the check with their own slice of the rewrite. The rule is in [Semantics: Keyed services](../../spec/Semantics.spec.md#keyed-services).

- Two keys can be equal when at least one of them is a symbol and their key types are the same, or one of them is `object`. The check does not look at other combinations, for example `int?` and `int`.
- The service collection extension does not report it, because MS.DI uses `Equals`.
- The message tells the user to use the same key member, or a `const` key.

### Consequences

- Good: the case that gave no diagnostic now gives a Warning on the container.
- Good: every edge of the dependency graph stays known at compile time. The checks of cycles, captive dependencies, and held Transients stay complete.
- Good: the generated code does not change, so the performance does not change.
- Bad: a site that is correct by design also gets the Warning, for example a site whose symbol is never equal to the registered key. The user suppresses it with `#pragma` or `.editorconfig`.
- Bad: the check can miss keys of other types that are equal at run time.

## Rejected options

- **Match at run time with `Equals`.** A site that has no match at compile time looks up the table at run time. Then the target of the edge is not known at compile time. The full check must add an edge to each registration that can be equal, which reports false cycles and false captive dependencies, or it must ignore the edge, which misses real ones. A missing dependency can also not be reported for such a site. This goes against the first principle of the constitution, and it changes documented difference 16.
- **An error instead of a warning.** The generator cannot know whether the keys are equal. An error would block a container that is correct.
- **No diagnostic.** The case stays silent. A user finds it only at run time, as an empty collection or a service from the fallback provider.

## Evidence

Tested with SourceGen.Ioc after commit `69365ba` and this change, .NET SDK 11.0.100, `Microsoft.Extensions.DependencyInjection` 10.0.15 on net10.0 and 11.0.0 on net11.0. The tests are the unit tests `RunTimeKeyTests` and the NativeAOT test `KeyedCollection_SiteKeyEqualToARegisteredKeyAtRunTime_ContainerStillWorks`.

| Case | Result |
| --- | --- |
| Site `IReadOnlyList<IStore>` with `StoreAliases.A` (`static readonly string A = "a"`), registration with the key `"a"` | `SGIOC505`. The site gets an empty collection. |
| `GetKeyedServices<IStore>("a")`, and `GetRequiredKeyedService<IReadOnlyList<IStore>>` with `"a"` or with `StoreAliases.A` | The registered store. |
| Site with the same symbol, or with a `const` equal to `"a"` | Matched. No `SGIOC505`. |
| Site with an `int` symbol, registration with a `string` key | No `SGIOC505`. |
| Site with the value `"b"`, registration with the value `"a"` | No `SGIOC505`. |
| Severity set to `Suppress` or to `Error` | The generated container is the same. |
| The same site in an assembly that generates the service collection extension, without a container | No `SGIOC505`. |

Not tested: `int?` and `int` keys, a key of an interface type, two different symbols of the same type, and an `object` symbol at the site.
