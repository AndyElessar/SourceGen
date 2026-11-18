# Injection (Field / Property / Method / Constructor)

## Overview

Full support for member and constructor injection via `[IocInject]` and `[Inject]` attributes. The generator handles factory method generation when necessary and supports complex scenarios including optional parameters, keyed services, and wrapper types.

## Feature: Injection (Field / Property / Method / Constructor)

When a class marked with `IocRegisterAttribute`, `IocRegisterForAttribute` and its members or parameters marked with `IocInjectAttribute` or `InjectAttribute`, generate the necessary code to handle the injection.

Only check with name `IocInjectAttribute` or `InjectAttribute`, so user can use other library's attribute, like `Microsoft.AspNetCore.Components.InjectAttribute`, make sure the Key interpret logic is compatible with `Microsoft.AspNetCore.Components.InjectAttribute`.

### Factory Method Generation Requirements

**Important**: Factory method registration is only generated when necessary. The following cases require factory method:

- Constructor parameter has `[IocInject]` attribute (SourceGen.Ioc-specific, MS.E.DI cannot handle)
- Field/Property/Method has `[IocInject]` attribute
- Decorator pattern is used
- Factory or Instance is specified
- The selected constructor is not public, such as an `internal` one (MS.E.DI calls public constructors only, so it would call another constructor or fail). Constructors are selected among those accessible from the compiled assembly: public ones, and internal ones declared in it or in an assembly that gives it access with `InternalsVisibleTo`; for an open generic implementation, the open registration is not emitted and each closed registration constructs the closed type (see [Generic Registration](Register.Generics.spec.md))

The following cases are handled natively by MS.E.DI and do **NOT** require factory method:

- `[FromKeyedServices]` attribute on constructor parameters
- `[ServiceKey]` attribute on constructor parameters
- `IServiceProvider` parameter in constructor

### Method Parameter Analysis

Method parameters marked with `[IocInject]` must be analyzed using the same logic as constructor parameters:

- `[ServiceKey]` attribute: Inject the registration key (or default if non-keyed)
- `[FromKeyedServices]` attribute: Use keyed service resolution
- `[IocInject]` attribute with Key: Use keyed service resolution
- `IServiceProvider` type: Pass the service provider directly
- Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
- Wrapper types: Generate inline wrapper construction (see **Wrapper Type Resolution** below)
- Parameters with default values: See rule for optional parameters below

### Property/Field Analysis

Properties and fields marked with `[IocInject]` or `[Inject]` are analyzed for injection:

- Only non-static properties with a setter (`set` or `init`) are eligible
- Only non-static, non-readonly fields are eligible
- `[IocInject]` attribute with Key: Use keyed service resolution (`GetRequiredKeyedService<T>(key)` or `GetKeyedService<T>(key)`)
- `IServiceProvider` type: Pass the service provider directly
- Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
- Wrapper types: Generate inline wrapper construction (see **Wrapper Type Resolution** below)
- Nullable annotation (`T?`): Use `GetService<T>()` (non-required) instead of `GetRequiredService<T>()`
- Property/Field with initializer: Treated as having a default value, use optional resolution. An initializer of `null`, `null!`, `default` or `default!` marks a required member, not a default value.

### InjectMembers

The `InjectMembers` property of a registration attribute lists members to inject like `[IocInject]` members (`[IocInject]` on the member takes precedence). `nameof()` only names a member: the member MUST be resolved as a member of the registered type (or of one of its base classes), whatever type arguments the `nameof()` expression uses. For an open generic registration, `InjectMembers = [nameof(Node<int>.Repo)]` injects `Node<T>.Repo`, so `Node<string>` injects `IRepo<string>`, not `IRepo<int>`; a member inherited from `NodeBase<T>` uses the type arguments `Node<T>` gives its base class. An interface member, such as `nameof(INode<int>.Repo)`, MUST be resolved to its implementation on the registered type (`Node<T>.Repo`). A member that the registered type does not have (declared on an unrelated type, or only as a default interface implementation), or that it implements explicitly, cannot be assigned or called on it and MUST NOT be injected (the analyzer reports `SGIOC024`). The generated code names the member on the registered type, so a member hidden there MUST NOT be injected either (the analyzer reports `SGIOC024`): with `Derived : Base` declaring `public new IBar? X`, `nameof(Base.X)`, or an interface member implemented by `Base.X`, is not injected, as `new Derived() { X = ... }` assigns `Derived.X`. A member is hidden by a member with the same name declared on the registered type, or on a base class between it and the declaring type of the member, that is not an override and that the generated code can access: `public`, or `internal` or `protected internal` and declared in the compiled assembly or in an assembly that gives it access (`InternalsVisibleTo`), so an `internal new` member of a referenced assembly does not hide the member; a method is only hidden by a member that is not a method or by a method with the same parameter types. When the registered type has an `[IocInject]` member with the same name (such as the hiding member itself), that member takes precedence over the element, which is dropped like any element named by an `[IocInject]` member: the `[IocInject]` member is injected and `SGIOC024` MUST NOT be reported for the hidden member. The element is dropped by name whatever member it names (also a static member, a member of another type, or a method group without an injectable overload), so the analyzer MUST NOT report `SGIOC024` or `SGIOC043` for it.

`nameof()` of a method names its method group, which cannot choose an overload: `nameof(Derived.Init)` names both `Base.Init(IFoo)` and `Derived.Init(IBar)` (overloads declared on the type named by `nameof()` and its base classes), as does `nameof(Derived.Init)` with two `Init` overloads on `Derived`. Each overload is resolved on the registered type as above, and is injectable when the registered type has it, does not hide it, and it is an injectable member (an instance, ordinary, non-generic method returning `void` or non-generic `Task` that is `public`, `internal`, or `protected internal`; a `Task` method counts as a candidate whether or not `AsyncMethodInject` is enabled, so a `void` and a `Task` overload are ambiguous either way). When exactly one overload is injectable, it MUST be injected like a single member (a `Task` overload only when `AsyncMethodInject` is enabled; otherwise it is not injected and the analyzer reports `SGIOC024`), so `nameof(Derived.Init)` with `Init(IFoo)` and `static Init(IBar)` injects `Init(IFoo)`. When more than one overload is injectable, the element is ambiguous and MUST NOT inject any of them; the overload MUST NOT be chosen by the order of the candidates (the analyzer reports `SGIOC043`, unless the registered type has an `[IocInject]` member with the same name, which takes precedence over the element as above; `[IocInject]` is only read from the members the registered type declares, so on a base class overload it does not resolve the ambiguity, and `nameof(Base.Init)` names that overload instead). When no overload is injectable, nothing is injected (the analyzer reports `SGIOC024`). `nameof(Base.Init)` names only the overloads declared on `Base` and its base classes, so it is not ambiguous when `Base` declares a single `Init`.

A method declared on a base class that is not hidden can still lose overload resolution on the registered type: C# removes the methods of a base type from the candidates when a more derived type has an applicable method with the same name, so with `Derived : Base` declaring `public void Init(object o)` (or `Init(IFoo foo, IBar? bar = null)`, or `Init<T>(T x)`), `s0.Init(s0_m0)` calls `Derived.Init`, not the injected `Base.Init(IFoo)`. Overload resolution treats an override as declared on the type of the original virtual method, so the same happens to an injected override with an overload next to it (`Derived` declaring `override Init(IFoo)` and `Init(object)`). The declaring type of an injected method is therefore the type that declares the original (least-overridden) method. When the registered type, or a base class between it and that declaring type, declares a method with the same name that is not an override (static methods included) and that the generated code can access (as for hiding above), the generated code MUST call the method through its declaring type: `((global::Base)s0).Init(s0_m0)`. A virtual method called this way still dispatches to its override. The declaring type is always accessible to the generated code, because a base class is at least as accessible as the class deriving from it (CS0060). Otherwise the call MUST stay `s0.Init(...)`. This applies to `InjectMembers` elements (keyed or not) and to `[IocInject]` methods of implementation types and decorators (including decorators of imported modules), in every injected method call of the Register and Container output, synchronous and async, including `Func<..., T>` with input parameters; for an open generic registration, the declaring type uses the type arguments of each closed type (`((global::Base<string>)s0).Init(...)`).

### Optional Parameter Handling (constructor and method parameters)

When a parameter has a default value, it MUST receive the resolved service, or its default value when the service is not registered. The Register and Container output MUST compile for any parameter type:

|Parameter|Resolution|
|---|---|
|Default value `null` and nullable type (`IDependency? dep = null`, `int? count = null`)|`sp.GetService<T>() ?? default`|
|Any other default value (`int timeout = 30`, `string name = "x"`, `Mode mode = Mode.Safe`)|`sp.GetService(typeof(T)) switch { T service => service, _ => defaultValue }`|
|Non-nullable type defaulting to `null` or `default` (`Settings settings = default`)|`sp.GetService(typeof(T)) switch { T service => service, _ => default(T)! }`|

- The untyped `GetService(typeof(T))` form is required because `GetService<T>()` returns `default` for an unregistered value type, which cannot be told apart from a registered value. The `T` arm types the fallback, so target-typed initializers such as `new()` and `[]` create a `T`, and the fallback is only evaluated when the service is not registered. A nullable value type `T?` is matched by its underlying type in the pattern.
- Keyed parameters use `GetKeyedService<T>(key)` / `GetKeyedService(typeof(T), key)` in the same way.
- An enum default value MUST be cast to the enum type (`(Mode)(1)`), because the parameter symbol stores its underlying constant.
- The default value is the parameter's own declared default value (a constant). Properties and fields with a non-null initializer use the same form with the initializer expression, copied as written.

### Wrapper Type Resolution

When a parameter or member type is a recognized wrapper type, the generator resolves it differently depending on whether it is a **direct wrapper** (inner type is a plain service) or a **nested wrapper** (inner type is itself a wrapper).

**Direct `Lazy<T>` / `Func<...>`**: Standalone registrations are emitted so wrapper services can be resolved directly from DI. Lifetimes and tags are inherited from the matched inner service `T`.

The wrapper demand (`Lazy<T>`, `Func<...>`, `KeyValuePair<K, V>`, dictionaries and collections) MUST be scanned from every dependency of a registration, the same way for the Register and Container outputs (`Grouping/WrapperDemand.cs`): its factory's parameters (or the implementation type's constructor parameters), its injection members, and its decorators' constructor parameters and injection members.

One `Lazy<T>` / parameterless `Func<T>` registration is emitted per registration of `T`, so `IEnumerable<Lazy<T>>` contains one wrapper per implementation. Each MUST resolve that registration's instance, the same instance as resolving `T`:

|Registration of `T`|Wrapper registration|Resolves|
|---|---|---|
|Non-keyed|`services.AddXXX<Lazy<T>>(sp => ...)`|`sp.GetRequiredService<Impl>()`|
|Non-keyed, with its own instance|`services.AddXXX<Lazy<T>>(sp => ...)`|`sp.GetRequiredKeyedService<T>("SourceGen.Ioc.Decorated:{T}\|{Impl}")`, the hidden key of the service type's own instance (see [Decorators](Register.Decorators.spec.md) and [Generic factory](Register.Generics.spec.md))|
|Keyed|`services.AddKeyedXXX<Lazy<T>>(key, (sp, key) => ...)`|`sp.GetRequiredKeyedService<Impl>(key)`|
|Keyed, with its own instance|`services.AddKeyedXXX<Lazy<T>>(key, (sp, key) => ...)`|`sp.GetRequiredKeyedService<T>(key)`|

A service type has its own instance when decorators wrap it, or a factory it does not share with the implementation type creates it: a generic factory, or a factory of an async-init registration. The instance of an `Instance` registration is never decorated, so its `Lazy<T>` / parameterless `Func<T>` wrappers resolve the implementation type.

A keyed registration MUST NOT emit a non-keyed wrapper registration.

`Func<T1, ..., TReturn>` with input parameters is a factory: each call MUST construct a **new** implementation instance from the arguments, also for singleton and scoped registrations, because the arguments may differ between calls (unlike the parameterless `Func<T>`, which follows the lifetime). The call MUST then wrap the instance in the registration's decorators, also new per call; the arguments are only matched to the implementation, and decorator dependencies are resolved from DI. A keyed registration MUST be registered as a keyed Func (`services.AddKeyedXXX<Func<T1,...,TReturn>>(key, (sp, key) => ...)`), resolved through `[FromKeyedServices(key)]`, and the implementation's `[ServiceKey]` parameters MUST receive the registration key.

| Wrapper Type | Standalone Registration | Consumer Resolution |
| --- | --- | --- |
| `Lazy<T>` | `services.AddXXX<Lazy<T>>(sp => new Lazy<T>(() => sp.GetRequiredService<Impl>(), LazyThreadSafetyMode.ExecutionAndPublication))` (resolution as in the table above) | `sp.GetRequiredService<Lazy<T>>()` |
| `Func<T>` | `services.AddXXX<Func<T>>(sp => new Func<T>(() => sp.GetRequiredService<Impl>()))` (resolution as in the table above) | `sp.GetRequiredService<Func<T>>()` |
| `Func<T1, ..., TReturn>` | `services.AddXXX<Func<T1,...,TReturn>>(sp => new Func<T1,...,TReturn>((arg0,...) => ...))` | `sp.GetRequiredService<Func<T1,...,TReturn>>()` |
| `KeyValuePair<K, V>` | `services.Add(new ServiceDescriptor(typeof(KVP<K,V>), sp => ..., lifetime))` | (used by Dictionary resolution) |
| `IDictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |
| `IReadOnlyDictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |
| `Dictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |

- **Nullable wrapper types**: Use `GetService<T>()` (optional) instead of `GetRequiredService<T>()`
- **Nested wrappers**: Non-collection outer wrappers (`Lazy<T>`, `Func<T>`) are recursively resolved to arbitrary depth via inline construction. Collection outer wrappers support at most **1 level of inner wrapping** (2 levels total). Inner wrapper types are constructed **inline** (no standalone registration).
  - `Lazy<Func<T>>` → `new Lazy<Func<T>>(() => new Func<T>(() => sp.GetRequiredService<T>()))`
  - `Func<Lazy<T>>` → `new Func<Lazy<T>>(() => new Lazy<T>(() => sp.GetRequiredService<T>()))`
  - `Lazy<IEnumerable<T>>` → `new Lazy<IEnumerable<T>>(() => sp.GetServices<T>())`
  - `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` — Consumers resolve via `sp.GetServices<Lazy<T>>()` (uses standalone registrations)
  - Collection outer wrapper with 3+ levels (e.g., `IEnumerable<Lazy<Func<T>>>`) is **not** supported. No wrapper registrations are emitted; the consumer is registered with a plain `AddXXX<Consumer, Consumer>()` call and the parameter is left to MS.DI runtime resolution.
  - `ValueTask<T>` is **not** a recognized wrapper type in any context. Only `Task<T>` is supported for async-init wrapping. When used as a partial accessor return type: if the target service uses async-init, `SGIOC029` is reported; otherwise `SGIOC021` is reported.
- **Multi-parameter Func matching**: For `Func<T1,...,TN-1,TReturn>`, constructor parameters and injectable members are matched by runtime type (without tuple element names) against Func inputs using first-unused semantics. Unmatched dependencies are resolved from DI.
- **Multi-parameter Func of an async-init registration**: Not supported. A `Func` with input parameters constructs the instance synchronously and cannot await its async inject methods, so no `Func<T1,...,TReturn>` registration MUST be emitted for an async-init registration (the implementation type or a decorator applied to the service type has async inject methods); the Container resolves it from the fallback provider instead. The same applies to a service type of an `Instance` registration that a decorator with async inject methods is applied to: the registration is not async-init, since its instance is not decorated, but such a `Func` would construct a new instance and decorate it without calling those methods (see [Decorators](Register.Decorators.spec.md)). The implementation type of that registration is not decorated, so its `Func` with input parameters is still emitted. The analyzer reports such a dependency with `SGIOC030`.
- **Nested multi-parameter Func**: Not supported (e.g., `Lazy<Func<string, IService>>` with input parameters).
- **Open generic dependencies**: Wrapper inner types that reference closed generics trigger automatic closed generic registration (e.g., `Lazy<IHandler<TestEntity>>` → registers `Handler<TestEntity>`)
- **Factory method requirement**: Only **nested** wrapper types and **nullable** direct Lazy/Func types trigger factory method registration. Direct non-nullable Lazy/Func types resolve from their standalone registrations.
- **Tag-awareness**: Standalone `Lazy<T>`/`Func<T>`/`KeyValuePair<K, T>` registrations inherit the tags of the inner service `T` and are emitted within the same tag conditional block.
- **Tuple element names**: Standalone registrations are emitted once for each runtime type, so dependencies that differ only in tuple element names share one registration (see [Container Injection](Container.Injection.spec.md#overview)). The spelling the registration is written with depends on the wrapper. This only changes the text of the generated code: the runtime type is the same, and the same input always generates the same output.

  | Wrapper Type | Tuple element names written | Depends on the order of the dependencies |
  | --- | --- | --- |
  | `Lazy<T>` | Those of the registered service type | No |
  | `Func<T>` / `Func<T1, ..., TReturn>` | Those of the first scanned dependency | Yes |
  | `KeyValuePair<K, V>` | Those of the first scanned dependency with the same key type (`KeyValuePair`, dictionary or collection of pairs) | Yes |

  For example, with a registration of `IHandler<(Entity X, string Y)>`, a consumer of `Func<IHandler<(Entity A, string B)>>` declared before a consumer of `Func<IHandler<(Entity, string)>>` registers `Func<IHandler<(Entity A, string B)>>`; declared after it, `Func<IHandler<(Entity, string)>>` is registered instead. `Lazy<IHandler<(Entity X, string Y)>>` is registered in both cases.

  An open generic registration is closed once for each runtime type (and set of decorators) too, with the first spelling of its dependencies it is closed for (see [Generic Registration](Register.Generics.spec.md#feature-1-closed-generic-registration-from-open-generic)), and its `Lazy<T>` / `Func<T>` registrations are emitted once. With `Handler<T> : IHandler<T>` registered for `IHandler<>`, a consumer of `Lazy<IHandler<(Entity A, string B)>>` declared before a consumer of `Lazy<IHandler<(Entity, string)>>` registers `Handler<(Entity A, string B)>`, `IHandler<(Entity A, string B)>` and `Lazy<IHandler<(Entity A, string B)>>` once each.

#### Explicit Wrapper Registrations

A wrapper or collection type can itself be registered as a service type (`[IocRegisterFor<HandlerDictionary>(ServiceTypes = [typeof(IReadOnlyDictionary<string, IHandler>)], Instance = ...)]`, `[IocRegisterFor<Lazy<IHandler>>(Instance = ...)]`). `MS.DI` resolves a constructor parameter of that type as the registration, so the injected value MUST be the instance `GetService` returns, not a wrapper built by the generated code; otherwise a consumer silently gets a second, different object. The decision is made at compile time: the dependency resolves through `MS.DI` instead of being built. This matches the Container output (see [Container Collections](Container.Collections.spec.md#explicit-collection-registrations)).

|Rule|Required behavior|
|:---|:---|
|Types|`Lazy<T>`, `Func<...>`, `KeyValuePair<K, V>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`, and the collection types `IReadOnlyCollection<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `IList<T>`, `T[]`. `IEnumerable<T>` keeps `GetServices<T>()`, which already returns its explicit registration in `MS.DI`.|
|Match|The explicit registration MUST have the dependency's type (compared as a runtime type, see tuple element names above) and key (compared as written). A dependency under `KeyedService.AnyKey` and a registration under `KeyedService.AnyKey` never match, since `MS.DI` resolves no single service with that key.|
|Resolution|`sp.GetRequiredService<TWrapper>()` / `sp.GetRequiredKeyedService<TWrapper>(key)`, or `GetService` / `GetKeyedService` for an optional (nullable) dependency. It applies to constructor, method, factory and decorator parameters, injected properties and fields, and inside another wrapper: `Lazy<Func<T>>` registered explicitly is resolved as is; otherwise `new Lazy<Func<T>>(() => ...)` resolves an explicit `Func<T>`, and `Lazy<IReadOnlyList<T>>` an explicit `IReadOnlyList<T>`.|
|Async-init|An async-init registration is registered as `Task<TWrapper>` only, so it MUST NOT be used: the dependency keeps the generated wrapper. `Task<T>` itself keeps its async-init resolution (see [`WrapperKind.Task` Detection and Resolution](#wrapperkindtask-detection-and-resolution)). An `Instance` registration is never async-init: the instance is registered as is, without calling its inject methods (see [Classification Rules](#classification-rules)).|
|Tags|One call registers every tag group with a passed tag (an untagged registration only when no tags are passed, see [Tags](Register.Tags.spec.md)), so for the consumer's tags the explicit registrations of the type and key are either always, sometimes, or never registered with it. **Always** (an untagged one for an untagged consumer, or tagged ones whose tags together include every tag of the consumer): the explicit registration MUST be resolved as above. **Sometimes** (a tagged consumer, and tagged explicit registrations that do not cover all its tags): the dependency MUST resolve `sp.GetService<TWrapper>() ?? <built wrapper>` (`GetKeyedService` for a keyed one), so it gets the explicit registration exactly when it is registered; the generated code never registers these types itself, so a registration found at runtime is an explicit one. **Never** (an untagged consumer and tagged explicit registrations, or the reverse): the wrapper is built. With the explicit registration tagged `["A"]`, a consumer tagged `["A"]` resolves it, a consumer tagged `["A", "C"]` falls back at runtime, and an untagged consumer builds the wrapper.|
|Generated `Lazy<T>` / `Func<...>`|A standalone `Lazy<T>` / `Func<...>` registration of a registration of `T` MUST NOT be registered while an explicit `Lazy<T>` / `Func<...>` under the same key is. Tag groups are written in tag order with the generated registrations last in each group, so a generated wrapper registered in the same call would win in `MS.DI` (last wins), to `GetService` and to the consumers. With the same three cases for the tags of the registration of `T`: **always**, the generated registration MUST NOT be emitted; **sometimes**, it MUST be written inside `if (!(tags.Contains("A") \|\| ...))` with every tag of the explicit registrations, so it is only registered when none of them is passed; **never**, it is emitted as usual. Like `MS.DI`, `IEnumerable<Lazy<T>>` then contains the explicit registrations only.|
|Compilation scope|Only the registrations of the compilation that generates the Register output are known. A wrapper type registered by hand in the `IServiceCollection`, or by another assembly's Register output, is not seen: the dependency is built by the generated code as before, and a generated `Lazy<T>` / `Func<...>` registration written after such a registration still wins.|

`KeyValuePair<K, V>` is a value type, which the generic `AddXXX<T>` methods cannot register (`class` constraint), so an explicit `KeyValuePair<K, V>` registration does not compile in the Register output today.

```csharp
#region Define:
public sealed class HandlerDictionary : Dictionary<string, IHandler>
{
    public static readonly HandlerDictionary Default = new();
}

[IocRegisterFor<HandlerDictionary>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IHandler>)], Instance = "global::MyApp.HandlerDictionary.Default")]
[IocRegisterFor<Lazy<IHandler>>(ServiceLifetime.Singleton, Instance = "global::MyApp.Wrappers.Lazy")]
public static class Registrations;

[IocRegister]
public sealed class Consumer(IReadOnlyDictionary<string, IHandler> handlers, Lazy<IHandler> lazy, IDictionary<string, IHandler> other);
#endregion

#region Generate:
services.AddTransient<Consumer>((IServiceProvider sp) =>
{
    var p0 = sp.GetRequiredService<IReadOnlyDictionary<string, IHandler>>();     // HandlerDictionary.Default
    var p1 = sp.GetRequiredService<Lazy<IHandler>>();                            // Wrappers.Lazy: no generated Lazy<IHandler> is registered
    var p2 = sp.GetServices<KeyValuePair<string, IHandler>>().ToDictionary();   // not registered explicitly: built
    var s0 = new Consumer(p0, p1, p2);
    return s0;
});
#endregion
```

### Async Method Injection

`AsyncMethodInject` extends post-construction member injection with awaited initialization methods. The generator MUST treat async method injection as a distinct injection stage that runs after all synchronous injection steps.

#### Classification Rules

|Condition|Required behavior|
|:--------|:----------------|
|Method has `[IocInject]`/`[Inject]`, is an ordinary instance method, returns non-generic `Task`, and `AsyncMethodInject` is enabled|MUST classify the member as `InjectionMemberType.AsyncMethod`.|
|Method has `[IocInject]`/`[Inject]` and returns `void`|MUST continue to classify the member as `InjectionMemberType.Method`.|
|Method is declared on a decorator|MUST classify the member by the same rules. An async method of a decorator makes the decorated service type async-init (see [Async Decorators](Register.Decorators.spec.md#async-decorators)); otherwise its `Task` would be discarded without being awaited.|
|Method returns `Task<T>`|MUST NOT classify the member as async method injection. `Task<T>` is not a supported injection-method return type.|
|Method returns `ValueTask` or `ValueTask<T>`|MUST NOT classify the member as async method injection.|
|Method returns `Task` but `AsyncMethodInject` is disabled|The generator MUST treat the member as feature-gated; the analyzer owns the user-facing warning via `SGIOC022`.|
|The registration has an `Instance`|The instance is registered as is, without calling its inject methods, so the registration MUST NOT be async-init in the Register and Container outputs: `T` resolves the instance and `Task<T>` wraps it with `Task.FromResult`. Otherwise `Task<T>` would resolve a `Task<T>` registration that is never generated (and the Container output would not compile).|

#### Ordering Contract

The generator MUST emit member injection in the following fixed stage order. Source declaration order applies **within** each stage.

|Stage|Members|Emission rule|
|:----|:------|:------------|
|1|Properties|MUST be assigned first, in source declaration order.|
|2|Fields|MUST be assigned second, in source declaration order.|
|3|Synchronous methods|MUST be invoked third, in source declaration order.|
|4|Async methods|MUST be awaited last, in source declaration order.|

```mermaid
flowchart LR
    A[Construct instance] --> B[Assign properties]
    B --> C[Assign fields]
    C --> D[Call sync inject methods]
    D --> E[await async inject methods]
    E --> F[Return completed Task<TService>]
```

#### Register-Path Generation

When a registration contains one or more async inject methods, the registration generator MUST emit a `Task<TImplementation>` registration, and a `Task<TService>` registration for each service type that awaits it. The `Init()` local function MUST perform synchronous injection first, then `await` each async inject method in stage order.

##### Disposal through `AsyncInitHolder<T, TImplementation>`

MS.DI tracks and disposes the object a factory returns, so a factory returning `Task<T>` would leave the `T` it creates undisposed. Every `Task<T>` registration whose factory creates an instance (the `Task<TImplementation>` above, and an async-init decorator chain, see [Async Decorators](Register.Decorators.spec.md#async-decorators)) MUST therefore be emitted as two registrations:

|Registration|Required behavior|
|:-----------|:----------------|
|`AsyncInitHolder<T, TImplementation>`|Registered with the registration's lifetime and key (non-keyed when the registration is). Its factory MUST create the `async Task<T> Init()` local function and return `new global::SourceGen.Ioc.AsyncInitHolder<T, TImplementation>(Init())`. `TImplementation` is the registration's implementation type: it only identifies the registration, so that registrations of the same `Task<T>` and key (two implementation types of a decorated service type under one key) each get a holder of their own.|
|`Task<T>`|Registered with the same lifetime and key, as `sp.GetRequiredService<AsyncInitHolder<T, TImplementation>>().Task` (keyed: `sp.GetRequiredKeyedService<AsyncInitHolder<T, TImplementation>>(key).Task`, with the lambda's `key`, so `KeyedService.AnyKey` registrations get one holder per requested key).|

Because the holder has the service's lifetime, a singleton holder is disposed with the root provider, and a scoped or transient holder with the scope that resolved it (MS.DI tracks disposable transients); resolving the holder never resolves a scoped service from the root. One instance per lifetime is unchanged: the holder caches the task, and a transient `Task<T>` creates a new holder per resolution. A `Task<TService>` registration that forwards to `Task<TImplementation>` creates no instance and has no holder.

`AsyncInitHolder<T, TImplementation>` is a runtime type of the `SourceGen.Ioc` package marked `[Experimental("SGIOCEXP001")]`; a generated Register file that has an async-init registration MUST emit `#pragma warning disable SGIOCEXP001` after the nullable directives, and other Register files MUST NOT.

The holder disposes only the task's result, like the Container output disposes an async-init field (see [Disposal of Async-init Fields](Container.Injection.spec.md#disposal-of-async-init-fields)):

|Holder method|Required behavior|
|:------------|:----------------|
|`Dispose()`|When the task completed successfully, dispose the result with `IDisposable.Dispose()`; a result that implements only `IAsyncDisposable` causes an `InvalidOperationException`. Any exception MUST be passed to `IocContainerGlobalOptions.OnDisposeException` and not rethrown.|
|`DisposeAsync()`|When the task completed successfully and the result is disposable, prefer `IAsyncDisposable.DisposeAsync()`, otherwise `Dispose()`. Any exception MUST be passed to `IocContainerGlobalOptions.OnDisposeException` and not rethrown. MUST NOT allocate a state machine when there is nothing to dispose.|
|Pending, faulted or canceled task|MUST NOT be awaited and MUST NOT dispose anything, even if the task completes later.|
|Repeated calls|Only the first `Dispose()` or `DisposeAsync()` call disposes the result.|

A decorator chain disposes its outermost decorator. The instance it wraps is disposed by its own registration, after the decorator: the implementation type's holder, or MS.DI for a synchronous implementation type. An instance that a factory creates inside the chain's `Init()` has no registration of its own and is not disposed. Intermediate decorators are not disposed either, as in the synchronous decorator registration and the Container output.

> **Known limitation**: MS.DI also tracks the `Task<T>` the factory returns (`Task` implements `IDisposable`), and disposes it before the holder. `Task.Dispose()` throws `InvalidOperationException` for a task that has not completed, so disposing a scope or provider while an initialization is still pending throws, unlike the Container output. This is MS.DI behavior that the generated registrations cannot avoid, and it predates the holder.

```csharp
#region Define:
using System.Threading.Tasks;

public interface ILogger { }
public interface IAsyncInitializer
{
    Task InitializeAsync();
}

public interface IMyService { }

[IocRegister(ServiceTypes = [typeof(IMyService)], Lifetime = ServiceLifetime.Singleton)]
public class MyService : IMyService
{
    [IocInject]
    public ILogger Logger { get; set; } = default!;

    [IocInject]
    public void InitializeSync()
    {
    }

    [IocInject]
    public async Task InitializeAsync(IAsyncInitializer initializer)
    {
        await initializer.InitializeAsync();
    }
}
#endregion

#region Generate:
services.AddSingleton<AsyncInitHolder<MyService, MyService>>((IServiceProvider sp) =>
{
    async Task<MyService> Init()
    {
        var s0_p0 = sp.GetRequiredService<ILogger>();
        var s0_m1 = sp.GetRequiredService<IAsyncInitializer>();
        var s0 = new MyService() { Logger = s0_p0 };
        s0.InitializeSync();
        await s0.InitializeAsync(s0_m1);
        return s0;
    }
    return new AsyncInitHolder<MyService, MyService>(Init());
});
services.AddSingleton<Task<MyService>>((IServiceProvider sp) => sp.GetRequiredService<AsyncInitHolder<MyService, MyService>>().Task);
services.AddSingleton<Task<IMyService>>(async (IServiceProvider sp) => await sp.GetRequiredService<Task<MyService>>());
#endregion
```

#### `WrapperKind.Task` Detection and Resolution

`Task<T>` is the async-init wrapper for consumer dependencies and partial accessors. The generator MUST only recognize the direct, non-nested form. Resolution MUST distinguish whether `T` itself has an async-init service path or only a synchronous service path.

|Requested type|Classification|Resolution behavior|
|:-------------|:-------------|:------------------|
|`Task<T>` where `T` is a non-wrapper service type|`WrapperKind.Task`|MUST classify as `WrapperKind.Task`. Resolution MUST follow the async-init vs. sync-only rules below.|
|`Task<Lazy<T>>`|Unsupported|MUST NOT be classified as `WrapperKind.Task`.|
|`Lazy<Task<T>>`|Unsupported|MUST NOT be classified as a supported nested wrapper combination.|
|`IEnumerable<Task<T>>`|Unsupported|MUST NOT be classified as a supported collection wrapper.|

|Inner service `T` shape|Register-path behavior|Container-path behavior|
|:----------------------|:---------------------|:----------------------|
|Async-init service (`T` resolves through generated `Task<T>` init path)|MUST resolve `Task<T>` directly via `sp.GetRequiredService<Task<T>>()` or `sp.GetService<Task<T>>()`, depending on requiredness/default handling rules.|MUST call the generated async resolver path for `Task<T>` directly.|
|Sync-only service (`T` only has synchronous resolution)|MUST wrap the synchronous resolution as `Task.FromResult(sp.GetRequiredService<T>())` or the matching optional/default-aware `Task.FromResult(...)` form.|MUST wrap the sync resolver result as `Task.FromResult(self.GetT_Resolve())` or the equivalent generated sync resolver call.|
|Not registered in the container (an imported module or the fallback provider)|—|MUST resolve `GetService(typeof(Task<T>)) as Task<T> ?? Task.FromResult((T)GetRequiredService(typeof(T)))` (keyed and optional forms accordingly), so that an async-init service of an imported module keeps its initialization task and a synchronous one is wrapped in a completed task.|

The Register path decides the shape per (service type, key) pair, comparing the dependency's key expression with the registration's: the same `T` can be async-init without a key and synchronous under a key (or the reverse), and `[FromKeyedServices("sync")] Task<T>` MUST then resolve `Task.FromResult(sp.GetRequiredKeyedService<T>("sync"))`, because only `T` is registered under that key.

It also takes the tags into account, like [explicit wrapper registrations](#explicit-wrapper-registrations): one call registers every tag group with a passed tag, so an async-init registration of another tag group may be missing. When the async-init registrations of `T` are always registered with the consumer, `Task<T>` MUST resolve `sp.GetRequiredService<Task<T>>()`; when only for some passed tags (a tagged consumer, and tagged async-init registrations that do not cover all its tags), `sp.GetService<Task<T>>() ?? Task.FromResult(sp.GetRequiredService<T>())`; otherwise `Task.FromResult(...)`. With `IService` async-init under tag `A` and synchronous under tag `B`, a consumer under `B` falls back to the synchronous service when only `B` is passed, instead of failing to resolve the missing `Task<IService>`. This applies to every dependency, including the body of a `Func<T1, ..., TReturn>` factory.

```csharp
// Invalid: nested async wrappers are not supported.
[IocRegister]
public class BadConsumer
{
    [IocInject]
    public void Initialize(Task<Lazy<IMyService>> service)
    {
    }
}
```

### Examples

**Basic Injection**:

```csharp
#region Define:
public interface IMayServiceDependency1;
public interface IMayServiceDependency2;
public interface IMayServiceDependency3;

[IocRegister(ServiceLifetime.Singleton)]
public class MyService([IocInject(Key = 10)] IMayServiceDependency1 sd)
{
    private readonly IMayServiceDependency1 sd = sd;

    [IocInject]
    public IMayServiceDependency2 Dependency { get; init; } = default!;

    [IocInject]
    public void Initialize(IMayServiceDependency3 sd3)
    {
        // Initialization code
    }
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    var p0 = sp.GetRequiredKeyedService<IMayServiceDependency1>(10);
    var s0_p0 = sp.GetRequiredService<IMayServiceDependency2>();
    var s0_m1 = sp.GetRequiredService<IMayServiceDependency3>();
    var s0 = new MyService(p0) { Dependency = s0_p0 };
    s0.Initialize(s0_m1);
    return s0;
});
#endregion
```

**Keyed method**: the key of an injected method (`[IocInject(Key = ...)]` on the method, or the key of its `InjectMembers` element) MUST apply to its parameters without their own key; a parameter's own key (`[FromKeyedServices(key)]`, `[IocInject(Key = ...)]`) takes precedence. The container output follows the same rule (see [Container Injection](Container.Injection.spec.md)).

```csharp
#region Define:
[IocInject(Key = "file")]
public void Initialize(ILogger fileLogger, [FromKeyedServices("console")] ILogger consoleLogger) { }
#endregion

#region Generate:
var s0_m0 = sp.GetRequiredKeyedService<ILogger>("file");
var s0_m1 = sp.GetRequiredKeyedService<ILogger>("console");
s0.Initialize(s0_m0, s0_m1);
#endregion
```

**Method with `[ServiceKey]` and `[FromKeyedServices]` on parameters**:

```csharp
#region Define:
public interface IMyService;
public interface IDependency;

[IocRegister(ServiceLifetime.Singleton, Key = "MyKey")]
public class MyService : IMyService
{
    public IDependency? Dep { get; private set; }
    public string? Key { get; private set; }

    [IocInject]
    public void Initialize(
        [FromKeyedServices("special")] IDependency dep,
        [ServiceKey] string key,
        IServiceProvider sp)
    {
        Dep = dep;
        Key = key;
    }
}
#endregion

#region Generate:
services.AddKeyedSingleton<MyService>("MyKey", (IServiceProvider sp, object? key) =>
{
    var s0_m0 = sp.GetRequiredKeyedService<IDependency>("special");
    var s0_m1 = "MyKey";
    var s0 = new MyService();
    s0.Initialize(s0_m0, s0_m1, sp);
    return s0;
});
#endregion
```

**Optional parameters with default values**:

```csharp
#region Define:
public interface IMyService;
public interface IOptionalDependency;

[IocRegister(ServiceLifetime.Singleton)]
public class MyService : IMyService
{
    public IOptionalDependency? OptDep { get; private set; }

    [IocInject]
    public void Initialize(IOptionalDependency? optDep = null, int timeout = 30)
    {
        OptDep = optDep;
    }
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    // An unregistered service gets the parameter's default value (see Optional Parameter Handling)
    var s0_m0 = sp.GetService<IOptionalDependency>() ?? default;
    var s0_m1 = sp.GetService(typeof(int)) switch { int service => service, _ => 30 };
    var s0 = new MyService();
    s0.Initialize(s0_m0, s0_m1);
    return s0;
});
#endregion
```

**Constructor with optional resolvable parameter**:

```csharp
#region Define:
public interface IMyService;
public interface IOptionalDependency;

[IocRegister(ServiceLifetime.Singleton)]
public class MyService(IOptionalDependency? optDep = null) : IMyService
{
    public IOptionalDependency? OptDep { get; } = optDep;
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    var p0 = sp.GetService<IOptionalDependency>() ?? default;
    var s0 = new MyService(p0);
    return s0;
});
#endregion
```

**Property and Field injection with nullable and keyed services**:

```csharp
#region Define:
public interface IMyService;
public interface IDependency;
public interface IOptionalDependency;
public interface ILogger;
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
}

[IocRegister(ServiceLifetime.Singleton)]
public class MyService : IMyService
{
    // Required property injection with key (default! marks it required, not a default value)
    [IocInject(Key = "special")]
    public IDependency Dep { get; init; } = default!;

    // Nullable property injection (uses GetService instead of GetRequiredService)
    [IocInject]
    public IOptionalDependency? OptionalDep { get; set; }

    // Property with initializer (treated as optional)
    [IocInject]
    public ILogger Logger { get; set; } = NullLogger.Instance;

    // Field injection (requires the FieldInject feature; the field must be accessible)
    [IocInject]
    internal IServiceProvider ServiceProvider = default!;
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    var s0_p0 = sp.GetRequiredKeyedService<IDependency>("special");
    var s0_p1 = sp.GetService<IOptionalDependency>();
    var s0_p2 = sp.GetService(typeof(ILogger)) switch { ILogger service => service, _ => NullLogger.Instance };
    var s0_p3 = sp;
    var s0 = new MyService() { Dep = s0_p0, OptionalDep = s0_p1, Logger = s0_p2, ServiceProvider = s0_p3 };
    return s0;
});
#endregion
```

**MS.E.DI native handling example (no factory needed)**:

```csharp
#region Define:
public interface IMyService;
public interface IDependency;

[IocRegister<IMyService>(ServiceLifetime.Singleton)]
public class MyService(
    [FromKeyedServices("special")] IDependency dep,  // MS.E.DI handles [FromKeyedServices]
    IServiceProvider sp                              // MS.E.DI handles IServiceProvider
) : IMyService;
#endregion

#region Generate:
// Simple type-based registration - MS.E.DI handles the special parameters automatically
services.AddSingleton<MyService, MyService>();
services.AddSingleton<IMyService>((IServiceProvider sp) => sp.GetRequiredService<MyService>());
#endregion
```

**Wrapper type resolution**:

```csharp
#region Define:
public interface IMyService;
public interface IOtherService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
public class MyService : IMyService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IOtherService)])]
public class OtherService : IOtherService;

[IocRegister(Lifetime = ServiceLifetime.Singleton)]
public class Consumer(Lazy<IMyService> lazyService, Func<IOtherService> factory) { }
#endregion

#region Generate:
// ...MyService and OtherService registrations...

// Consumer resolves the wrappers from DI, so MS.E.DI constructs it directly
services.AddSingleton<Consumer, Consumer>();

// Standalone wrapper registrations for the registered inner services, resolving their implementations
services.AddSingleton<Lazy<IMyService>>((IServiceProvider sp) => new Lazy<IMyService>(() => sp.GetRequiredService<MyService>(), LazyThreadSafetyMode.ExecutionAndPublication));
services.AddSingleton<Func<IOtherService>>((IServiceProvider sp) => new Func<IOtherService>(() => sp.GetRequiredService<OtherService>()));
#endregion
```

## See Also

- [Decorators](Register.Decorators.spec.md)
- [Factory Method Registration](Register.Factory.spec.md)
- [Container Injection](Container.Injection.spec.md)
