# Key Diagnostics

Diagnostics for keyed services: [ServiceKey] parameters and injected KeyValuePair key types. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC013 - Error - Usage - Key type is unmatched

Report when `ServiceKeyAttribute` is mark on parameter, but the key type is not matched with the registered key type from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Collects each service registration's Key type (from generic parameter or `KeyType` property of `[IoCRegister]` or `[IoCRegisterFor]`).
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`. The constructor of a `Factory` or `Instance` registration and the `[Inject]` methods of an `Instance` registration are not checked, since the container does not call them, unless a requested `Func` with input parameters constructs the registration (see [Analyzed Registrations](Diagnostics.Dependencies.spec.md#analyzed-registrations)).
- Checks the parameters against each registration of the implementation type that calls the method, with that registration's own Key: the constructor against the constructor-activated registrations (and the `Instance` and `Factory` registrations a requested `Func` with input parameters constructs, see [Analyzed Registrations](Diagnostics.Dependencies.spec.md#analyzed-registrations)), the `[Inject]` methods against the registrations without `Instance` (and those that do not return `Task` also against the registrations such a `Func` constructs).
- A `Func` with input parameters passes each argument to the first unused parameter of the same runtime type, in order: the constructor parameters, the injected properties, the injected fields (like the generator, which sorts the injected members by stage), then the parameters of the `[Inject]` methods that do not return `Task`, whatever the features (see [Register Injection](../../Spec/Register.Injection.spec.md)). A `[ServiceKey]` parameter that receives an argument does not receive the key, so it is checked against a registration only constructed by such `Func`s when one of the requested `Func` types for the registration leaves the parameter without an argument. For example, an `Instance` registration of `Foo([ServiceKey] int key)` with `Key = "a"` requested through `Func<int, Foo>` is not reported (the generator emits `new Foo(arg0)`), while `Func<string, Foo>` is.
- Reports when the parameter type is not compatible with the Key type of such a registration, once per incompatible Key type.
- Exception: Skips type checking when `KeyType` is `Csharp`.

---

## SGIOC014 - Warning - Usage - Key does not exists

Report when `ServiceKeyAttribute` is mark on parameter, but there is no specified `Key` from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Checks whether the service registration has a `Key` specified.
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`. The constructor of a `Factory` or `Instance` registration and the `[Inject]` methods of an `Instance` registration are not checked, since the container does not call them, unless a requested `Func` with input parameters constructs the registration (see [Analyzed Registrations](Diagnostics.Dependencies.spec.md#analyzed-registrations)).
- Checks the parameters marked with `[ServiceKey]` of the registration's own `Factory` method (the generator passes `default` for them). The `Factory` of `IocRegisterDefaultsAttribute` is not checked, because it is shared by keyed and non-keyed registrations; the diagnostic is reported on the `Factory` argument.
- Reports when `[ServiceKey]` is used but a registration that calls the method has no Key specified (each registration of the implementation type is checked, like SGIOC013), once per parameter. For example, a keyed `Instance` registration followed by a constructor-activated registration without a Key reports the constructor's `[ServiceKey]` parameter. A registration only constructed by `Func`s with input parameters is checked only when one of them leaves the parameter without an argument, like SGIOC013.

---

## SGIOC015 - Warning - Usage - KeyValuePair's Key type is unmatched

Report when a `KeyValuePair<K, V>` is injected but the registered key type of `V` does not match `K`.

**Analysis:**

- Runs during CompilationEnd phase after all services are collected.
- For each registered service with a constructor-activated registration (see [Analyzed Registrations](Diagnostics.Dependencies.spec.md#analyzed-registrations)), scans constructor parameters and `[IocInject]`/`[Inject]` members (method parameters, properties, fields).
- Detects parameters/members whose type contains key-value semantics:
  - `KeyValuePair<K, V>` directly
  - `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`
  - Collection types wrapping `KeyValuePair<K, V>`: `IEnumerable<KeyValuePair<K, V>>`, `IReadOnlyCollection<KeyValuePair<K, V>>`, `ICollection<KeyValuePair<K, V>>`, `IReadOnlyList<KeyValuePair<K, V>>`, `IList<KeyValuePair<K, V>>`, `KeyValuePair<K, V>[]`
- For each detected key-value parameter with key type `K` and value type `V`:
  - Finds every keyed registration (each registration with a Key, not only the first registration of its implementation type) whose implementation type implements interface `V`, has `V` as a base class, or equals `V`, using an index of keyed registrations by every type they are assignable to (built once per compilation on first use).
  - If at least one keyed registration for `V` exists but **none** have a key type compatible with `K`, reports the diagnostic.
- Key type compatibility rules:
  - `K = object` is always compatible with any key type (no diagnostic).
  - If a registration uses `KeyType = Csharp` with `nameof()`, key type is resolved from the referenced symbol and checked for compatibility. A `nameof()` of a method group with several overloads cannot choose one, so its key type is unknown (`null`, no diagnostic) rather than the return type of whichever overload comes first.
  - If a registration uses `KeyType = Csharp` with a string literal key (not `nameof()`), `KeyTypeSymbol` is `null` and compatibility analysis is skipped (no diagnostic).
  - Otherwise, reports if the registration's `KeyTypeSymbol` is not assignable to `K`.
- Skips parameters that have `[FromKeyedServices]` attribute (those resolve specific keyed services, not KVP aggregation).

**Message format:** `KeyValuePair parameter '{0}' has key type '{1}' but no keyed service for '{2}' has a matching key type`
