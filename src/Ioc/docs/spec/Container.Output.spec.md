# Container output

> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals.

## Overview

The generator adds the container implementation to a class that has `IocContainerAttribute`. The container is the composition root. It gets the complete dependency graph from its own view (see "Views" in [Analysis model](Analysis.Model.spec.md#views)). It owns all instances. It manages lifetime, caching, and disposal.

This document defines the observable behavior of the container. It also defines the structural requirements that performance and NativeAOT make necessary. The implementation decides each generation detail that this document does not list as a requirement, such as member names, the data structure of the lookup table, and the granularity of the locks. The generated code in this document is an illustration.

The generator generates the container only when the full check has no Error (see "Two levels of checks" in [Analysis model](Analysis.Model.spec.md#two-levels-of-checks)).

## Container class

- The class MUST be a `partial class` and MUST NOT be static. The generator does not check these two rules. The generated partial declaration makes the compiler report them.
- The class MUST NOT be generic, and it MUST NOT be nested in a generic type (`Outer<T>.AppContainer`). In both cases the generator reports `SGIOC107` and does not generate the container. The class MAY be a nested type of a non-generic type. Each containing type MUST be `partial`, and the compiler reports a violation.
- The class MAY also have `IocModuleAttribute`. An `ExplicitOnly` container is the exception (`SGIOC102`).

### Constructor

The generator generates exactly one public constructor. The shape depends on `IntegrateServiceProvider`:

| `IntegrateServiceProvider` | Constructor |
| --- | --- |
| `true` (default) | `public AppContainer(IServiceProvider fallbackProvider)`. It throws `ArgumentNullException` when the argument is `null`. |
| `false` | `public AppContainer()` |

The generator does not generate a parameterless constructor when `IntegrateServiceProvider = true`. At compile time, the generator already sends each missing dependency to the fallback provider. An instance without a fallback provider always fails at run time.

### Implemented interfaces

`IIocContainer`, `IServiceProvider`, `ISupportRequiredService`, `IKeyedServiceProvider`, `IServiceProviderIsService`, `IServiceProviderIsKeyedService`, `IServiceScopeFactory`, `IServiceScope`, `IDisposable`, `IAsyncDisposable`.

The container is also its own `IServiceScope`. [Root and scope](#root-and-scope) gives the reason.

```csharp
public interface IIocContainer :
    IServiceProvider, ISupportRequiredService, IKeyedServiceProvider,
    IServiceProviderIsService, IServiceProviderIsKeyedService,
    IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    T? GetService<T>() where T : class;
    T GetRequiredService<T>() where T : notnull;
    IEnumerable<T> GetServices<T>();
    T? GetKeyedService<T>(object? serviceKey) where T : class;
    T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull;
    IEnumerable<T> GetKeyedServices<T>(object? serviceKey);
}
```

- `IIocContainer` gives strongly typed methods such as `GetService<T>`. The interface is not generic.
- Each strongly typed method MUST behave exactly like the related `IServiceProvider` extension method. It MUST NOT be slower than that extension method.

## Root and scope

The root and a scope are different instances of the same class.

- The instance that the user creates with the constructor is the root. `CreateScope()` creates a scope.
- The generator generates `public AppContainer CreateScope()`. Its return type is the container class itself. `IServiceScopeFactory.CreateScope()` is an explicit interface implementation that calls it. Thus the user can use partial accessors and strongly typed methods on a scope:

  ```csharp
  using var scope = container.CreateScope();
  var handler = scope.OrderHandler;   // partial accessor, Scoped service
  ```

- `IServiceScope.ServiceProvider` returns the scope itself.
- A call to `CreateScope()` on a scope creates another scope of the root. It does not create a nested scope. MS.DI does the same.
- The root does not track the scopes that it creates. Disposal of the root does not dispose a scope that is not yet disposed. MS.DI does the same.

Structural requirements:

- A scope instance MUST NOT allocate storage for Singleton services. The Singleton instances, the synchronization objects, and the state of the root are in one state object. The root and all scopes share this state object. Each instance holds only a reference to it.
- The root instance does not use the Scoped storage (see below).

### Resolution of a Scoped service from the root

When the container resolves a Scoped closed definition and the instance that resolves it is the root, the container throws `InvalidOperationException`. The message names the service and the lifetime. No option disables this behavior.

This includes the indirect cases. One case is a Transient service that depends on a Scoped service and that the root resolves. The other case is a collection that contains a Scoped service. The generator already stops a Singleton that depends on a Scoped service at compile time. Thus the exception occurs only when the user asks the root directly.

## Resolution of a closed definition

Each closed definition has one internal resolver in the container. Its behavior depends on the lifetime:

| Lifetime | Storage | Behavior |
| --- | --- | --- |
| Singleton | The shared state object | Created one time for the whole root |
| Scoped | The scope instance | Created one time for each scope |
| Transient | None | Created for each request |
| Instance registration | None | Each request reads the static member (through an entry, if the registration is imported). A wrapper, collection or dictionary that contains it follows the cache rules of a Singleton, and keeps the object that it read first, as MS.DI keeps the object of `Add{Name}` |

- The creation steps follow the construction plan (see "Construction plan" in [Analysis model](Analysis.Model.spec.md#construction-plan)). The container always resolves a dependency on the current instance (root or scope). It resolves the dependencies of a Singleton on the root.
- The fast path of a Singleton or Scoped service (the instance exists) MUST be one field read and one null check. It MUST NOT take a lock and MUST NOT allocate memory. The creation path and the fast path are two separate methods, so that the fast path can be inlined.
- Failure during creation: during the creation of an instance or a decorator chain, a step after `new` can throw. These steps are the assignment of fields and properties, the injection methods, and the constructors of the later decorator layers. In that case, nothing disposes the objects that are created but not yet stored in the container. The container disposes them, outer objects first and inner objects last. Then it throws the original exception again. The rule is the same as for [the service collection extension](ServiceCollection.Output.spec.md#failure-during-creation) (see "Failure during creation"). It applies to asynchronous creation too. The container does not store the failed instance. The next resolution creates it again.
  - The container stores the layers of a decorator chain only after the whole chain is built. Otherwise a layer is disposed one time at the failure and one more time when the container is disposed. The container stores the implementation instance before it builds the chain. The failure handling of the chain does not dispose the implementation instance.
  - A Transient path does not have this handling. The generator generates it only when the object is disposable, and the container does not support a disposable Transient (`SGIOC403`).
  - After a failed creation, the field is empty, the lock is released, a retry succeeds, and a concurrent first resolution does not leak an object or dispose an object two times (tested, see the ADRs).
- When a factory method returns `null`, the container throws `InvalidOperationException`. A service type is a reference type, and `null` is not a valid instance. Without explicit handling, the check for "already created" makes the container call the factory again and again. The same applies to an instance registration: when the static member is `null`, the request throws `InvalidOperationException`. This is a [documented difference](../Constitution.md#8-documented-differences-from-msdi) (see [ADR IocRewrite/0021](../adr/0001_IocRewrite/0021_NullFromFactoryThrows.md)).

### Service types

All service types of one closed definition share the same underlying instance, except for Transient:

- The type itself: the implementation instance without decorators.
- A service type without a decorator chain: the implementation instance, converted to the service type with a static cast `(IService)instance`, so that a type error appears at compile time. When an imported instance definition returns `object`, the cast is a run-time cast.
- A service type with a decorator chain: the outermost layer of that chain. For Singleton and Scoped, each chain caches its own outermost layer. Each middle layer is stored too, so that the container can dispose it.
- Transient: each request creates a new implementation instance and a new complete chain.

### Asynchronous closed definitions

- The internal resolver returns `Task<instance type>`. Singleton and Scoped cache the task itself.
- Concurrent resolution initializes the service only one time. For each value of `ThreadSafeStrategy`, an asynchronous closed definition always uses the [lock of its lifetime](#thread-safety) to protect the step "create and store the task". The container does not await inside the lock. `ThreadSafeStrategy.None` is the exception. It gives no guarantee.
- When initialization fails, the task stays in the failed state. The container does not retry.
- The `Task<S>` of each service type is a projection of the task of the instance. When the service type has a decorator chain, the projection builds the chain. For Singleton and Scoped, each projection is created only one time and cached. Thus all requests in the same scope get the same `Task<S>`.
- Each generated asynchronous method uses `ConfigureAwait(false)`.

## Lookup by `Type`

`GetService(Type)`, `GetKeyedService(Type, object?)`, their `Required` versions, and the strongly typed versions share one lookup table.

### Contents of the table

| Type | Key | Condition |
| --- | --- | --- |
| Each service type of each closed definition | The key of the definition | The closed definition is synchronous |
| `Task<S>` | The key of the definition | Each service type `S` of an asynchronous closed definition |
| Built-in services (see "Built-in services" in [Analysis model](Analysis.Model.spec.md#built-in-services)) | None | Always |
| `IEnumerable<S>` | No key, each key that occurs, `AnyKey` | Each registered service type `S`. When a candidate is asynchronous, the entry is `IEnumerable<Task<S>>` instead. |
| Other wrapper types | As the demand needs | The view has a demand (see "Sources of demands" in [Analysis model](Analysis.Model.spec.md#sources-of-demands)) |

A wrapper type in the table includes a wrapper whose `T` only the fallback provider has. The container creates that wrapper, the same as at the dependency site (see [Behavior for each request](#behavior-for-each-request)). When a required and an optional dependency site ask for the same wrapper type and key, the entry resolves `T` as the required site does.

A `KeyValuePair<TKey, W>` whose value `W` is a wrapper, such as `KeyValuePair<string, Lazy<S>>`, is also a demand for `W` with the key of the dependency site. Thus the table has both the pair and `W` for that key.

A collection whose element is a wrapper, such as `IEnumerable<Lazy<S>>`, is also a demand for the wrapper of the element. Its entry follows the rule of a [single service](Analysis.Model.spec.md#single-service): with one candidate, the entry is the wrapper of that candidate. With more than one candidate, the entry throws `InvalidOperationException` (ambiguity), as the entry of `S` does. The service collection extension registers the wrapper for each candidate, so there MS.DI takes the last one.

For an inaccessible type, the container gets the `Type` from the type entry of the module (see "Type entry and wrapper entry" in [Module output](Module.Output.spec.md#type-entry-and-wrapper-entry)).

### Lookup rules

| Situation | Result |
| --- | --- |
| `(type, key)` has exactly one candidate, after the rule that a concrete registration wins | That candidate |
| More than one candidate stays | The container throws `InvalidOperationException` (ambiguity). It does not go to the fallback provider. |
| The type is an asynchronous service `S` itself | The type counts as not registered. The container does not go to the fallback provider. Only `Task<S>` can be resolved. |
| The type is `IEnumerable<S>` and a candidate is asynchronous | The container throws `InvalidOperationException`. Only `IEnumerable<Task<S>>` can be resolved. |
| The table does not have the type | With a fallback provider, the container goes to it (see [Fallback provider](#fallback-provider)). Without one, the result is `null`, and the `Required` version throws `InvalidOperationException`. An unknown `IEnumerable<T>` has the same result (see [Unknown `IEnumerable<T>`](#unknown-ienumerablet)). |
| `GetKeyedService(type, null)` | The same as a lookup without a key |
| `GetKeyedService(type, KeyedService.AnyKey)` asks for one service | The container throws `InvalidOperationException`. MS.DI does the same. |
| `GetKeyedServices(type, KeyedService.AnyKey)` | All registrations of that type that have a key. Registrations without a key are not included. |
| A collection type that the container knows, but the key has no registration | An empty collection (after the merge with the fallback provider) |

At run time, the container compares keys with `Equals`, as MS.DI does. The container reads the value of a symbol key one time, when the container type is initialized.

Two keys can have different compile-time identities and be equal at run time. When two such keys are registered on the same service type, the container throws `InvalidOperationException` when the container type is initialized. The message lists the two keys. Such keys are the responsibility of the user (see [Keyed services](Semantics.spec.md#keyed-services)). This design fails early, so that one key does not silently replace the other.

A key that only a dependency site asks for, such as the key of a keyed collection without a registration with that key, is not registered and does not throw. When it is equal at run time to a key that the table already has for the same element type, the lookup by `Type` uses `Equals` as usual: each collection type of that key, also one that only the dependency site asks for, gives the collection of the key that the table already has. The dependency site itself still uses its own collection, which matches keys by their compile-time identity (a [documented difference](../Constitution.md#8-documented-differences-from-msdi)).

### Unknown `IEnumerable<T>`

A request by `Type` can ask for an `IEnumerable<T>` that the container does not know at all. This means that `T` has no registration and that no demand for it exists at compile time. Without a fallback provider, the result is `null`, so `sp.GetServices<T>()` throws `InvalidOperationException`. MS.DI returns an empty collection. This is a [documented difference](../Constitution.md#8-documented-differences-from-msdi).

- A `GetServices<T>()` call that is visible at compile time is a demand (see "Resolve calls" in [Analysis model](Analysis.Model.spec.md#resolve-calls)). The generator generates a resolver for an empty collection, so this limit does not apply.
- The strongly typed `GetServices<T>()` and `GetKeyedServices<T>(key)` of the container are not affected either. `T` is known at compile time, and the methods return an empty collection when they find nothing.
- When the container knows the service type and the key of the request has no registration, the collection is empty (see the table above).
- With a fallback provider, the container passes the whole request to it, and the fallback provider decides. MS.DI returns an empty collection.
- It is technically possible to return an empty array. `Array.CreateInstance(element type, 0)` works under NativeAOT for an element that is a reference type (tested, see the ADRs). The user decided not to use this. The container continues to answer only for types that are known at compile time.

### `IsService` and `IsKeyedService`

When the lookup table has the `(type, key)`, the result is `true`. An ambiguous `(type, key)` counts too. Otherwise, when a fallback provider exists and supplies the related interface as a service (`GetService(typeof(IServiceProviderIsService))` or `GetService(typeof(IServiceProviderIsKeyedService))`), the container asks it. Otherwise the result is `false`. MS.DI supplies these interfaces as services, and its provider does not implement them (see [ADR IocRewrite/0017](../adr/0001_IocRewrite/0017_FallbackInterfacesAsServices.md)). For an asynchronous service `S` itself the result is `false`, and for `Task<S>` it is `true`. Without a fallback provider, the result for an unknown `IEnumerable<T>` is `false`. With one, the fallback provider decides.

## Wrapper types

The generator generates wrapper types from demands (see "Sources of demands" in [Analysis model](Analysis.Model.spec.md#sources-of-demands)). The table shows when the container creates the instance of each wrapper:

| Wrapper | `T` is Singleton | `T` is Scoped | `T` is Transient or comes from the fallback provider |
| --- | --- | --- | --- |
| `Func<T>` | The root caches one | Each scope caches one | Each instance (root or scope) caches one |
| `Func<A..., T>` | Not applicable (`T` must be Transient) | Not applicable | Each instance caches one |
| `Lazy<T>` | The root caches one | Each scope caches one | Each injection creates a new one |
| `Task<T>` (synchronous service) | The root caches one | Each scope caches one | Each injection creates one with `Task.FromResult` |
| `KeyValuePair<TKey, T>` | Created each time (value type) | Created each time (value type) | Created each time (value type) |

- A `Func` captures the instance (root or scope) that resolved it. Each later call resolves `T` on that instance.
- A `Lazy<T>` that the root or a scope caches uses `LazyThreadSafetyMode.PublicationOnly`. It does not keep an exception, so after a failure the next `Value` creates `T` again, the same as a resolution of `T` (see [Resolution of a closed definition](#resolution-of-a-closed-definition)). The resolver of `T` still creates only one instance. A `Lazy<T>` that each injection creates uses `ExecutionAndPublication`.
- `Func<A..., T>` creates `T` from the mapping (see "Mapping of `Func<..., T>`" in [Analysis model](Analysis.Model.spec.md#mapping-of-func-t)). A mapped constructor parameter takes the argument of the caller. The captured instance resolves the other parameters.
- A nested combination applies the same rules from the inside to the outside. The cache location of an outer layer follows the shortest lifetime among the inner layers.

## Collections and dictionaries

- The contents and the order follow "Multiple services" in [Analysis model](Analysis.Model.spec.md#multiple-services).
- The container supplies all six collection types as `T[]`.
- The container supplies all three dictionary types as `Dictionary<TKey, T>`.
- Caching has three cases. When all elements are Singleton, the root caches one collection. When the collection contains Scoped elements and no Transient element, each scope caches one. When it contains a Transient element, or when a merge with the fallback provider is necessary, each request creates a new one. MS.DI does the same for `IEnumerable<T>`. This is the precondition for collection resolution that is not worse than MS.DI. The returned array and dictionary are mutable. A consumer that changes them is the responsibility of the user, as in MS.DI.

## Fallback provider

The generated container supports a fallback provider, for integration with `IServiceCollection`. When `IntegrateServiceProvider = true`, the `IServiceProvider` that the constructor receives is the fallback provider.

With a fallback provider, the behavior changes as follows:

- When the container does not find a service, the request MUST go to the fallback provider. The container has priority.
- A keyed request can need to go to the fallback provider, so the generated code MUST keep run-time flexibility for it.
- A request for more than one service must merge the services of the fallback provider, so the generated code MUST keep run-time flexibility for it. The services of the fallback provider come first, and the services of the container come second. In one resolution, the services of the fallback provider appear one time only.
- A service inside the fallback provider cannot see the registrations of the container. This is a documented limit.

The subsections below give the details.

### Which provider resolves

| Where the resolution occurs | The container uses |
| --- | --- |
| The root (this includes the dependencies of a Singleton) | The fallback provider itself |
| A scope | The fallback scope of that scope |

- The scope creates the fallback scope with the `IServiceScopeFactory` of the fallback provider, the first time that the scope needs the fallback provider. A scope that never uses the fallback provider does not create one.
- When the fallback provider does not supply `IServiceScopeFactory`, all scopes use the fallback provider itself.
- Concurrent first uses leave only one fallback scope. The container disposes each extra fallback scope immediately. An exception of this disposal goes to the [dispose-exception callback](#dispose-exception-callback).
- The container scope that creates a fallback scope disposes it. See [Disposal of the fallback scope and the fallback provider](#disposal-of-the-fallback-scope-and-the-fallback-provider).
- A scope resolves from the fallback scope and not from the root of the fallback provider. Thus a Transient `IDisposable` of the fallback provider is disposed with the scope. Otherwise the root of the fallback provider holds it until the program ends.

### Behavior for each request

| Request | The container has a registration | The container has no registration |
| --- | --- | --- |
| One service `T` | The container. It does not ask the fallback provider. | `fallback.GetService(typeof(T))` |
| One keyed service | The container | When the fallback provider implements `IKeyedServiceProvider`, the container resolves from it. Otherwise the service counts as absent. |
| `IEnumerable<T>` and the other collections, when the element is the service type itself | Merge: the services of the fallback provider first, then the services of the container | The result of the fallback provider (when the container knows `T`). Otherwise the container passes the whole request to the fallback provider. |
| Keyed collection | Merge with the services of the fallback provider for the same key (when it implements `IKeyedServiceProvider`) | The same as the row above |
| Collection whose element is a wrapper (`Lazy<T>`, `Func<T>`, `Task<T>`, `KeyValuePair<TKey, T>`) | Only the registrations of the container | An empty collection |
| Dictionary | Only the registrations of the container | An empty dictionary |
| `Lazy<T>`, `Func<T>`, `Task<T>` | The container | The container creates the wrapper itself. Inside the wrapper, it resolves `T` from the fallback provider. |
| `Func<A..., T>` | The container | Compile-time error (`SGIOC305`) |
| `KeyValuePair<TKey, T>` | The container | `fallback.GetService(typeof(KeyValuePair<TKey, T>))`: the container asks for it unchanged, as a normal service type and without a key. The container cannot make a key for a service of the fallback provider. A result exists only when the fallback provider itself registers this type. |
| A run-time request by `Type` for a type that the table does not have (this includes wrapper types) | Not applicable | The container passes the request unchanged to the fallback provider. |

- For a merge, the container gets the services of the fallback provider one time, with `fallback.GetService(typeof(IEnumerable<T>))`. It does not get them again in the same resolution.
- The merge applies only to a collection whose element is the service type itself. To wrap the services of the fallback provider in `Lazy<T>` or `Func<T>`, the container must resolve them first, and then the wrapper does not defer anything. For a dictionary and a `KeyValuePair`, the container cannot get the key of each service from MS.DI.
- The container always creates the wrapper types. It does not ask the fallback provider for `Lazy<T>` or `Func<T>`. These types are reserved for automatic generation (the [limits](../Constitution.md#52-limits) of the constitution do not let the user register them), and MS.DI itself does not supply them. `KeyValuePair<TKey, T>` is the exception. When the container has no related registration, it cannot build the pair. It asks the fallback provider for the type unchanged (see the table above).
- The generator does not check the lifetime of a dependency that the fallback provider resolves. It reports `SGIOC501` (Info) at compile time.
- An optional dependency tries the container, then the fallback provider, then the default value. When the fallback provider returns `null`, the container uses the default value.
- An optional `Lazy<T>`, `Func<T>` or `Task<T>` whose `T` has no registration in the container is the exception, because the wrapper must not resolve `T` before it is used. With a fallback provider, the container creates the wrapper, and the wrapper asks the fallback provider for `T` with `GetService`: the value is `null` when the fallback provider does not have `T`. A required one uses `GetRequiredService` and throws. Without a fallback provider, the dependency site gets its default value.
- A value-type dependency is a `KeyValuePair<TKey, T>` or any value type for which the container cannot have a registration. When the fallback provider resolves it, the rules are the same as for a reference type, with one more step that unboxes the result. For a required dependency, the container throws `InvalidOperationException` when the fallback provider returns `null`. An optional dependency uses the default value. MS.DI does not prevent a non-generic registration of a value-type service.

## Eager resolution

`EagerResolveOptions`:

| Value | Behavior |
| --- | --- |
| `None` | Each service is created at its first request |
| `Singleton` (default) | The constructor of the root creates all Singleton services |
| `Scoped` | The creation of a scope creates all Scoped services |
| `SingletonAndScoped` | Both |

- "All" means each synchronous closed definition of that lifetime in the container view. This includes derived registrations. For each closed definition, the container creates the implementation instance and each decorator chain.
- Asynchronous closed definitions do not take part.
- The creation order is the topological order of the dependencies, with a dependency before the service that depends on it. Services with no relation follow the ordering (see "Ordering" in [Analysis model](Analysis.Model.spec.md#ordering)). During eager resolution, the container still gets each dependency through the internal resolver. It does not read the field directly, because a factory or a call through `IServiceProvider` can ask for a service that is not yet created.
- A Singleton that is created eagerly needs no synchronization. No other thread uses the container before the constructor of the root completes.
- When eager resolution throws an exception, the container disposes the instances that are already created, in the reverse topological order. Then the original exception continues to the caller. Otherwise a container whose constructor fails leaks the disposable objects that are already created.
  - The container disposes an instance synchronously when the instance implements `IDisposable`. Otherwise it starts `DisposeAsync` and does not wait for it. The caller cannot call `DisposeAsync` on a container whose constructor failed, so the rule of synchronous `Dispose` for an object that implements only `IAsyncDisposable` does not apply here.
  - An exception of this disposal goes to the [dispose-exception callback](#dispose-exception-callback).

## Thread safety

The generated container is thread-safe by default (see [Principles](../Constitution.md#4-principles)).

`ThreadSafeStrategy` has three values only: `None`, `Lock` (the default), and `CompareExchange`.

`ThreadSafeStrategy` affects only the first creation path of Singleton and Scoped services. A read after creation is not synchronized.

| Value | Concurrent first resolution |
| --- | --- |
| `None` | No guarantee. The container can create more than one instance. One instance stays, and the others are not disposed. Use this value when it is certain that only one thread uses the container. |
| `Lock` (default) | A double check with `System.Threading.Lock`. The instance is created only one time. |
| `CompareExchange` | No lock. The container can create more than one instance. `Interlocked.CompareExchange` keeps the first one. The container disposes each losing instance immediately if the instance is disposable. |

Lock allocation:

| Lifetime | Lock | Reason |
| --- | --- | --- |
| Singleton | One lock for each closed definition, in the state object that the root and all scopes share | Concurrent first resolution is common. With one shared lock, a slow constructor blocks the first creation of all other Singleton services. |
| Scoped | One lock for each container instance (scope or root), shared by all Scoped closed definitions | Concurrency inside one scope is rare. `CreateScope()` allocates only one more object. |

- The container holds the lock during the whole first creation. This includes the constructor, the injected members, and the creation of the decorator chains.
- An asynchronous closed definition always uses the same locks, except with `None` (see [Asynchronous closed definitions](#asynchronous-closed-definitions)). Thus a container with `CompareExchange` can still have locks.
- Requirement: a valid dependency graph MUST NOT deadlock. This includes a cycle through `Lazy<T>` or `Func<T>`, and a reentrant call into the container through `IServiceProvider` during creation. `System.Threading.Lock` is reentrant. A thread that enters the same lock again during creation does not block.
- With `CompareExchange`, the container disposes a losing instance only synchronously (`IDisposable`). It does not dispose a losing instance that implements only `IAsyncDisposable`, and it does not throw.

## Disposal

This section describes the generated container. MS.DI does the disposal for the service collection extension. For the differences, see [Holder](ServiceCollection.Output.spec.md#holder), [Decorator chain](ServiceCollection.Output.spec.md#decorator-chain) and [Number of disposals](ServiceCollection.Output.spec.md#number-of-disposals).

### What the container disposes

- The container disposes the objects that it creates: implementation instances, each decorator of each decorator chain, and the objects that Singleton and Scoped factories return.
- The container does not dispose a Transient service. A disposable Transient is an error on the container (`SGIOC403`). For the cases of this limit and its exceptions, see [Disposable Transient](Semantics.spec.md#disposable-transient).
- The container does not dispose the static object of an instance registration.
- Each object is disposed one time only. If the `Dispose` of a decorator calls the `Dispose` of its inner object itself, the inner object is called two times. The container does not remove the duplicate. By .NET convention, `Dispose` MUST be safe to call more than one time.
- MS.DI itself has no concept of a decorator. With the common practice (a wrap in a factory delegate), MS.DI tracks only the outermost layer that the factory returns, and the middle layers are not disposed. Both outputs of this library dispose each layer.
- The container disposes what the container creates. The container does not dispose what the user gives to it. For the fallback side, see [Disposal of the fallback scope and the fallback provider](#disposal-of-the-fallback-scope-and-the-fallback-provider).

### `Dispose` and `DisposeAsync`

- `DisposeAsync` calls `IAsyncDisposable.DisposeAsync` by preference. It calls `IDisposable.Dispose` only when the service does not implement `IAsyncDisposable`.
- `Dispose` calls `IDisposable.Dispose` only. A service that implements only `IAsyncDisposable` causes an exception on a synchronous `Dispose`, the same as MS.DI (see [Exceptions](#exceptions)).

### Order

The generator decides the disposal order at compile time. The container does not record the creation order at run time.

1. A scope: the Scoped instances of the scope, then its fallback scope.
2. The root: the Singleton instances, then the fallback provider that `ServiceProviderFactory` created.
3. Inside one lifetime: the reverse topological order of the dependency graph. A service that depends on another service is disposed first, and the service that it depends on is disposed later. Deferred edges (`Lazy`, `Func`) do not count in the topology. Services with no relation follow the reverse of the ordering (see "Ordering" in [Analysis model](Analysis.Model.spec.md#ordering)).
4. Inside one closed definition: each decorator chain from the outermost layer to the innermost layer, then the implementation instance.

For services that have a dependency relation, the result is the same as the reverse of the creation order, because a dependency is always created first. For services with no relation, the compile-time order can be different from the real creation order. The reason for this design is that the container does not have to keep a disposal list at run time.

The generator decides at compile time whether an object needs disposal. It generates disposal code only for an instance whose disposal kind is not "no disposal necessary" (see "Disposal kind" in [Analysis model](Analysis.Model.spec.md#disposal-kind)). The kind "run-time check" occurs only when the return type of a Singleton or Scoped factory does not show at compile time whether the object is disposable (an interface or a class that is not sealed). For such an instance, the container uses a type test at disposal. The container skips an instance that is not yet created.

### Disposal of the fallback scope and the fallback provider

Principle (see [What the container disposes](#what-the-container-disposes)): the container disposes what the container creates. The container does not dispose what the user gives to it.

| Object | Who creates it | Who disposes it | When |
| --- | --- | --- | --- |
| The fallback provider that the user passes to the constructor | The user | The user. The container never disposes it. | Not applicable |
| The fallback provider that `ServiceProviderFactory.CreateServiceProvider` creates | The infrastructure of the container | The root that this call returns | After all Singleton services of the root are disposed. It is the last step of root disposal. |
| The fallback scope | A scope of the container (the first time that it needs the fallback provider) | The container scope that created it | After all Scoped instances of the scope are disposed. It is the last step of scope disposal. |

The reason for the order: a service of the container can depend on a service of the fallback provider. The reverse is not possible, because the fallback provider cannot see the registrations of the container. Thus the fallback side is always disposed last, and a service of the container can still use the fallback services in its own `Dispose`.

How the container disposes them:

| Method called on the container | Effect on the fallback scope or on the fallback provider that the container owns |
| --- | --- |
| `DisposeAsync` | When the object implements `IAsyncDisposable`, the container awaits its `DisposeAsync()`. The scope and the provider of MS.DI both implement it. Otherwise the container calls `Dispose()`. |
| `Dispose` | The container calls `IDisposable.Dispose()`. If the fallback scope holds a service that implements only `IAsyncDisposable`, MS.DI throws `InvalidOperationException` here. |

- An exception that the fallback side throws during disposal counts in the aggregation of [Exceptions](#exceptions), the same as an exception from a service of the container. It does not stop disposal, and the container does not swallow it. This is the last step, so all services of the container are already disposed.
- A scope that never used the fallback provider has no fallback scope and has nothing to dispose.
- When the fallback provider does not supply `IServiceScopeFactory`, a scope uses the fallback provider itself and has no fallback scope. Disposal of the container scope does nothing to the fallback provider.
- The root does not create a fallback scope. A resolution on the root uses the fallback provider itself. Disposal of the root does nothing to the fallback provider, except in the case of the second row of the first table above.
- The root does not track scopes. Thus disposal of the root does not dispose the fallback scope of a scope that is not yet disposed. Each scope MUST be disposed separately. After the root disposes a fallback provider that the container owns, MS.DI decides the behavior of the fallback scope of each scope that is not yet disposed.
- The fallback scope is created lazily, so its creation can be concurrent with the disposal of the scope. The rule is the same as for a normal instance (see [After disposal, and concurrency](#after-disposal-and-concurrency)). The creating side checks one more time, after it stores the fallback scope, whether disposal has started. The two sides compete for the right to dispose with an atomic operation, so that each fallback scope is disposed exactly one time. In this case the creating side disposes the fallback scope synchronously. It MUST NOT block to wait for `DisposeAsync`. An exception from this disposal cannot replace the `ObjectDisposedException` that is being thrown, so it goes to the [dispose-exception callback](#dispose-exception-callback).

### Exceptions

- When an instance throws during disposal, the container continues to dispose the others. After all are complete, the container throws the exception unchanged if there is only one. It wraps more than one exception in an `AggregateException`.
  - MS.DI behaves differently. It throws the exception unchanged and stops. The services created before that one are not disposed, and they cannot be disposed later (tested, see the ADRs).
- When synchronous `Dispose` finds an instance that implements only `IAsyncDisposable`, it produces an `InvalidOperationException`. This exception counts in the aggregation too, and disposal continues.

#### Dispose-exception callback

The global dispose-exception callback is this:

```csharp
public static class IocContainerGlobalOptions
{
    public static Action<Exception>? OnDisposeException { get; set; }
}
```

The container always aggregates and throws each exception that its `Dispose` and `DisposeAsync` can throw, by the rules above. Such an exception does not go through the callback. The callback receives only the exceptions that cannot be thrown to a caller:

| Source | Situation |
| --- | --- |
| Container | During synchronous `Dispose`, the task of an asynchronous service is not yet complete. A continuation disposes the result later, and that disposal throws (see [Asynchronous closed definitions](#asynchronous-closed-definitions-1)). |
| Container | The creation of a fallback scope is concurrent with the disposal of the scope, and the disposal by the creating side throws (see [Disposal of the fallback scope and the fallback provider](#disposal-of-the-fallback-scope-and-the-fallback-provider)). |
| Container | The creation of an instance is concurrent with the disposal of the container, the creating side gets the right to dispose the instance, and that disposal throws (see [After disposal, and concurrency](#after-disposal-and-concurrency)). |
| Container | With `CompareExchange`, the disposal of a losing instance throws (see [Thread safety](#thread-safety)). |
| Container | Concurrent first uses create more than one fallback scope, and the disposal of an extra one throws (see [Which provider resolves](#which-provider-resolves)). |
| Container | The constructor of the root throws in `ServiceProviderFactory.CreateServiceProvider`, and the disposal of the provider that it created throws (see [Integration with IServiceCollection](#integration-with-iservicecollection)). |
| Both | Creation fails, and the disposal of the objects that are already created throws. The original exception is thrown as usual. |
| Service collection extension | During synchronous `Dispose`, the task of an asynchronous service is not yet complete. The continuation of the holder disposes the result later, and that disposal throws (see "Disposal of `OwnTask`" in [Service collection extension output](ServiceCollection.Output.spec.md#disposal-of-owntask)). The holder always throws its other exceptions. |

- The callback receives each exception separately. When the disposal of more than one object fails in one situation, for example several layers of a decorator chain, the callback is called one time for each exception. It does not receive an `AggregateException` that collects them. An `AggregateException` that user code throws itself is one exception, and the callback receives it unchanged.
- When the callback is `null` (default), these exceptions are discarded.
- The callback can be called on any thread. It can also be called concurrently.
- An exception that the callback itself throws gets no special handling.

### Asynchronous closed definitions

| State of the task at disposal | `DisposeAsync` | `Dispose` |
| --- | --- | --- |
| Not yet created | Skip | Skip |
| Completed successfully | Dispose the result and its decorator chains | Dispose the result and its decorator chains |
| Faulted or canceled | Skip (no instance exists) | Skip |
| Not yet complete | Wait until it completes (ignore its exception). If it succeeds, dispose the result. | Do not wait. Attach a continuation that disposes the result after the task completes successfully. An exception in the continuation goes to the [dispose-exception callback](#dispose-exception-callback). |

- Disposal MUST NOT read the exception of a task that already failed. If it did, a failure of creation that no code awaited would give no signal. The caller that got the `Task<S>` awaits it and receives the exception. When no caller awaits a task that failed before disposal, the exception reaches `TaskScheduler.UnobservedTaskException`. When disposal waits for a task that is not complete, the wait observes the exception as a side effect, and disposal does not report it. The dispose-exception callback does not receive the failure of a creation. See [ADR IocRewrite/0020](../adr/0001_IocRewrite/0020_DisposalDoesNotObserveCreationFailure.md).

### After disposal, and concurrency

- `Dispose` and `DisposeAsync` can be called more than one time. The second and later calls have no effect.
- After disposal starts, the public resolution methods (the `GetService` family, the partial accessors, `CreateScope`) throw `ObjectDisposedException`. Only the public entry points do this check. The internal resolvers do not.
- The public resolution methods of a scope also throw `ObjectDisposedException` after the disposal of the root starts. The root disposes the Singleton instances. A scope that resolved a Singleton after that would create it again, and nothing would dispose the new instance. MS.DI also throws in this case.
- When resolution and disposal are concurrent, the container guarantees that it does not leak. Each Singleton or Scoped instance that the container creates and that needs disposal is disposed exactly one time. To do this, the creating side checks one more time, after it stores the instance, whether disposal has started. If it has, the two sides compete for the right to dispose that instance with an atomic operation. The side that gets the right disposes the instance. The creating side throws `ObjectDisposedException`.
  - When the creating side gets the right, it disposes the instance synchronously when the instance implements `IDisposable`. Otherwise it starts `DisposeAsync` and does not wait for it. It MUST NOT block, because the caller is a resolution and not a disposal. An exception of this disposal goes to the [dispose-exception callback](#dispose-exception-callback), because the `ObjectDisposedException` is thrown in its place.
- For an instance that is created concurrently with disposal, the disposal order is not guaranteed to follow [Order](#order).
- The same rules apply to the fallback scope.

## Partial accessors

A partial accessor is the fast path of the container. The user declares a partial method or a partial property in the container class to skip the lookup by `Type` and get the service instance directly. For the shape, see "Partial accessor" in [Annotations](Annotations.spec.md#partial-accessor).

- The generator generates the implementation part. It calls the internal resolver directly and does not use the lookup table.
- A partial accessor is a dependency site. All rules and diagnostics of [Dependency resolution](Semantics.spec.md#dependency-resolution) apply. A return type that is a reference type annotated as nullable means that the dependency is optional. A nullable value type is required.
- The return type MAY be a service type or any valid wrapper type.
- The user gives the key with `[IocInject]` on the member.
- An accessor that gets a Scoped service on the root throws `InvalidOperationException`. On a scope it works normally.
- After disposal starts, an accessor throws `ObjectDisposedException`.

```csharp
#region Define:
[IocContainer(IntegrateServiceProvider = false)]
public partial class AppContainer
{
    public partial IClock Clock { get; }

    [IocInject(Key = "main")]
    public partial IRepo GetMainRepo();

    public partial Lazy<IReport>? Report { get; }      // optional

    public partial Task<IAsyncService> AsyncService { get; }
}
#endregion
```

## Integration with IServiceCollection

When `IntegrateServiceProvider = true` and the project references `Microsoft.Extensions.DependencyInjection` (which supplies `BuildServiceProvider`), the generator generates this class inside the container class:

```csharp
public sealed class ServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;
    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder);
}
```

- `CreateServiceProvider` builds the MS.DI provider from `containerBuilder`. It creates the root with that provider as the fallback provider.
- The factory is an independent nested class. The container does not implement it. Thus nobody has to create a container instance only to get the factory.
- When the constructor of the root throws, for example during eager resolution, `CreateServiceProvider` disposes the provider that it created and throws the original exception again.
- The root that `CreateServiceProvider` returns disposes the provider that `CreateServiceProvider` created, as the last step of its disposal. The host disposes only the container that the factory returns. If the container does not dispose the provider, nobody does. The container never disposes a provider that the user passes to the constructor. See [Disposal of the fallback scope and the fallback provider](#disposal-of-the-fallback-scope-and-the-fallback-provider).

## Imported instance definitions

The container handles an imported instance definition as a normal closed definition. Only the generated code is different:

| Item | Method |
| --- | --- |
| Creation | Call the construction entry and the decorator entry of the module (see [Construction entry](Module.Output.spec.md#construction-entry) and [Decorator entry](Module.Output.spec.md#decorator-entry) in Module output) |
| Storage | The field type is the return type of the entry (it can be `object`) |
| Use as an accessible service type | A run-time cast |
| Use as an inaccessible parameter of another entry | Pass the `object` directly |
| Registration of an inaccessible service type in the lookup table | Get the `Type` from the type entry |
| Inaccessible wrapper type | Create it with the wrapper entry (see "Type entry and wrapper entry" in [Module output](Module.Output.spec.md#type-entry-and-wrapper-entry)). The container wraps the inner resolution in an erased form such as `Func<object>` and passes it in. |
| Disposal | Follow `disposable` and `asyncDisposable` of the module info |

## Requirements for the generated code

- Start of the file: `// <auto-generated/>`, `#nullable enable annotations`, `#nullable disable warnings`, `#pragma warning disable SGIOCEXP001` (see "Types only for generated code" in [SPEC](SPEC.spec.md#types-only-for-generated-code)). The files of all three outputs are the same in this respect.
- The generated code marks what the generator declares with `[global::System.CodeDom.Compiler.GeneratedCode("SourceGen.Ioc", "{version}")]`. The files of all three outputs are the same in this respect.
  - A type that the generator declares completely has the attribute on the type. Examples are `{Name}ServiceCollectionExtensions`, the carrier type, `__IocModule`, and `ServiceProviderFactory`.
  - A partial declaration of a type that the user declares (the container class, the module class, and their containing types) MUST NOT have the attribute. Each member and each nested type that the generator declares in it has the attribute instead. `GeneratedCodeAttribute` has `AllowMultiple = false`. When one class is both a container and a module, or when two containers are nested in the same type, two generated partial declarations with the attribute cause the compile error CS0579. Roslyn also treats every member of a type with the attribute as generated code, so analyzers would skip the code that the user writes in that type.
  - `{version}` is `major.minor.patch` of the generator. It does not contain the fourth part (the git height), so the generated code changes only when the version of the generator changes.
- The code references each type by its name qualified with `global::`.
- The code does not use `var`. Each local variable has an explicit type. The files of all three outputs are the same in this respect.
- The names of generated members MUST be deterministic. They MUST NOT conflict with each other or with the members that the user declares in the container class, including its base classes. Infrastructure members have the prefix `__`.
- A public member that the generator generates and that the user is not meant to call directly has `[EditorBrowsable(EditorBrowsableState.Never)]`.
- The code does not use reflection, `MakeGenericType`, `Activator`, or `Array.CreateInstance`. A NativeAOT publish MUST NOT have warnings.
- The lookup table uses a statically initialized structure, for example `FrozenDictionary`. The generator does not generate code that gets a service with a switch, and it gives the user no option for it. The decision to add a switch waits until the core is complete and depends on measurements.
- The generated file MUST keep the binding cost of the C# compiler linear. The items of the lookup table use an explicit type and not a target-typed `new`.

## Example

```csharp
#region Define:
public interface IClock;
public interface IOrderService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
internal sealed class SystemClock : IClock;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IOrderService)])]
internal sealed class OrderService(IClock clock) : IOrderService, IDisposable
{
    public void Dispose() { }
}

[IocContainer(IntegrateServiceProvider = false)]
public partial class AppContainer;
#endregion

#region Generate:
// Illustration: the implementation decides the names and the structure. The other interface members
// and the [GeneratedCode] attribute on each generated member are omitted.
partial class AppContainer : global::SourceGen.Ioc.IIocContainer,
    global::Microsoft.Extensions.DependencyInjection.IServiceScope
{
    private sealed class __Singletons
    {
        public global::SystemClock SystemClock = null!;   // eager resolution, no synchronization necessary
    }

    private readonly __Singletons __singletons;
    private readonly bool __isRoot;
    private int __disposed;

    public AppContainer()
    {
        __singletons = new();
        __isRoot = true;
        __singletons.SystemClock = new global::SystemClock();
    }

    private AppContainer(global::AppContainer root)
    {
        __singletons = root.__singletons;
    }

    public global::AppContainer CreateScope()
    {
        __ThrowIfDisposed();
        return new global::AppContainer(this);
    }

    // Singleton: one indirect read
    private global::SystemClock __SystemClock() => __singletons.SystemClock;

    // Scoped: the fast path and the creation path are separate
    private global::OrderService? __orderService;
    private readonly global::System.Threading.Lock __scopedLock = new();   // shared by all Scoped services of this instance

    private global::OrderService __OrderService() => __orderService ?? __CreateOrderService();

    private global::OrderService __CreateOrderService()
    {
        if (__isRoot) throw new global::System.InvalidOperationException(
            "Cannot resolve scoped service 'OrderService' from the root container.");

        lock (__scopedLock)
        {
            if (__orderService is { } existing) return existing;
            global::OrderService instance = new global::OrderService((global::IClock)__SystemClock());
            __orderService = instance;
        }
        // Check only after the store: concurrent disposal disposes the instance exactly one time
        __ThrowIfDisposedAfterCreate(ref __orderService);
        return __orderService;
    }

    public void Dispose()
    {
        if (global::System.Threading.Interlocked.Exchange(ref __disposed, 1) != 0) return;
        // OrderService implements IDisposable (known at compile time). SystemClock needs no disposal.
        global::System.Threading.Interlocked.Exchange(ref __orderService, null)?.Dispose();
    }
}
#endregion
```

## See also

- [Semantics](Semantics.spec.md)
- [Analysis model](Analysis.Model.spec.md)
- [Module output](Module.Output.spec.md)
- [Service collection extension output](ServiceCollection.Output.spec.md)
- [Diagnostics](Diagnostics.spec.md)
