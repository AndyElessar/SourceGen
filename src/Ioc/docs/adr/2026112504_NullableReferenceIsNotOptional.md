---
status: accepted
date: 2026-11-25
---

# A nullable reference type does not make a dependency optional

## Context and problem statement

Item 7 of section 4.1 of the [constitution](../Constitution.md) made a reference type annotated as nullable an optional dependency. A parameter `IRepo? repo` and an injected property `IRepo? Repo { get; set; }` got `null` when no service was found. MS.DI looks only at the default value of a parameter, so the constitution listed this rule as a documented difference from MS.DI.

The rule made the decision depend on the type, and it needed several exceptions:

- A nullable value type (`int?`) is a different type, so it stays required ([ADR 2026112406](2026112406_OptionalDependencyByNullableType.md)).
- Code without nullable annotations (`#nullable disable`) is always required.
- An initializer such as `= null!` only suppresses the nullable warning and does not count ([ADR 2026112503](2026112503_NullForgivingInitializerIsRequired.md)).

The same class also behaved differently in the container and in MS.DI. A parameter `IRepo? repo` was optional in the container and required in MS.DI.

ASP.NET Core Blazor injects properties with `[Inject]` in `DefaultComponentPropertyActivator`. It does not read nullable annotations. Every `[Inject]` property is required, and a missing service throws `InvalidOperationException` (see Evidence).

## Decision outcome

A dependency is optional only when the user writes the default value:

- A parameter is optional when it has a default value (`IRepo? repo = null`).
- An injected field or property is optional when it has an initializer that counts by item 7 (`IRepo? Repo { get; set; } = null;`).

A nullable annotation alone does not make a parameter or an injected member optional. This applies to reference types and value types in the same way.

The partial accessor is the only exception. A partial accessor whose return type is a reference type annotated as nullable is optional. A partial accessor has no place for a default value. It belongs to the container class, and MS.DI has no partial accessor, so it does not cause a difference from MS.DI. The `?` on its return type has the same role as the choice between `GetService` and `GetRequiredService`. A nullable value type stays required, as ADR 2026112406 states.

The difference "A reference type annotated as nullable is an optional dependency" is removed from section 8 of the constitution. The other differences get new numbers.

The specifications and the code no longer refer to a documented difference by its number. They describe the difference in words and link to section 8. Only the constitution uses the numbers.

This decision replaces one consequence of ADR 2026112503. That ADR said that a user who wants `null` as the default value writes the member as nullable. The user now writes the initializer `= null`.

### Consequences

- Good: one rule for parameters and injected members, for reference types and value types: an explicit default value makes a dependency optional.
- Good: a parameter behaves the same way in the container and in MS.DI. The constitution has one difference fewer.
- Good: `#nullable disable` is no longer a special case. A type from metadata gives the same result with or without nullable annotations.
- Bad: a user who wants an optional dependency writes `= null` in addition to `?`.
- Bad: a `[ServiceKey] string? key` parameter without a default value is required. On a registration without a key, it reports `SGIOC023`. The user writes `[ServiceKey] string? key = null`.
- Bad: a member `IRepo? Repo { get; set; }` without an initializer is required. A user who reads `?` as "can be absent" can find this surprising. The generator reports `SGIOC402` or `SGIOC501`, so the problem shows at compile time.

## Rejected options

- **Keep the nullable annotation as optional.** The rule needs the exceptions above, and it keeps a difference from MS.DI for the most common dependency site, the constructor parameter.
- **Make the nullable return type of a partial accessor required too.** A partial accessor then has no way to ask for an optional service. The user would have to call `GetService` on the container instead, which loses the compile-time check of the accessor.
- **Keep the numbers of the documented differences in the specifications and the code.** A removed or added difference changes the numbers after it, and each reference must change too. A reference in words does not break.

## Evidence

The source of `src/Components/Components/src/DefaultComponentPropertyActivator.cs` (dotnet/aspnetcore, branch `main`, read on 2026-11-25; the commit was not recorded):

- It selects the properties that have `InjectAttribute`, including inherited ones, with `BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic`.
- It reads only `InjectAttribute.Key`. It does not read nullable annotations.
- It resolves each property with `GetService` or `GetKeyedService`. When the result is `null`, it throws `InvalidOperationException` ("Cannot provide a value for property ..."). It has no optional path.

For the behavior of MS.DI with nullable parameters, see the Evidence of [ADR 2026112406](2026112406_OptionalDependencyByNullableType.md).

Not tested: the Blazor activator at run time. The facts above come from its source only. The helper types `MemberAssignment` and `PropertySetter`, which it calls, were not read.
