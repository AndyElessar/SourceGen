---
status: accepted
date: 2026-11-24
---

# Initializers and the object key type in the list of implicit rules

## Context and problem statement

Section 4.1 of the [constitution](../Constitution.md) lists the only cases in which the generator decides a result from types alone. Everything else needs an explicit setting.

The specifications had two rules that were not in that list:

- An injected field or property with an initializer is an optional dependency. When no service is found, the generator does not assign it, and the initial value stays (see [Semantics](../spec/Semantics.spec.md#dependency-resolution)).
- A dictionary, a `KeyValuePair<TKey, T>`, or a collection of `KeyValuePair<TKey, T>` with `TKey` equal to `object` contains all keyed registrations. With any other `TKey`, only the registrations whose key type is exactly `TKey` match (see [Analysis model](../spec/Analysis.Model.spec.md#grammar-of-wrapper-types)).

Thus the specifications contradicted the constitution.

## Decision outcome

Both rules are added to the list in section 4.1:

- Item 7 now includes a field or property that has `[IocInject]`, has an initializer, and can be assigned after construction. The initializer counts only for a type whose source is in the current compilation, because metadata does not keep the initializer.
- The rule does not cover an `init` or `required` property, because an object initializer cannot assign it conditionally. It also does not cover a member that `IocInjectMemberAttribute` specifies. These stay required dependencies, as the specifications state.
- Item 9 is new. It states the match by key type and the `object` case.

### Consequences

- Good: the constitution and the specifications agree.
- Good: an initializer is a default value that the user writes. It is handled the same way as the default value of a parameter.
- Bad: the same declaration gives a different result for a type from metadata. The field or property is then a required dependency. The specifications state this limit.

## Rejected options

- **Remove the initializer rule and require an explicit setting.** The initializer already states the default value. A second setting for the same fact adds work and can disagree with the initializer.
- **Remove the `object` case and require an exact key type.** Then the user cannot ask for keyed services of different key types in one dictionary. `object` is the only type to which every key converts.

## Evidence

No tests apply to this decision.
