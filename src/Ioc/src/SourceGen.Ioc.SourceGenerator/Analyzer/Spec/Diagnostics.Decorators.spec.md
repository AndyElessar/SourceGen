# Decorator Diagnostics

Diagnostics for the decorator constructor parameters that receive the decorated instance. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC034 - Warning - Design - Multiple decorator parameters can receive the decorated instance

Report when more than one constructor parameter of a decorator is of the decorated service type. Only the first one receives the decorated instance (see [Decorated Parameter](../../Spec/Register.Decorators.spec.md#decorated-parameter)); the others are resolved from the container like any dependency, which is often unintended.

**Analysis:**

- Checks each decorator the generator applies to a registration's service type: the registration's own `Decorators`, otherwise those of its best `[IocRegisterDefaults]` (see [Default Settings](Analyzer.Register.spec.md#default-settings)). Decorators are matched with the generator's own rules (implemented service type, closed generic constraints).
- Uses the constructor the generator uses: `[IocInject]`, then the primary constructor, then the one with the most parameters.
- Reports each extra parameter once per decorator and service type (the definition for generic service types), however many registrations the decorator decorates.

**Message format:** `Parameter '{Parameter}' of decorator '{Decorator}' is of the decorated type '{ServiceType}' but is resolved as a dependency; only the first such parameter '{DecoratedParameter}' receives the decorated instance`

**Fix:** Give the decorated instance a single parameter, or remove the extra parameter.

---

## SGIOC035 - Error - Design - Decorator has no parameter for the decorated instance

Report when a decorator implements a decorated service type of a registration but none of its constructor parameters is of that type, so the generator skips the decorator for that service type.

**Analysis:**

- Checks the same decorators, service types and constructor as `SGIOC034`.
- A decorator that does not implement the service type, or whose generic constraints are not satisfied, is skipped by the generator without a diagnostic.
- Reports once per decorator and service type (the definition for generic service types), at the decorator constructor (or the decorator type when it has no declared constructor).

**Message format:** `Decorator '{Decorator}' has no constructor parameter of type '{ServiceType}' to receive the decorated instance, so it is not applied to '{ServiceType}'`

**Fix:** Add a constructor parameter of the service type, or stop implementing the service type in the decorator.
