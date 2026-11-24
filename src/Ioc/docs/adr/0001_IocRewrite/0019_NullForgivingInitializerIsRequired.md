---
status: accepted
date: 2026-11-25
---

# An initializer that only suppresses the nullable warning does not make a member optional

> Partly replaced by [ADR IocRewrite/0020](0020_NullableReferenceIsNotOptional.md): a nullable reference type no longer makes a parameter or an injected member optional. The text below is kept as it was.

## Context and problem statement

The list of implicit rules in the [constitution](../../Constitution.md#41-explicitness) makes an injected field or property optional when it has an initializer and can be assigned after construction. The initial value stays when no service is found. [ADR IocRewrite/0011](0011_ImplicitRulesInConstitution.md) added this rule. It treats an initializer as a default value that the user writes.

With nullable reference types, a non-nullable field or property without an initializer causes warning CS8618. Users often write `= null!` to suppress this warning on a member that the container sets:

```csharp
[IocInject] public IClock Clock { get; set; } = null!;
```

Here `null!` is not a default value. It tells the compiler that the member gets its value later. With this rule, this member is optional. When no service provides `IClock`, the generator reports nothing, and `Clock` stays `null`. The first use of `Clock` throws `NullReferenceException` at run time.

## Decision outcome

An initializer counts for this rule only when it gives a real default value. An initializer whose expression is the null-forgiving operator (`!`) applied to `null`, `default`, or `default(T)` does not count. Parentheses around the expression do not change this. A member with such an initializer is a required dependency, the same as a member without an initializer.

The generator decides this from the syntax of the initializer. The rule needs no semantic analysis.

The same change also states the scope of the rule more exactly. An initializer counts only for a member that is declared in the current compilation. The test is the assembly of the member, not the assembly of the implementation type. A base class from a referenced assembly has no initializer in metadata. In the IDE, a project reference gives its syntax, so a test on the type would give a different result in the IDE and on the command line.

### Consequences

- Good: a missing dependency of a member with `= null!` reports `SGIOC402` without a fallback provider, and `SGIOC501` with one. It does not fail later at run time.
- Good: the rule follows the intent of the user. `!` says "the value comes later", and a real initializer says "use this value when nothing else comes".
- Bad: the rule is one more exception in the list of implicit rules. A user who wants `null` as the default value writes the member as nullable (`IClock?`), which makes it optional by the same list of rules.

## Rejected options

- **Keep every initializer as a default value.** A missing dependency of the common `= null!` pattern then gives no diagnostic and fails at run time. This goes against the compile-time guarantee of the constitution.
- **Exclude every initializer that gives the default value of its type, such as `= null`, `= default`, or `= 0`.** The generator would need the constant value of the initializer. `= 0` or `= default` on a value type can be a value that the user wants. `= null` without `!` on a non-nullable type causes warning CS8625, and on a nullable type the member is already optional by its annotation. The `!` is the clear sign that the user does not mean a default value.
- **Add an explicit setting, such as a `Required` property on `IocInjectAttribute`.** This adds API for a fact that the code already states with `!`. A user who forgets the setting gets the same silent `null` as before.

## Evidence

No run-time behavior applies to this decision. The generator reads the syntax: `null!` is a `PostfixUnaryExpressionSyntax` of kind `SuppressNullableWarningExpression` (Microsoft.CodeAnalysis.CSharp 5.12.0). The generator tests of member injection cover each excluded form and a real initializer.

The rule does not look through a cast. An initializer such as `= (IClock)null!` or `= (IClock)default!` still counts as a real initializer, so the member stays optional. These forms are rare, because `null!` alone already suppresses the warning.
