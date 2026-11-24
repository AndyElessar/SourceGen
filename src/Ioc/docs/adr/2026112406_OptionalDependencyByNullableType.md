---
status: accepted
date: 2026-11-24
---

# A nullable value type and `[Optional]` do not make a dependency optional

> Partly replaced by [ADR 2026112504](2026112504_NullableReferenceIsNotOptional.md): a nullable reference type no longer makes a parameter or an injected member optional. The text below is kept as it was.

## Context and problem statement

The list of implicit rules in the [constitution](../Constitution.md#41-explicitness) makes a reference type annotated as nullable an optional dependency. It did not say how two other cases behave:

- A nullable value type (`int?`, `KeyValuePair<string, IRepo>?`) without a default value. This applies to a parameter and to the return type of a partial accessor.
- A parameter that has `[Optional]` and no default value (`[Optional] IRepo repo`).

MS.DI is the reference (see [Purpose](../Constitution.md#1-purpose)). MS.DI looks only at the default value of a parameter. It does not look at a nullable annotation or at `[Optional]` (see [Evidence](#evidence)).

A nullable reference type (`IRepo?`) is an annotation only. At run time it is the same type as `IRepo`, so the service to look up does not change. A nullable value type (`int?`) is a different type, `Nullable<int>`. MS.DI also treats it as a different service type: `GetService(typeof(int?))` looks for a registration of `Nullable<int>` and not of `int`.

## Decision outcome

- A nullable value type is a required dependency, on a parameter and on the return type of a partial accessor. Only a nullable reference type makes a dependency optional by its type.
- A parameter that has `[Optional]` and no default value is a required dependency.
- The constitution does not change. Its list of implicit rules already names only a reference type annotated as nullable.

### Consequences

- Good: one rule for parameters and partial accessors. A `?` makes a dependency optional only when it does not change the type.
- Good: for a parameter, the only difference from MS.DI stays the one of a nullable reference type (a documented difference of the constitution).
- Good: a missing `int?` gives `SGIOC402` without a fallback provider, and not a silent `null`.
- Bad: a user who wants an optional value type writes a default value (`int? limit = null`). A partial accessor has no default value, so it cannot ask for an optional value type.

## Rejected options

- **The nullable return type of a partial accessor is optional, also for a value type.** The user of a partial accessor writes `T?` to say that the service can be absent. But an optional `int?` can work in two ways only, and both are wrong:
  - The generator looks up `int` and converts the result to `int?`. This is an implicit conversion of types, which the [constitution](../Constitution.md#41-explicitness) forbids.
  - The generator looks up `Nullable<int>` and uses `null` when it finds nothing. A container registers only reference types, so the result is almost always `null`. For `KeyValuePair<TKey, T>?` the generated `KeyValuePair<TKey, T>` never matches. The user expects an injection and silently gets `null`.
- **A nullable value type is optional on a parameter too.** It has the same problem, and it adds a new difference from MS.DI. The same class would then behave differently in the container and in the service collection extension.
- **`[Optional]` without a default value counts as the default value `default`.** MS.DI does not do this, so it would be a new documented difference. A user who wants an optional parameter can write a default value.

## Evidence

Tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on net10.0 (.NET SDK 11.0.100). Each class was registered as Transient and resolved with `GetRequiredService` from an otherwise empty `ServiceCollection`:

| Constructor parameter | Result of MS.DI |
| --- | --- |
| `int? value` | `InvalidOperationException`: unable to resolve `Nullable<Int32>` |
| `int? value = null` | Resolved, the value is `null` |
| `IRepo? value` | `InvalidOperationException`: unable to resolve `IRepo` |
| `KeyValuePair<string, IRepo>? value` | `InvalidOperationException`: unable to resolve the nullable type |
| `[Optional] IRepo repo` | `InvalidOperationException`: unable to resolve `IRepo` |
| `[Optional] int value` | `InvalidOperationException`: unable to resolve `Int32` |

The source of MS.DI (dotnet/runtime, branch `release/10.0`) confirms it. `CallSiteFactory.CreateArgumentCallSites` uses the default value only when no registration is found, through `ParameterDefaultValue.TryGetDefaultValue`. That method decides with `ParameterInfo.HasDefaultValue` only. The code does not read nullable annotations or `ParameterInfo.IsOptional`. The C# compiler writes no default constant for `[Optional]` without a default value, so `HasDefaultValue` is `false`.

Not tested: MS.DI 11.0.0.
