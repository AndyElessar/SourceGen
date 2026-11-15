# Factory Diagnostics

Diagnostics for Factory and Instance members given with nameof() and for generic factory mappings. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC008 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute`/`IoCRegisterForAttribute`/`IoCRegisterDefaultsAttribute` has specify `Factory` or `Instance` and use nameof(), but field/property/method in nameof() is not static or is inaccessible.

**Analysis:**

- Checks the `Factory` or `Instance` parameter of `[IoCRegister]`/`[IoCRegisterFor]`/`[IoCRegisterDefaults]` attributes.
- When `nameof()` is used to specify a member, resolves that member symbol.
- Reports when:
  - Member is not static.
  - Member is private or protected.
  - Member's containing type is private.

---

## SGIOC016 - Error - Design - Factory Method is unmatched

Report when:

- `[IocRegister]`/`[IoCRegisterFor]`/`[IoCRegisterDefaults]` has specificed `Factory` but the factory method is generic and does not mark with `[IocGenericFactory]`.
- `[IocGenericFactory]` or `GenericFactoryTypeMapping` cannot fill every factory method type parameter. The generator skips such registrations.

**Analysis:**

- Checks Factory member specified via `nameof()` on `[IocRegister]`, `[IoCRegisterFor]`, or `[IoCRegisterDefaults]` attributes.
- When the Factory references a method symbol, checks if the method is generic (has type parameters).
- If the method is generic, checks if it has `[IocGenericFactory]` attribute.
- The diagnostic does NOT fire if `GenericFactoryTypeMapping` is provided on the registration attribute (`IocRegisterForAttribute` or `IocRegisterDefaultsAttribute`) AND the number of placeholder types (mapping array length minus 1) equals the factory method's type parameter count.
- Reports when the factory method is generic but neither `[IocGenericFactory]` attribute on the method NOR a valid `GenericFactoryTypeMapping` on the registration attribute provides the type mapping.
- Checks every method marked with `[IocGenericFactory]`, whether or not it is referenced as a factory. Reports when:
  - The mapping has no service type template or no placeholder type.
  - The placeholder count differs from the method's type parameter count.
  - A placeholder type has type arguments or is an array, such as `List<int>`, `int?`, `(int, string)` or `int[]`: the template is matched into their type arguments, so the generator never finds them.
  - A placeholder type does not appear in the service type template (at any nesting level, including array and tuple element types). The type arguments of a containing type, such as `int` in `Outer<int>.Inner`, are not part of the template, and the message shows the containing types of the template. A placeholder MAY appear more than once; a service type that gives it different type arguments is reported by `SGIOC039`.
- Applies the same count and template checks to `GenericFactoryTypeMapping` on the registration attribute.
- Duplicate placeholders are reported by `SGIOC017` instead.

**Message format:** `Generic factory method '{MethodName}' has no valid type parameter mapping: {Reason}`

---

## SGIOC017 - Error - Design - Generic Factory Method's type parameters are duplicated

Report when:

- `[IocGenericFactory]`'s type parameters from second to end have duplicated types, they must be unique.
- `GenericFactoryTypeMapping` property on `[IocRegisterFor]` or `[IocRegisterDefaults]` attribute contains duplicate placeholder types.

**Analysis:**

- Checks methods marked with `[IocGenericFactory]` attribute.
  - Extracts the type array from the attribute's constructor arguments.
  - Starting from the second type (index 1), checks if any type appears more than once.
  - Reports when duplicate placeholder types are found, as each type must uniquely map to a factory method type parameter.
- Checks the `GenericFactoryTypeMapping` property on `[IocRegisterFor]` or `[IocRegisterDefaults]` attributes.
  - Validates that all placeholder types in the mapping are unique.
  - Reports when duplicate placeholder types are detected in the mapping.

---

## SGIOC039 - Warning - Design - Service type does not match the generic factory mapping

Report when a service type of a registration whose `Factory` (its own, or the `Factory` of its best default settings) is a generic factory does not match the service type template of the factory's mapping (`[IocGenericFactory]` or `GenericFactoryTypeMapping`), so the factory's type arguments cannot be determined for it. The generator does not register that service type in either output (see [Generic Registration](../../Spec/Register.Generics.spec.md#feature-3-generic-factory-type-mapping)); the implementation type itself is still registered: constructed, or calling the factory when the mapping matches it.

**Analysis:**

- Runs at compilation end over the registrations `RegisterAnalyzer` collects, with the `Factory` method resolved by the attribute syntax node action. Registrations with an `Instance`, factories given as a string, and generic factories without a valid mapping (`SGIOC016`) are not analyzed.
- Checks each service type of the registration, matched against the template like the generator. The implementation type of a closed registration is checked only when generated code cannot construct it (an interface, an abstract or static class, or a class without a public constructor or an internal one it can access: declared in the same assembly, or in one that gives it access with `InternalsVisibleTo`), such as the target of `[IocRegisterFor<IRequestHandler<Entity>>]`; otherwise it is constructed when the mapping does not match it. A service type of an open generic registration is reported only when it cannot match for any type arguments: a type parameter where the template has a constructed type, such as `IRequestHandler<T>` for the template `IRequestHandler<Task<int>>`, may match some closings and is not reported. A placeholder repeated in the template, such as `int` in `IPair<int, int>`, is reported when it takes different type arguments (`IPair<Entity, Dto>`), unless they may become equal for some closing (`IPair<T, int>`, or `IPair<T[], int[]>`, but not `IPair<List<T>, int>`).
- The closed types a registration of `[IocRegisterDefaults]` without an implementation type is closed for are discovered from dependencies, so they are not analyzed.
- Reported once per service type, at the registration attribute.

**Message format:** `Service type '{ServiceType}' of '{Implementation}' does not match the service type template '{Template}' of the generic factory '{Factory}', so it is not registered`

**Fix:** Remove the service type, or register it without the generic factory.

---

## Known Limitations

- `SGIOC039`: only the registrations `RegisterAnalyzer` collects are analyzed, so the service types the generator skips for implementations added by `[IocRegisterDefaults(ImplementationTypes = ...)]` and for the registrations of imported modules are not reported. The `Factory` of type-level default settings is analyzed like that of assembly-level ones, and the default settings of imported modules have no `Factory`, like in the generator. It can miss a skipped service type, but does not report one the generator registers.
