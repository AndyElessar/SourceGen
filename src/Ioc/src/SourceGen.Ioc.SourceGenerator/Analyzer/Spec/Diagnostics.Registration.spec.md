# Registration Diagnostics

Diagnostics for registration attributes and their default settings: target types, Instance lifetime, Factory and Instance conflicts, duplicated registrations and default settings, service type compatibility, open types nested in a generic type and nameof() of generic types. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC001 - Error - Usage - Invalid Attribute Usage

Report when IoCRegisterAttribute or IoCRegisterForAttribute is mark on private or abstract class, or on a type that generated code cannot construct and that has neither a `Factory` (its own or of its best default settings) nor an `Instance`. The generator does not register such a type in either output.

**Analysis:**

- Collects all types marked with `[IoCRegister]` or `[IoCRegisterFor]` (both assembly-level and type-level).
- Checks the target type's accessibility modifier and abstract state.
- Reports when the type is `private` or is an `abstract` class (not interface).
- At compilation end, over the registrations `RegisterAnalyzer` collects (`AnalyzeUnconstructibleImplementations`): reports an implementation type without a `Factory` (its own, or the `Factory` of its best default settings) or an `Instance` that is an interface, or a class without a constructor accessible from the compiled assembly: only `private` or `protected` constructors, or `internal` ones declared in a referenced assembly that does not give access with `InternalsVisibleTo`. Open generic implementations are checked the same way. The `ImplementationTypes` of default settings are checked too, reported at the default settings attribute: an open one is created with the `Factory` of its settings, while a closed one never is. Types already reported as `private` or `abstract` are not reported again. The open generic `[IocRegisterFor]` registrations of an imported module (recursively), which the generator closes in the importing assembly without their `Factory` or `Instance`, are reported at the `[IocImportModule]` attribute when the importing assembly cannot construct the implementation, such as `Repo<T>` with only an `internal` constructor and no `InternalsVisibleTo` (`AnalyzeImportedOpenRegistrations`).

**Message format:** `The type '{Type}' cannot be registered because {Reason}`, where the reason is `it is private`, `it is abstract`, `it has no constructor accessible from this assembly; specify a Factory or Instance`, or `it is an interface; specify a Factory or Instance` (another type kind, such as an enum, is named likewise).

---

## SGIOC009 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Instance` and the effective lifetime is not Singleton. The Register (MS.DI) output does not register such an `Instance`.

**Analysis:**

- Runs at compilation end, once the default settings are known (`ResolveRegistrations`).
- Checks registration attributes that specify `Instance`.
- Gets the effective lifetime like the generator's `MergeSettings`: the `Lifetime` of the attribute, otherwise the lifetime of its best `[IocRegisterDefaults]`, otherwise the MSBuild `SourceGenIocDefaultLifetime`, otherwise Transient (see [Default Settings](Analyzer.Register.spec.md#default-settings)).
- Reports when the effective lifetime is not Singleton; the message names it.

**Message format:** `Instance registration '{Instance}' requires Singleton lifetime, but its lifetime is '{Lifetime}'`

---

## SGIOC010 - Error - Usage - Invalid Attribute Usage

Report when `IoCRegisterAttribute` or `IoCRegisterForAttribute` has specify `Factory` and `Instance` at same attribute. `Instance` takes precedence and `Factory` is ignored (see [Factory](../../Spec/Register.Factory.spec.md#instance-and-factory-precedence)).

**Analysis:**

- Checks named arguments of registration attributes.
- Reports when both `Factory` and `Instance` are specified on the same attribute.

---

## SGIOC011 - Warning - Design - Duplicated Registration Detected

Report when there are duplicated registrations for same implement type, same key, and at least one matching tag.
When TagOnly=false, the registration is considered to have an empty tag for comparison.

**Analysis:**

- Runs at compilation end, once the default settings are known (`ResolveRegistrations`), over the registrations in source order, so the later duplicate is reported.
- Collects all registrations and builds an index of `(ImplementationType, Key, Tag)`. Implementation types are compared as runtime types (`RuntimeTypeSymbolComparer`), so `Impl<(int A, int B)>` and `Impl<(int X, int Y)>` are the same implementation type.
- The tags of a registration without `Tags` are those of its best `[IocRegisterDefaults]`, like the generator (see [Default Settings](Analyzer.Register.spec.md#default-settings)).
- When `TagOnly=false`, automatically adds an empty string as an additional Tag.
- Checks each effective Tag; reports if the same combination already exists.

---

## SGIOC012 - Warning - Design - Duplicated Registration Detected

Report when there are duplicated `IoCRegisterDefaults` for same target type and at least one matching tag.
When TagOnly=false, the setting is considered to have an empty tag for comparison.

**Analysis:**

- Collects all `[IoCRegisterDefaults]` of the compiled assembly (both assembly-level and type-level), in the order of the generator (see [Default Settings](Analyzer.Register.spec.md#default-settings)): non-generic attributes, then generic attributes, each in source order. The settings the generator ignores, which come later, are reported.
- The settings of imported modules are not reported.
- Builds an index of `(TargetTypeName, Tag)`.
- When `TagOnly=false`, automatically adds an empty string as an additional Tag.
- Checks each effective Tag; reports if the same combination already exists.

---

## SGIOC032 - Warning - Design - Service type is not compatible with implementation

Report when an explicit service type of `[IocRegister]`/`[IocRegister<T>]`/`[IocRegisterFor]`/`[IocRegisterFor<T>]` is skipped by the generator because its generic shape does not match the implementation type.

**Analysis:**

- Checks service types from `ServiceTypes` and from generic attribute type arguments (type-level and assembly-level registrations).
- Reports when:
  - The implementation is an open generic type but the service type is not.
  - The implementation is not an open generic type, the service type is, and the implementation does not implement or inherit any closed form of it. A closed implementation that does is registered as each such closed form (see [Basic Registration](../../Spec/Register.Basic.spec.md#feature-basic-registration)).
  - Both are open generic, the service type is not the implementation itself, and the implementation does not implement or inherit any form of the service type.
- Does not report when the implementation implements the open service type with reordered or partly closed type arguments (for example `Handler<TResponse, TRequest> : IHandler<TRequest, TResponse>` or `TextHandler<T> : IHandler<T, string>`). Only the open generic registration is skipped in that case; discovered closed service types are still registered through type argument mapping (see [Generic Registration](../../Spec/Register.Generics.spec.md#type-argument-mapping)).
- Service types implicitly added by `RegisterAllInterfaces`/`RegisterAllBaseClasses` are not reported.

**Message format:** `Service type '{ServiceType}' cannot be registered for implementation '{ImplementationType}' and will be skipped: {Reason}`

---

## SGIOC038 - Error - Usage - Open type nested in a generic type is not supported

Report when a registration or default settings attribute uses an open type declared in a generic type, such as `Outer<O>.Node<T>`, `Outer<O>.Plain` or `typeof(Outer<>.INode<>)`. The generator closes open generic types with their own type arguments, not with those of their containing type, so it ignores such a type (see [Generic Registration](../../Spec/Register.Generics.spec.md#types-nested-in-a-generic-type)).

**Analysis:**

- Checks the type `[IocRegister]` is applied to, reported at the attribute. Its other arguments are not reported, since the whole registration is ignored.
- Checks each `typeof()` in the constructor arguments (the target of `[IocRegisterFor]` / `[IocRegisterDefaults]`, and service types) and in `ServiceTypes`, `Decorators` and `ImplementationTypes` of `[IocRegister]`, `[IocRegisterFor]` and `[IocRegisterDefaults]`, reported at the `typeof()` expression (`AnalyzeOpenTypesNestedInGenericType`). Default settings whose target is such a type are ignored as a whole, including their `ImplementationTypes`.
- Checks the interfaces and base classes an open implementation is registered as through `RegisterAllInterfaces` / `RegisterAllBaseClasses` (its own or those of its default settings), such as `Outer<T>.INode<T>` of `Node<T> : Outer<T>.INode<T>`, reported at the registration attribute at compilation end, once the default settings are known (`ResolveRegistrations`), so live IDE analysis without full solution analysis does not show them, and those of an open `ImplementationTypes` entry of `[IocRegisterDefaults]` with `RegisterAllInterfaces` / `RegisterAllBaseClasses`, reported at its `typeof()` (`AnalyzeOpenTypesNestedInGenericType`). The implicit check is skipped when the target of `[IocRegisterDefaults]` is itself such a type, since the settings are already ignored as a whole.
- Checks the interfaces and base classes an open decorator in `Decorators` declares and takes as a constructor parameter, such as `Outer<T>.INode<T>` of `NodeDecorator<T>(Outer<T>.INode<T> inner) : Outer<T>.INode<T>`, reported at its `typeof()` (`AnalyzeOpenTypesNestedInGenericType`): the decorator is not applied to that form. Nested forms it only implements, such as a marker interface, are not reported.
- A type is reported when one of its containing types is generic and it uses type parameters (its own or its containing types'). The message names the innermost containing type with type parameters, such as `Outer<O>` for `Outer<O>.Mid.Node<T>`.
- Closed types declared in a generic type, such as `typeof(Outer<int>.Node<string>)`, and generic types declared in a non-generic type are not reported.
- Like the generator, the analyzers ignore these types: such a registration, service type, decorator, implementation type, or default settings (by their target) is not collected for other diagnostics, so a dependency that only it would resolve is reported by `SGIOC018`.

**Message format:** `'{Type}' is an open type declared in the generic type '{ContainingType}', which the generator cannot close, so it is ignored; use its closed types instead`

**Fix:** Use the closed types that are needed, such as `[IocRegisterFor(typeof(Outer<int>.Node<string>))]` or `ServiceTypes = [typeof(Outer<int>.INode<string>)]`, or move the type out of the generic type.

---

## SGIOC040 - Warning - Design - Key uses the type parameters of the open generic implementation

Report when the C# `Key` (`KeyType.Csharp`) of an open generic registration uses the type parameters of the implementation type, such as `Key = nameof(Name)` on `Node<T>` or `Key = nameof(Keys<T>.Name)`. An open generic registration has a single key, while such a key can differ per closed type and does not compile without the type parameters, so the generator does not emit the open registration and registers only the closed types discovered at compile time, each with the key read from its closed type (`Node<string>.Name`) (see [Factory](../../Spec/Register.Factory.spec.md#instance-and-factory-precedence)).

**Analysis:**

- Checks `[IocRegister]` / `[IocRegister<T>]` / `[IocRegisterFor]` / `[IocRegisterFor<T>]` attributes with a `Key` and `KeyType = KeyType.Csharp` whose registered type is an open generic type (the type the attribute is applied to, or an unbound `typeof(Node<>)` target). Open types nested in a generic type are ignored (`SGIOC038`).
- Reads the key like the generator (`GetKeyInfo` with the semantic model, which resolves `nameof()` to the member's path) and reports when it refers to a type parameter of the implementation type where a type parameter can be written (`ReferencesTypeParameters`, shared with the generator), so member names such as `Keys.T` are not reported, nor is `nameof(T)`, which becomes the literal `"T"` for every closed type. A value `Key` is the member's name, so it is not checked.
- Does not report a registration with an `Instance`, whose open registration is not emitted anyway.
- Reported at the `Key` argument.

**Message format:** `Key '{Key}' of '{Implementation}' uses its type parameters, so it can differ per closed type and the open generic registration is not registered; only the closed types discovered at compile time are registered`

**Fix:** Use a key that does not depend on the type parameters, such as a member of a non-generic type, or register the closed types that are needed.

---

## SGIOC041 - Error - Usage - Member of an unbound generic type cannot be used

Report when a `Factory`, `Instance` or C# `Key` (`KeyType.Csharp`) of a registration attribute is given with `nameof()` of a member of an unbound generic type that the implementation type does not declare, such as `nameof(Factories<>.Create)`. Its path would name the type parameters of that type, which are not in scope in generated code and cannot be closed, so the generator ignores the registration in both outputs (see [Factory](../../Spec/Register.Factory.spec.md#instance-and-factory-precedence)).

**Analysis:**

- Checks `[IocRegister]` / `[IocRegister<T>]` / `[IocRegisterFor]` / `[IocRegisterFor<T>]` attributes, binding only the `nameof()` arguments that contain an unbound generic name (`Factories<>`), with the generator's own check (`FindNameofOfUnboundGenericType`).
- The own unbound type of an open generic implementation type, such as `Instance = nameof(Node<>.Default)` for `[IocRegisterFor(typeof(Node<>))]`, is closed like the implementation type and is not reported; for a closed implementation type, such as `typeof(Node<int>)`, it is reported. Another unbound generic type is reported also on a generic registration whose type parameters have the same names. A value `Key` is the member's name, so it is not checked. Open types nested in a generic type are ignored (`SGIOC038`).
- Reported once per attribute, at the first such `nameof()` argument. `SGIOC040` is not reported for the same attribute.
- `[IocRegisterDefaults]` is not checked.

**Message format:** `{Argument} '{Member}' is a member of the unbound generic type '{Type}', whose type arguments cannot be determined, so the registration is ignored; give its type arguments`

**Fix:** Give the type arguments, such as `nameof(Factories<int>.Create)`, or `nameof(Factories<T>.Create)` on an open generic `Node<T>`.
