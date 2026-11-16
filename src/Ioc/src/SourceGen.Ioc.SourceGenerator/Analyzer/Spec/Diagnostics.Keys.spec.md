# Key Diagnostics

Diagnostics for keyed services: [ServiceKey] parameters and injected KeyValuePair key types. Index: [Ioc Analyzer](SPEC.spec.md).

Format: ID - Level - Category - Description

## SGIOC013 - Error - Usage - Key type is unmatched

Report when `ServiceKeyAttribute` is mark on parameter, but the key type is not matched with the registered key type from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Collects each service registration's Key type (from generic parameter or `KeyType` property of `[IoCRegister]` or `[IoCRegisterFor]`).
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`.
- Reports when the parameter type is not compatible with the registered Key type.
- Exception: Skips type checking when `KeyType` is `Csharp`.

---

## SGIOC014 - Warning - Usage - Key does not exists

Report when `ServiceKeyAttribute` is mark on parameter, but there is no specified `Key` from `IoCRegisterAttribute` or `IoCRegisterForAttribute`.

**Analysis:**

- Checks whether the service registration has a `Key` specified.
- Checks constructor and `[Inject]` method parameters marked with `[ServiceKey]`.
- Checks the parameters marked with `[ServiceKey]` of the registration's own `Factory` method (the generator passes `default` for them). The `Factory` of `IocRegisterDefaultsAttribute` is not checked, because it is shared by keyed and non-keyed registrations; the diagnostic is reported on the `Factory` argument.
- Reports when `[ServiceKey]` is used but the registration has no Key specified.

---

## SGIOC015 - Warning - Usage - KeyValuePair's Key type is unmatched

Report when a `KeyValuePair<K, V>` is injected but the registered key type of `V` does not match `K`.

**Analysis:**

- Runs during CompilationEnd phase after all services are collected.
- For each registered service, scans constructor parameters and `[IocInject]`/`[Inject]` members (method parameters, properties, fields).
- Detects parameters/members whose type contains key-value semantics:
  - `KeyValuePair<K, V>` directly
  - `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`
  - Collection types wrapping `KeyValuePair<K, V>`: `IEnumerable<KeyValuePair<K, V>>`, `IReadOnlyCollection<KeyValuePair<K, V>>`, `ICollection<KeyValuePair<K, V>>`, `IReadOnlyList<KeyValuePair<K, V>>`, `IList<KeyValuePair<K, V>>`, `KeyValuePair<K, V>[]`
- For each detected key-value parameter with key type `K` and value type `V`:
  - Finds the registered services with `HasKey = true` whose `Type` implements interface `V`, has `V` as a base class, or equals `V`, using an index of keyed registrations by every type they are assignable to (built once per compilation on first use).
  - If at least one keyed registration for `V` exists but **none** have a key type compatible with `K`, reports the diagnostic.
- Key type compatibility rules:
  - `K = object` is always compatible with any key type (no diagnostic).
  - If a registration uses `KeyType = Csharp` with `nameof()`, key type is resolved from the referenced symbol and checked for compatibility. A `nameof()` of a method group with several overloads cannot choose one, so its key type is unknown (`null`, no diagnostic) rather than the return type of whichever overload comes first.
  - If a registration uses `KeyType = Csharp` with a string literal key (not `nameof()`), `KeyTypeSymbol` is `null` and compatibility analysis is skipped (no diagnostic).
  - Otherwise, reports if the registration's `KeyTypeSymbol` is not assignable to `K`.
- Skips parameters that have `[FromKeyedServices]` attribute (those resolve specific keyed services, not KVP aggregation).

**Message format:** `KeyValuePair parameter '{0}' has key type '{1}' but no keyed service for '{2}' has a matching key type`
