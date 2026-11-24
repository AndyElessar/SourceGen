---
status: accepted
date: 2026-11-25
---

# Which dependencies are optional

## Context and problem statement

An optional dependency gets a default when no service is found. A required dependency reports `SGIOC402` without a fallback provider, and `SGIOC501` with one. The list of implicit rules in the [constitution](../../Constitution.md#41-explicitness) decides which dependency sites are optional. These cases needed a decision:

- A parameter or an injected member whose type is a reference type annotated as nullable (`IRepo? repo`).
- A nullable value type (`int?`, `KeyValuePair<string, IRepo>?`) without a default value, on a parameter or on the return type of a partial accessor.
- A parameter that has `[Optional]` and no default value (`[Optional] IRepo repo`).
- An injected field or property with an initializer, and in particular `= null!`. [ADR IocRewrite/0011](0011_ImplicitRulesInConstitution.md) added initializers to the list of implicit rules. With nullable reference types, a non-nullable member without an initializer causes warning CS8618. Users often write `= null!` only to suppress this warning on a member that the container sets, as in `[IocInject] public IClock Clock { get; set; } = null!;`. If such a member were optional, a missing service would give no diagnostic, and the first use of `Clock` would throw `NullReferenceException` at run time.
- An initializer of a member that another assembly declares.

MS.DI is the reference (see [Purpose](../../Constitution.md#1-purpose)). MS.DI looks only at the default value of a parameter. It does not look at a nullable annotation or at `[Optional]` (see [Evidence](#evidence)).

A nullable reference type (`IRepo?`) is an annotation only. At run time it is the same type as `IRepo`, so the service to look up does not change. A nullable value type (`int?`) is a different type, `Nullable<int>`. MS.DI also treats it as a different service type: `GetService(typeof(int?))` looks for a registration of `Nullable<int>` and not of `int`.

ASP.NET Core Blazor injects properties with `[Inject]`. It does not read nullable annotations, and every `[Inject]` property is required (see [Evidence](#evidence)).

## Decision outcome

A dependency is optional only when the user writes its default value:

- A parameter is optional when it has a default value (`IRepo? repo = null`). This also applies to a parameter of an injection method and to a `[ServiceKey]` parameter.
- An injected field or property is optional when it has an initializer and can be assigned after construction (`IRepo? Repo { get; set; } = null;`). The generator does not assign it when no service is found, and the initial value stays.

A nullable annotation alone does not make a parameter or an injected member optional. This applies to reference types and value types in the same way. A parameter that has `[Optional]` and no default value is required.

An initializer counts only in these cases:

- The member is declared in the current compilation. The test is the assembly of the member, not the assembly of the implementation type and not the availability of syntax. Metadata does not keep the initializer. In the IDE, a project reference gives syntax for another assembly, so a test on syntax would give a different result in the IDE and on the command line.
- The initializer gives a real default value. An initializer whose expression is the null-forgiving operator (`!`) applied to `null`, `default`, or `default(T)` does not count. Parentheses around the expression do not change this. Such an initializer only suppresses the nullable warning. `!` says "the value comes later", and a real initializer says "use this value when nothing else comes". The generator decides this from the syntax of the initializer. It does not look through a cast, so `= (IClock)null!` or `= (IClock)default!` still counts as a real initializer. This form is rare, because `null!` alone already suppresses the warning.

The partial accessor is the only exception to the rule about annotations. A partial accessor whose return type is a reference type annotated as nullable is optional. A partial accessor has no place for a default value. It belongs to the container class, and MS.DI has no partial accessor, so the exception causes no difference from MS.DI. The `?` on its return type has the same role as the choice between `GetService` and `GetRequiredService`. A nullable value type on a partial accessor is required.

The constitution states these rules in its list of implicit rules. It has no documented difference about optional dependencies.

### Consequences

- Good: one rule for parameters and injected members, for reference types and value types: an explicit default value makes a dependency optional.
- Good: a parameter behaves the same way in the container and in MS.DI.
- Good: `#nullable disable` is not a special case. A type from metadata gives the same result with or without nullable annotations.
- Good: a missing dependency of a member with `= null!`, or of an `int?` parameter, reports `SGIOC402` or `SGIOC501`. It does not fail later at run time with a silent `null`.
- Bad: a user who wants an optional dependency writes `= null` in addition to `?`.
- Bad: a member `IRepo? Repo { get; set; }` without an initializer is required. A user who reads `?` as "can be absent" can find this surprising. The generator reports the problem at compile time.
- Bad: a `[ServiceKey] string? key` parameter without a default value is required. On a registration without a key, it reports `SGIOC023`. The user writes `[ServiceKey] string? key = null`.
- Bad: a partial accessor cannot ask for an optional value type.
- Bad: the exclusion of `= null!` is one more exception in the list of implicit rules.

## Rejected options

- **A reference type annotated as nullable is optional.** This was the first rule, and the constitution listed it as a documented difference from MS.DI. It needed exceptions: a nullable value type, `#nullable disable`, and `= null!`. The same class also behaved differently in the container and in MS.DI, at the most common dependency site, the constructor parameter.
- **A nullable value type is optional, on a parameter or on a partial accessor.** An optional `int?` can work in two ways only, and both are wrong:
  - The generator looks up `int` and converts the result to `int?`. This is an implicit conversion of types, which the [constitution](../../Constitution.md#41-explicitness) forbids.
  - The generator looks up `Nullable<int>` and uses `null` when it finds nothing. A container registers only reference types, so the result is almost always `null`. For `KeyValuePair<TKey, T>?` the generated `KeyValuePair<TKey, T>` never matches. The user expects an injection and silently gets `null`.

  On a parameter, it also adds a new difference from MS.DI, so the same class behaves differently in the container and in the service collection extension.
- **The nullable return type of a partial accessor is required too.** A partial accessor then has no way to ask for an optional service. The user would have to call `GetService` on the container instead, which loses the compile-time check of the accessor.
- **`[Optional]` without a default value counts as the default value `default`.** MS.DI does not do this, so it would be a new documented difference. A user who wants an optional parameter can write a default value.
- **Every initializer counts as a default value, also `= null!`.** A missing dependency of the common `= null!` pattern then gives no diagnostic and fails at run time. This goes against the compile-time guarantee of the constitution.
- **Exclude every initializer that gives the default value of its type, such as `= null`, `= default`, or `= 0`.** The generator would need the constant value of the initializer. `= 0` or `= default` on a value type can be a value that the user wants. `= null` without `!` on a non-nullable type causes warning CS8625. Excluding `= null` would also break the recommended way to make a member optional, `IRepo? Repo { get; set; } = null;`. The `!` is the clear sign that the user does not mean a default value.
- **An explicit setting, such as a `Required` property on `IocInjectAttribute`.** This adds API for a fact that the code already states with a default value or with `!`. A user who forgets the setting gets a silent `null`.

## Evidence

MS.DI, tested with `Microsoft.Extensions.DependencyInjection` 10.0.15 on net10.0 (.NET SDK 11.0.100). Each class was registered as Transient and resolved with `GetRequiredService` from an otherwise empty `ServiceCollection`:

| Constructor parameter | Result of MS.DI |
| --- | --- |
| `int? value` | `InvalidOperationException`: unable to resolve `Nullable<Int32>` |
| `int? value = null` | Resolved, the value is `null` |
| `IRepo? value` | `InvalidOperationException`: unable to resolve `IRepo` |
| `KeyValuePair<string, IRepo>? value` | `InvalidOperationException`: unable to resolve the nullable type |
| `[Optional] IRepo repo` | `InvalidOperationException`: unable to resolve `IRepo` |
| `[Optional] int value` | `InvalidOperationException`: unable to resolve `Int32` |

The source of MS.DI (dotnet/runtime, branch `release/10.0`) confirms it. `CallSiteFactory.CreateArgumentCallSites` uses the default value only when no registration is found, through `ParameterDefaultValue.TryGetDefaultValue`. That method decides with `ParameterInfo.HasDefaultValue` only. The code does not read nullable annotations or `ParameterInfo.IsOptional`. The C# compiler writes no default constant for `[Optional]` without a default value, so `HasDefaultValue` is `false`.

Blazor: the source of `src/Components/Components/src/DefaultComponentPropertyActivator.cs` (dotnet/aspnetcore, branch `main`, read on 2026-11-25; the commit was not recorded):

- It selects the properties that have `InjectAttribute`, including inherited ones, with `BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic`.
- It reads only `InjectAttribute.Key`. It does not read nullable annotations.
- It resolves each property with `GetService` or `GetKeyedService`. When the result is `null`, it throws `InvalidOperationException` ("Cannot provide a value for property ..."). It has no optional path.

Roslyn: `null!` is a `PostfixUnaryExpressionSyntax` of kind `SuppressNullableWarningExpression` (Microsoft.CodeAnalysis.CSharp 5.12.0). The generator tests of member injection cover each excluded form and a real initializer.

Not tested:

- MS.DI 11.0.0.
- The Blazor activator at run time. The facts above come from its source only. The helper types `MemberAssignment` and `PropertySetter`, which it calls, were not read.
