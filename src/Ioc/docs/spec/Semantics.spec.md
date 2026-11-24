# Registration and resolution semantics

> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals.

## Overview

This specification defines what a registration means and how a dependency is resolved. These rules do not depend on the output. They apply to the container, to the module output and to the service collection extension, unless a rule names one output.

The constitution holds the terms, the principles, the scope and the diagnostic policy. This specification adds only the details that the constitution does not state. In this document, each statement that something "is an error" is an IoC semantic error. The generator always reports it at the Error level. For the ID of each diagnostic, see the [diagnostic list](Diagnostics.spec.md).

## Registration model

- **Instance definition**: the unit that identifies an instance.
  - Implementation type registration: implementation type + key.
  - Factory registration: factory method + key.
  - Instance registration: static member + key.
- More than one attribute can contribute service types to one instance definition. The result is the union. The Lifetime, `Decorators` and `Tags` MUST be exactly the same. If they are not, it is an error.
- One instance definition MUST be declared completely in one assembly. When another assembly puts a registration attribute on the same instance definition, it is an error.
- A concrete registration suppresses a derived registration. When the concrete registration of `Handler<Foo>` and the open registration of `Handler<>` exist together with the same key:
  - The two are independent instance definitions. Their settings do not have to agree. The concrete registration can be in a different assembly from the open registration.
  - The open registration does not produce a derived registration for `Handler<Foo>`. Thus `Handler<Foo>` appears one time only, with the service types and settings that the concrete registration declares.
  - Tags filter first, and suppression comes second. When tags exclude the concrete registration, the generator produces the derived registration as usual.
- Different from MS.DI: one Singleton or Scoped instance definition has one underlying instance. This is true also when it registers more than one service type, when a decorator wraps it, or when a wrapper type wraps it. The container creates different instances only for different instance definitions.
- A Transient instance definition gets a new instance at each injection point. For `A(IFoo foo, IBar bar)` with `Impl : IFoo, IBar` registered as Transient, `foo` and `bar` are two different `Impl` objects.
- When there is an implementation type, the generator always registers the implementation type itself as a service type. If the same implementation type is also registered as a keyed service, the two get different instances.
- Constructor selection. The generator examines these in order:
  1. A constructor marked with `[IocInject]` or `[ActivatorUtilitiesConstructor]`.
  2. If no constructor is marked and the type has a primary constructor, the primary constructor.
  3. The only public constructor.

  If none of these applies, it is an error. This occurs when more than one constructor is marked, or when there is no primary constructor and the number of public constructors is zero or more than one. `[IocInject]` can also mark a constructor that is not public. If the generated code cannot access it, the compiler reports the error. A user who does not own the type uses a factory registration.
- When one "service type + key" has more than one instance definition:
  - A concrete registration wins over a derived registration. For example, `IntRepo : IRepo<int>` wins over the `Repo<int>` that comes from `Repo<>`. This agrees with the behavior of MS.DI.
  - If more than one candidate stays after that rule, a dependency site that asks for one service gets an ambiguity error. The user MUST use a key or a collection. A request for one service at run time throws `InvalidOperationException`.
  - A collection contains all instance definitions and does not apply the priority rule. A dictionary applies the priority rule first and then checks for duplicate keys.
- Ordering. The two outputs share it, and it does not depend on how registrations are imported. Derived registrations come first, and concrete registrations come second. In each group, the ordering is ordinal, by the fully qualified name of the instance definition (implementation type, factory method, instance member) and the canonical form of the key.
  - Consequence: a registration of the assembly does not override a registration of an imported module. When more than one concrete registration provides the same service type, the container reports an ambiguity. In the service collection extension, the registration that is last by name wins, and it does not matter which assembly imports which.

For the detailed rules, see [Constructor selection](Annotations.spec.md#constructor-selection), [Instance definitions](Analysis.Model.spec.md#instance-definitions), [Suppression](Analysis.Model.spec.md#suppression) and [Single service](Analysis.Model.spec.md#single-service).

## Dependency resolution

- A resolve call in code (`sp.GetService<T>()` and so on) is not a dependency site. It is only a [discovery source](#open-generics). It causes generation only and never causes an error, because the generator cannot know which provider the call targets.
- The generator MUST check for circular dependencies. A circular dependency is an error. A cycle through `Lazy<T>` or `Func<T>` is legal. `Task<T>` does not break a cycle.
- `IocContainerAttribute.IntegrateServiceProvider` decides whether there is a fallback provider.
- When a required dependency is not found:
  - Without a fallback provider: it is an error.
  - With a fallback provider: the generator generates code that resolves from the fallback provider, and it reports an Info diagnostic. The level stays Info and not Warning, because with the default settings the framework services (`ILogger<T>`, `IOptions<T>` and so on) all come from the fallback provider. These diagnostics show only in the IDE and in the SARIF log. The dependency graph gives the full list.
- An optional dependency tries these in order: the container, then the fallback provider, then the default.
  - A parameter with a default value: the default value is used. This is also true when the parameter is nullable too.
  - A reference type annotated as nullable: it gets `null`. MS.DI looks at the default value only and not at the nullable annotation, so this is a documented difference. Code without nullable annotations (`#nullable disable`) is always required.
  - A parameter of a nullable value type (`int?`) without a default value is required. MS.DI does the same.
  - A parameter that has `[Optional]` and no default value is required. MS.DI does the same (see [ADR 2026112406](../adr/2026112406_OptionalDependencyByNullableType.md)).
  - A partial accessor whose return type is a reference type annotated as nullable: it gets `null`. A nullable value type is required, as on a parameter.
  - An injected property or field that has an initializer and that can be assigned after construction: the generator does not assign it, and the initial value stays. This applies only to a member that is declared in the current compilation. The test is the assembly of the member and not the availability of syntax, because a project reference in the IDE also gives syntax.
  - An initializer whose expression is the null-forgiving operator applied to `null`, `default`, or `default(T)` (for example `= null!`) does not count. Parentheses around it do not change this. Such an initializer only suppresses the nullable warning, so the member is required (see [ADR 2026112503](../adr/2026112503_NullForgivingInitializerIsRequired.md)).
  - An `init` or `required` injected property is always required, because an object initializer cannot assign conditionally.
  - A member of a type that the user does not own, specified with `IocInjectMemberAttribute`, is always required.
- Lifetime limits on dependencies:
  - Captive dependency (an error): a Singleton MUST NOT depend on a Scoped service. This includes a dependency through `Lazy<T>` or `Func<T>`, and an indirect dependency through a Transient (for example a Singleton that depends on a Transient that depends on a Scoped service).
  - A Singleton or Scoped service that depends on a Transient (a Warning, which does not affect generation): the holder keeps that Transient instance until the holder itself is disposed. This makes the Transient a Singleton or a Scoped service in effect. A stateless Transient has no problem, so this is not an error. It includes a direct dependency, `Lazy<T>`, and a collection or dictionary that contains a Transient.
    - Exception: a dependency on a Transient through `Func<T>` or `Func<..., T>` causes no warning. Each call creates a new instance, and the holder keeps only the factory.
- Resolution of a Scoped service from the root: it always throws `InvalidOperationException` at run time. There is no option to turn this off.
- Type matching ignores tuple element names, nullable annotations, and the difference between `dynamic` and `object`. The run-time type decides.

For the detailed rules, see [Extraction of dependency sites](Annotations.spec.md#extraction-of-dependency-sites), [Normalization](Analysis.Model.spec.md#normalization), [Dependency resolution](Analysis.Model.spec.md#dependency-resolution), [Circular dependencies](Analysis.Model.spec.md#circular-dependencies) and [Lifetime checks](Analysis.Model.spec.md#lifetime-checks).

## Member injection

- The generator can inject into a field, a property and a method.
- A user who owns the type puts `[IocInject]` on the member. A user who does not own the type specifies the member with `IocInjectMemberAttribute`.
- The generator injects a `required` or `init` property with an object initializer.
- The order is fixed:
  1. Constructor.
  2. Fields and properties.
  3. Methods.

For the detailed rules, see [Injected members](Annotations.spec.md#injected-members).

## Asynchronous services

- Asynchronous injection method: an injection method that returns `Task`, marked with `[IocInject]` or specified with `IocInjectMemberAttribute`. A return type of `Task<X>`, `ValueTask` or `async void` is an error.
- The unit of asynchrony is the closed instance definition. In the cases below, the whole instance definition is an asynchronous service, and all its service types and its own type MUST be injected as `Task<T>`:
  - The implementation type has an asynchronous injection method.
  - A decorator that is really applied on any one of its decorator chains has an asynchronous injection method.
  - The factory method returns `Task<T>`. It is an asynchronous factory of `T`, and the service type is `T`. This is the documented exception to the rule that a wrapper type cannot be registered.
- A dependency site that asks for an asynchronous service `T` MUST inject `Task<T>`. If it does not, it is an error.
- Run order: all synchronous injection completes first. Then the generated code awaits each asynchronous injection method in declaration order.
- A Singleton or Scoped asynchronous service caches the `Task<T>` itself. Concurrent resolution initializes one time only. If initialization fails, the task stays failed and there is no retry.
- A run-time call of `GetService(typeof(T))` for an asynchronous service treats the service as not registered. It also does not go to the fallback provider. Only `Task<T>` can be resolved.
- Collections and dictionaries. When the candidates of a service type include an asynchronous implementation:
  - The element type that the dependency site asks for MUST be `Task<T>` or a legal wrapper of it (`IReadOnlyList<Task<T>>`, `IDictionary<TKey, Task<T>>`, `IEnumerable<Lazy<Task<T>>>` and so on). If it is not, it is an error.
  - The collection that is always generated is `IEnumerable<Task<T>>` and not `IEnumerable<T>`.
  - A run-time request to the container for `IEnumerable<T>` throws `InvalidOperationException`. It does not return a partial result with only the synchronous candidates.
- A dependency site can ask for a synchronous service as `Task<T>`. The generator wraps it in a completed task.

For the detailed rules, see [Checks of an injection method](Annotations.spec.md#checks-of-an-injection-method) and [Asynchronous classification](Analysis.Model.spec.md#asynchronous-classification).

## Decorators

- The generated code puts the instance of the implementation type into the decorators in order. The IoC container also injects the other dependencies of each decorator.
- Order: the first item of the `Decorators` array is the outermost layer. `Decorators = [typeof(A), typeof(B)]` gives `A(B(implementation))`.
- Unit of a decorator chain: each service type of each instance definition, other than the implementation type itself, has one decorator chain. All decorator chains share the underlying implementation instance. When one instance definition is resolved as `IA` and as `IB`, the two results are different outermost objects that wrap the same implementation instance.
- Inner parameter: the generator selects the constructor of the decorator with the general constructor selection rule. In that constructor, the parameter whose type equals the service type of the decorator chain receives the inner instance. More than one such parameter is an error. The other parameters are ordinary dependencies.
  - Parameters with a key and `[ServiceKey]` parameters are excluded first and cannot be the inner parameter. For the remaining parameters, only the type counts. Thus a decorator can also depend on a keyed service of the same type.
- Where a decorator applies: a decorator applies only to the service types for which it has an inner parameter. The decorator chains of the other service types do not go through it.
- The inner parameter is decided one time at the definition level and is not decided again after closing. After an open generic is closed, another parameter can happen to have the same type as the service type. It stays an ordinary dependency.
- Each decorator MUST apply to at least one service type. If it does not, it is an error. The generator decides this at the definition level and does not consider generic constraints. If constraints exclude all closed forms of an open generic, it is not an error.
- Open generic decorator: the generator derives the type arguments from the mapping between the type of the inner parameter and the service type. A type parameter that cannot be derived this way is an error.
- When `Decorators` is specified but the instance definition has no service type other than the implementation type itself, it is an error.
- A resolution of the implementation type itself gives the original instance without decorators.
- Decorators apply only to implementation type registrations. They do not apply to factory registrations or instance registrations.

For the detailed rules, see [Decorators](Analysis.Model.spec.md#decorators).

## Open generics

- The generator can find dependency demands automatically, find the closed generics of open registrations from them, and generate code. The user can also give the demand manually with `IocDiscoverAttribute`.
- The only discovery sources are dependency sites, `IocDiscoverAttribute`, and resolve calls on `IServiceProvider` in code (`GetService`, `GetRequiredService`, `GetServices` and their keyed versions).
  - Both the generic form (`GetService<T>()`) and the non-generic form whose argument is a direct `typeof()` (`GetService(typeof(T))`) are supported.
  - When the key of a resolve call is a compile-time constant, the generator discovers with that key. When it is not a constant, the generator discovers all keyed registrations of that type and does not guess one of them.
  - One rule applies to both forms: the requested type MUST be a concrete closed type that is known at compile time. The cases below are outside discovery and need `IocDiscoverAttribute`:
    - The type is a type parameter or contains a type parameter (`GetService<T>()`, `GetService<IHandler<T>>()` or `GetService(typeof(T))` in a generic method or a generic type).
    - The type is an open generic (`GetService(typeof(IHandler<>))`).
    - The argument is not a direct `typeof()` (for example a variable, or a `Type` that a method returns).
  - When the type that a resolve call asks for has no matching registration, or the combination of wrapper types is not legal, the generator ignores the call silently.
- When the service type is an open generic (`IRequestHandler<,>`) and the implementation type is non-generic or partly closed, the generator registers all closed forms that the implementation type implements.
- When the type arguments do not satisfy the generic **constraints** of an open generic implementation or decorator, that implementation or decorator is not a candidate. It is not an error. The dependency graph MUST be able to show which decorators are really applied to each closed generic, and why a candidate is excluded.
- The C# rules of constraint satisfaction decide whether a constraint fits. This includes variance conversions and the special constraints such as `class`, `struct`, `unmanaged` and `new()`.
- When more than one candidate fits, the rules of the [registration model](#registration-model) for more than one instance definition apply. There is no "first match" rule.

For the detailed rules, see [Expansion of service types](Annotations.spec.md#expansion-of-service-types), [Sources of demands](Analysis.Model.spec.md#sources-of-demands), [Closing](Analysis.Model.spec.md#closing) and [Check of generic constraints](Analysis.Model.spec.md#check-of-generic-constraints).

## Wrapper types

The generator can find dependency demands automatically and generate wrapper types. This applies to all registration kinds. `Func<..., T>` applies only to implementation type registrations. For each registered service type, the generator always generates `IEnumerable<T>` (`IEnumerable<Task<T>>` when there is an asynchronous candidate). It generates the other wrapper types only for a discovery demand. The discovery sources are the same as for [open generics](#open-generics).

- `Func<T>`: it captures the scope that resolves it. When `T` is a Singleton, the `Func` is a global singleton. If not, each scope caches one `Func`. The Lifetime set at registration decides whether the created instance is stored.
- `Func<..., T>`: the caller can pass arguments after injection. The generator maps the arguments by type to the constructor parameters of `T`. If more than one argument has the same type, or an argument maps to no constructor parameter, it is an error. `T` MUST be registered as Transient. If it is not, it is an error.
- `Lazy<T>`: its Lifetime follows `T`. When `T` is Transient, each injection gets a new `Lazy<T>`.
- `Task<T>`: an asynchronous service.
- `KeyValuePair<TKey, T>`: it wraps only services that have a key.
- Collection (`IEnumerable<T> / IReadOnlyCollection<T> / ICollection<T> / IReadOnlyList<T> / IList<T> / T[]`): it returns more than one service at a time.
- Dictionary (`IDictionary<TKey, T> / IReadOnlyDictionary<TKey, T> / Dictionary<TKey, T>`): it returns more than one keyed service at a time.
  - It contains only the registrations whose key type is exactly `TKey`. When `TKey` is `object`, it contains all of them.
  - When one key has more than one instance definition, it is an error.
- Nested wrapper types. These combinations are supported:<br/>
  (1) `Func<Task<T>>`<br/>
  (2) `Func<..., Task<T>>`<br/>
  (3) `Lazy<Task<T>>`<br/>
  (4) `KeyValuePair<TKey, T>` that wraps `Func<T>`, `Func<..., T>`, `Lazy<T>`, `Task<T>`, (1), (2), (3)<br/>
  (5) `Collection` that wraps `Func<T>`, `Func<..., T>`, `Lazy<T>`, `Task<T>`, `KeyValuePair<TKey, T>`, (1), (2), (3), (4)<br/>
  (6) `Dictionary` that wraps `Func<T>`, `Func<..., T>`, `Lazy<T>`, `Task<T>`, (1), (2), (3)<br/>
  > The combination of nested wrapper types that a dependency site asks for MUST obey these rules. If it does not, it is an error.

> No registration kind can register one of these wrapper types as a service type. The generator MUST report an error and tell the user to use the automatic generation or to wrap the type in a class. This prevents a conflict with the generated code. The only exception is a factory method that returns `Task<T>` (see [Asynchronous services](#asynchronous-services)).

For the detailed rules, see [Grammar of wrapper types](Analysis.Model.spec.md#grammar-of-wrapper-types), [Multiple services](Analysis.Model.spec.md#multiple-services) and [Mapping of `Func<..., T>`](Analysis.Model.spec.md#mapping-of-func-t).

## Factory and instance

- Factory method registration: the specified static factory method creates the service instance. The IoC container injects the parameters of the method.
- Generic factory method registration: the service type is always the return type of the method. The generator gets the mapping of type parameters directly from the return type. `ServiceTypes` MUST NOT be specified. A user who needs a different service type or a different order of type parameters writes an adapter method with a matching return type.
- Instance: the specified static object is the service instance. It is always a Singleton. Neither output disposes it. When one static member is registered with more than one key, each key gives the same object.

For the detailed rules, see [Service types of a factory](Annotations.spec.md#service-types-of-a-factory) and [Service types of an instance](Annotations.spec.md#service-types-of-an-instance).

## Keyed services

- `KeyType.Value`: the key MUST be a constant that is legal in an attribute. Its value is known at compile time.
- `KeyType.Csharp`: the key MUST be specified with `nameof()`. It can point to any static field, property or constant.
  - When it points to a `const`: the generator normalizes it to the constant value. It is fully equivalent to `KeyType.Value`.
  - In the other cases: the fully qualified name is the compile-time identity, and the key is not equal to any other key. At run time, `Equals` compares the keys. If two different symbols are equal at run time, that is the responsibility of the user. In this case (two keys registered on the same service type), the generated container throws an exception at type initialization. It does not let one key silently replace the other.
- The key at the dependency site and the key at the registration MUST have the same compile-time identity to match. The generator does not guess.
- Across assemblies, the key MUST be accessible from outside the assembly. If it is not, it is an error. The symbol of a `KeyType.Csharp` key MUST be public. The value of a `KeyType.Value` key MUST NOT be of an internal type (for example an internal enum).
- The generator recognizes `[FromKeyedServices]` and `[ServiceKey]` of MS.DI directly. Both can be put on parameters only.
- `IocInjectAttribute` aligns with `[FromKeyedServices]`. With `LookupMode`, it supports `InheritKey` and `NullKey` of `ServiceKeyLookupMode`. It can be used on a parameter, a field, a property and a partial accessor.
  - `LookupMode` is `InheritKey` or `NullKey` and `Key` is also specified: an error.
  - `[IocInject]` and `[FromKeyedServices]` on the same position: an error.
  - On a constructor (to select the constructor) or on an injection method (to mark it for injection), `Key` and `LookupMode` MUST NOT be specified. If one is specified, it is an error. The key belongs on each parameter.
  - When `InheritKey` finds no registration for that key, it does not go back to the registration without a key. The generator handles it as a missing dependency. This agrees with MS.DI.
- `[ServiceKey]` is supported on parameters only. A field or property that needs the key gets it through a parameter of the constructor or of an injection method.
- `KeyedService.AnyKey`: registration with it is not supported and is an error. A query of `GetKeyedServices` with it is supported and returns all keyed registrations of that service type. A dependency site with it is an error unless it asks for a collection or a dictionary, because MS.DI always throws when `AnyKey` resolves one service.

For the detailed rules, see [Keys](Annotations.spec.md#keys) and [Key specification](Annotations.spec.md#key-specification).

## Tags

- A service without tags is always included.
- A service with tags is included only when at least one tag matches.
- Tags belong to the instance definition.
- The container specifies tags at compile time with `IncludeTags`. The service collection extension specifies tags at run time with the parameters of the method.

For the detailed rules, see [Tags](Analysis.Model.spec.md#tags).

## Registration scope

There are two registration scopes only: the whole assembly, or private to one `ExplicitOnly` container.

| Attribute | Position | Registration scope |
| --- | --- | --- |
| `IocRegisterAttribute`, `IocFactoryAttribute`, `IocInstanceAttribute` | Directly on the type or member | Always the whole assembly, also when the type is nested in an `ExplicitOnly` container class |
| `IocRegisterForAttribute`, `IocRegisterBatchAttribute`, `IocFactoryForAttribute`, `IocInstanceForAttribute`, `IocInjectMemberAttribute`, `IocDiscoverAttribute`, `IocImportModuleAttribute` | The assembly, a class that has `IocModuleAttribute`, an ordinary container class (`ExplicitOnly = false`) | The whole assembly. These three positions are fully equivalent |
| The same attributes as the row above | A container class with `ExplicitOnly = true` | That container only |
| `IocInjectAttribute`, `[FromKeyedServices]`, `[ServiceKey]`, `[ActivatorUtilitiesConstructor]` | Members and parameters of the type | An annotation that belongs to the type itself. It is visible to all registration scopes |

- Registrations and imports of the whole-assembly scope: all ordinary containers of the assembly include them, and they go to the module output and to the service collection extension.
- An `ExplicitOnly` container is isolated. It includes only the registrations and imports that are on itself, and it cannot see the registrations and imports of the assembly scope. Its own registrations also do not go to the module output or to the service collection extension. Thus the two registration scopes cannot have a conflict of instance definitions.
- An `ExplicitOnly` container does not apply tags. It includes everything that is on it. `ExplicitOnly` together with `IncludeTags` is an error.
- One class MUST NOT be both an `ExplicitOnly` container and a module. An `ExplicitOnly` container MUST NOT import the module of its own assembly. Both are errors.
- `IocInjectMemberAttribute` MUST be in the same assembly and the same registration scope as the **registration attribute** of that implementation type. If it is not, it is an error. The limit is on the position of the registration, not on the position of the type definition:
  - Allowed: the type is defined in a third-party library. The user registers it in the user's own assembly with `IocRegisterForAttribute` and puts `IocInjectMemberAttribute` in the same assembly.
  - Not allowed: a module registers a type, and the importer puts `IocInjectMemberAttribute` on that type. The construction is enclosed in the construction entry of the module, and the importer cannot add to it. One instance definition also MUST NOT have different constructions for different composition roots.
  - Not allowed: the registration is in the assembly scope and `IocInjectMemberAttribute` is on an `ExplicitOnly` container, or the reverse.
- An assembly that has no container and no module can import a module with an assembly-level `IocImportModuleAttribute`.
- A demand that a resolve call discovers belongs to the whole assembly. It also has an effect on an `ExplicitOnly` container, but the generator generates only for the registrations that the container already has.

For the detailed rules, see [Attribute declarations](Annotations.spec.md#attribute-declarations) and [Views](Analysis.Model.spec.md#views).

## Limits

The [scope](../Constitution.md#5-scope) of the constitution has the table of limits. This section adds the details.

### Type check

A type check in an analyzer is optional.

### Nested open generics

`Outer<>.Inner<>` is an error. A nested generic whose outer type is closed (`Outer<int>.Inner<>`) and a generic type nested in a non-generic type (`Outer.Inner<>`) are supported.

### Disposable Transient

The generated container does not dispose Transient services, which is different from MS.DI. A disposable Transient is an error. This limit also applies to these cases:

- The `T` of `Func<..., T>`.
- A Transient instance definition that has a disposable decorator.
- A Transient factory whose return type is known to be disposable at compile time.

More rules for this limit:

- The generator reports it on the container only. The service collection extension does not report it, because MS.DI disposes the service.
- This limit is not relaxed. The DI guidelines of .NET recommend that an `IDisposable` is not registered as transient. The registrations of the framework are all in the fallback provider, so this limit does not affect them.
- When the return type of a Transient factory does not show at compile time whether the object is disposable (an interface or a class that is not sealed), it is not an error. The container does not check the object and does not track it. The returned object is not disposed.

For the detailed rules, see [Disposable Transient](Analysis.Model.spec.md#disposable-transient).

### Service type

When the return type of a factory or the type of an instance is a value type, it is an error. The user SHOULD wrap the value in a class. The generated `KeyValuePair<TKey, T>` is the exception.

This limit is for registration only. When the type of a dependency is a value type and the container has no registration for it, the generator resolves it from the fallback provider like any other missing dependency. This includes a `KeyValuePair<TKey, T>` that the container cannot build.

## See also

- [How the generator reads annotations](Annotations.spec.md)
- [Analysis model](Analysis.Model.spec.md)
- [Module output](Module.Output.spec.md)
- [Container output](Container.Output.spec.md)
- [Service collection extension output](ServiceCollection.Output.spec.md)
- [Diagnostic list](Diagnostics.spec.md)
