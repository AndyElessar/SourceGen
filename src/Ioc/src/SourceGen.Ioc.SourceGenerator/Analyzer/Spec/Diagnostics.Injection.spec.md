# Injection Diagnostics

Diagnostics for member injection: [IocInject] usage, feature gating and InjectMembers. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC006 - Warning - Usage - Duplicated Attribute Usage

Report when `FromKeyedServicesAttribute` and `IocInjectAttribute`/`InjectAttribute` mark on one parameter. `FromKeyedServicesAttribute` takes precedence.

**Analysis:**

- Checks attributes on method parameters.
- Reports when a parameter has both `[FromKeyedServices]` and `[IocInject]`/`[Inject]`.

---

## SGIOC007 - Error - Usage - Invalid Attribute Usage

Report when `IocInjectAttribute`/`InjectAttribute` is mark on:

- static member
- member without public, internal, or protected internal accessibility
- property without setter or with private setter
- readonly field
- method with an unsupported return type
- method that is generic (has type parameters)
- method that is not an ordinary method (e.g., constructor, operator)
- explicit interface implementation

**Analysis:**

- Checks members (properties, fields, methods) marked with `[IocInject]` or `[Inject]`.
- Reports when:
  - Member is an explicit interface implementation, whose name cannot be assigned or called on the type (`it is an explicit interface implementation`).
  - Member is static.
  - Member is not `public`, `internal`, or `protected internal` (private, protected, or private protected members are rejected because generated code runs in a public static context).
  - Property has no setter or setter is private.
  - Field is readonly.
  - Method returns a type other than `void` or supported non-generic `Task`. `async void` is handled separately by `SGIOC028`.
  - Method is generic (has type parameters).
  - Method is not an ordinary method (i.e., constructors, operators, and other special methods are rejected).

### Method Return-Type Truth Table

|Method shape|`AsyncMethodInject` enabled|Diagnostic result|
|:-----------|:--------------------------|:----------------|
|`void Initialize(...)`|No/Yes|No return-type diagnostic.|
|`void Initialize(...)` declared as `async void`|No/Yes|`SGIOC028` MUST report. `SGIOC007` SHOULD NOT duplicate the return-type diagnostic.|
|`Task InitializeAsync(...)`|Yes|No `SGIOC007` return-type diagnostic.|
|`Task InitializeAsync(...)`|No|`SGIOC022` MUST report the disabled feature. `SGIOC007` MUST NOT report a duplicate return-type diagnostic.|
|`Task<T> InitializeAsync(...)`|No/Yes|`SGIOC007` MUST report.|
|`ValueTask InitializeAsync(...)` or `ValueTask<T> InitializeAsync(...)`|No/Yes|`SGIOC007` MUST report.|
|Any other non-void return type|No/Yes|`SGIOC007` MUST report.|

---

## SGIOC022 - Warning - Usage - Inject attribute ignored due to disabled feature

Report when a member has `[IocInject]`/`[Inject]` but its corresponding feature is disabled by `SourceGenIocFeatures`.

**Analysis:**

- Reads `SourceGenIocFeatures` from `AnalyzerConfigOptionsProvider.GlobalOptions` once during `CompilationStart`.
- Parses comma-separated feature names case-insensitively, trimming whitespace; missing/empty value defaults to `Register,Container,PropertyInject,MethodInject`.
- Checks members marked with `[IocInject]`/`[Inject]`:
  - `IPropertySymbol` requires `PropertyInject`
  - `IFieldSymbol` requires `FieldInject`
  - `IMethodSymbol` returning `void` requires `MethodInject`
  - `IMethodSymbol` returning non-generic `Task` requires `AsyncMethodInject`
- Reports when the required feature flag is not enabled.

### Feature Gate Mapping

|Member shape|Required feature|Notes|
|:-----------|:---------------|:----|
|Property|`PropertyInject`|Unchanged.|
|Field|`FieldInject`|Unchanged.|
|Method returning `void`|`MethodInject`|Covers synchronous method injection.|
|Method returning non-generic `Task`|`AsyncMethodInject`|`MethodInject` remains a project-level prerequisite when `AsyncMethodInject` is enabled; invalid combinations are reported by `SGIOC026`.|

**Message format:** `'{MemberName}' has [IocInject] but {FeatureName} feature is not enabled. Add '{FeatureName}' to <SourceGenIocFeatures> in your project file.`

---

## SGIOC023 - Error - Usage - Invalid InjectMembers element format

Report when an element in the `InjectMembers` array is not in a recognized format.

**Analysis:**

- Checks the `InjectMembers` named argument array on `[IocRegisterFor]` registration attributes.
- Validates each array element:
  - Must be one of:
    - A `nameof()` expression resolving to a valid member
    - A `new object[]` with exactly 2 or 3 elements where:
      - First element is a `nameof()` expression
      - Second element is a key (of any type)
      - Optional third element is a `KeyType` enum constant (`KeyType.Value` or `KeyType.Csharp`)
  - Arrays with more than 3 elements are rejected.
  - If 3 elements, the third must be a valid `KeyType` enum constant.
  - With `KeyType.Value` (the default), the key must be a non-null constant. The generator skips the element otherwise.
- Reports when an element does not match one of the recognized formats.

---

## SGIOC024 - Error - Usage - Member specified in InjectMembers cannot be injected

Report when a member resolved from `nameof()` in `InjectMembers` cannot be injected.

**Analysis:**

- Checks members specified via `nameof()` in the `InjectMembers` array, resolved on the registered type like the generator (see [Injection](../../Spec/Register.Injection.spec.md#injectmembers)): `nameof(Node<int>.Repo)` and `nameof(INode<int>.Repo)` check `Node<T>.Repo`.
- Reports when the member is:
  - not a member of the registered type (declared on a type it neither is, inherits, nor implements, or a default interface implementation): `it is not a member of 'Node<T>'`
  - an explicit interface implementation, whose name cannot be assigned or called on the registered type
  - hidden on the registered type by a member declared with `new` (see [Injection](../../Spec/Register.Injection.spec.md#injectmembers)), which the generated code would assign or call instead: `it is hidden by 'Derived.X'`
  - static
  - not `public`, `internal`, or `protected internal` (private, protected, or private protected members are rejected because generated registration code runs in a public static context)
  - property without setter or with private setter
  - readonly field
  - method that doesn't return `void` (or non-generic `Task` when `AsyncMethodInject` is enabled)
  - generic method
  - method that is not an ordinary method (i.e., constructors, operators, and other special methods are rejected)
- This validation reuses the same logic as SGIOC007 but specifically for members specified via `InjectMembers`.
- Not reported for an element when the registered type has an `[IocInject]` member (as enumerated by `GetInjectedMembers`) with the same name as the member the `nameof()` names, whatever that member is (hidden, static, `Task` without `AsyncMethodInject`, a member of another type, or a method group without an injectable overload): the generator's `MergeInjectionMembers` drops the element by name and injects the `[IocInject]` member. `SGIOC043` is not reported for it either.
- A `nameof()` of a method group with several overloads (see `SGIOC043`) reports when none of its overloads is injectable on the registered type: `none of its overloads can be injected`, or `it is not a member of 'Derived'` when the registered type has none of them. It does not report the reason of a single overload, which would depend on the order of the candidates.

---

## SGIOC028 - Warning - Usage - `async void` injection method cannot be awaited

Report when an `[IocInject]`/`[Inject]` method is declared as `async void`.

**Analysis:**

- Checks methods marked with `[IocInject]` or `[Inject]`.
- If a method is declared `async` and returns `void`, the analyzer MUST report `SGIOC028` because the generator cannot await it.
- `SGIOC028` SHOULD be the user-facing diagnostic for this case; `SGIOC007` SHOULD NOT add a duplicate return-type diagnostic for the same method.

**Message format:** `[IocInject] method '{MethodName}' is 'async void' which cannot be awaited. Change return type to 'Task'.`

---

## SGIOC043 - Error - Usage - InjectMembers names a method with several injectable overloads

Report when a `nameof()` in `InjectMembers` names a method group with more than one injectable overload on the registered type. `nameof()` cannot choose an overload, so the generator injects none of them (see [Injection](../../Spec/Register.Injection.spec.md#injectmembers)) rather than one chosen by the order of the candidates.

**Analysis:**

- Checks `nameof()` elements of `InjectMembers`, keyed or not, that resolve to a method group: `GetSymbolInfo` returns no symbol and more than one candidate. `nameof(Derived.Init)` names the overloads declared on `Derived` and on its base classes.
- Counts the injectable overloads with the generator's `GetInjectableMember`: resolved on the registered type (`AsMemberOf`), not hidden there (`GetHidingMember`), and injectable (`IsInjectableMember`: an instance, ordinary, non-generic method returning `void` or non-generic `Task`, `public`, `internal`, or `protected internal`). A `Task` overload counts as a candidate for this check whether or not `AsyncMethodInject` is enabled, like the generator; injecting it still requires `AsyncMethodInject` (see below).
- Reports when more than one overload is injectable. Not reported when the registered type has an `[IocInject]` member with the same name, which takes precedence over the element (the generator injects the `[IocInject]` member).
- When exactly one overload is injectable, it is checked like a single member: nothing is reported, except `SGIOC024` for a `Task` overload when `AsyncMethodInject` is not enabled (`method does not return void`), which the generator does not inject. When none is injectable, `SGIOC024` reports it.
- Reported at the `nameof()` expression, from the attribute syntax node action, so the descriptor is not tagged `CompilationEnd`.
- `ContainerAnalyzer` resolves the overloads the same way, so the dependencies of an ambiguous method group are not checked (`SGIOC018`).

**Message format:** `Method '{MethodName}' specified in InjectMembers has more than one injectable overload on '{RegisteredType}', so none of them is injected; mark the overload to inject with [IocInject] instead, or give it a unique name`
