# Service collection extension output

> Parent document: [Constitution](../Constitution.md). If this document and the constitution disagree, the constitution wins.

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals.

## Overview

An assembly marked with `[assembly: IocServiceCollectionExtensions]` generates one service collection extension: an `IServiceCollection` extension method named `Add{Name}`. The method is opt-in (see [Conditions for generation](#conditions-for-generation)). It registers the complete dependency graph of the [assembly view](Analysis.Model.spec.md#views) (Analysis model, section "Views") in the `IServiceCollection`. MS.DI owns the instances: MS.DI handles lifetime, caching, the selection of a single service, and disposal.

As with the container, the service collection extension registers everything. The complete dependency graph has two parts:

- The instance definitions of the scope of this assembly.
- The instance definitions of all imported modules, indirect imports included.

The method creates the internal types of other assemblies through the construction entries of the modules.

The service collection extension supports all features of [Semantics](Semantics.spec.md). It implements them with `sp => ...` factory delegates. The exceptions are:

- An instance registration (see [Registration for each lifetime](#registration-for-each-lifetime)).
- An asynchronous service (see [Asynchronous closed definitions](#asynchronous-closed-definitions)).

The service collection extension and the container use the same [construction plan](Analysis.Model.spec.md#construction-plan) (Analysis model, section "Construction plan"). The differences are:

| Item | Container | Service collection extension |
| --- | --- | --- |
| Tags | Compile time (`IncludeTags`) | Run time (a parameter of the method) |
| Checks | Full check | Definite check, plus `SGIOC405` and `SGIOC504` |
| A single service with more than one candidate | Compile-time error | MS.DI takes the last registration. A [definite ambiguity](#definite-ambiguity) is a Warning |
| Missing dependency | Compile-time error or fallback | MS.DI throws at run time |
| Disposable Transient | Compile-time error | Allowed. MS.DI disposes it |

## Conditions for generation

The service collection extension is opt-in. The generator generates it only for an assembly that has the assembly-level attribute below.

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
[Conditional("SOURCEGEN")]
public sealed class IocServiceCollectionExtensionsAttribute : Attribute
{
    public string? Name { get; init; }
}
```

```csharp
[assembly: IocServiceCollectionExtensions(Name = "App")]
```

- The name of the attribute matches the generated class `{Name}ServiceCollectionExtensions`. The `Name` property of the attribute gives the `{Name}` part of the method name. The attribute is the only source of the method name.
- An assembly without this attribute does not generate a service collection extension. The definite check, the module output, and the containers are not affected.
- When the assembly has this attribute but the assembly view has no instance definition (its own or imported), the generator still generates the method. The method does only the [guard against duplicate registration](#guard-against-duplicate-registration).
- When the definite check has an Error, the generator does not generate the method (see [Checks](#checks)).
- Reason: each service collection extension contains all the modules that it imports. If each assembly generated one, the registration code of a module that is imported through several levels would compile again at each level. The user could also think that the method of each module must be called.

## Method

```csharp
namespace {RootNamespace};

public static class {Name}ServiceCollectionExtensions
{
    public static IServiceCollection Add{Name}(this IServiceCollection services, params ReadOnlySpan<string> tags);
}
```

| Item | Source |
| --- | --- |
| `{Name}` | `IocServiceCollectionExtensionsAttribute.Name`. When it is not set, the assembly name. Characters that are not identifier characters (including `.`) are removed. When no character remains, or when the first character that remains cannot start a C# identifier (for example a digit), the generator reports `SGIOC108` and does not generate the method |
| Namespace | The MSBuild property `RootNamespace`. When it is not set, the assembly name. The generator does not check the namespace. When it is not a valid namespace, the compiler reports an error in the generated file |
| `tags` | The tags to include. An instance definition without tags is always registered. An instance definition with tags is registered only when at least one of its tags is in `tags` |

- The class name contains `{Name}`. This prevents two classes with the same name when two assemblies use the same namespace.
- The user calls only the service collection extension of the final assembly. A module MUST NOT be imported and also have its own service collection extension called separately. On a violation, the [guard against duplicate registration](#guard-against-duplicate-registration) throws at run time.

## Guard against duplicate registration

Before it registers any service, the service collection extension checks for markers and leaves markers. The `SourceGen.Ioc` library gives:

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001")]
public sealed class IocRegistrationMarker
{
    public string Assembly { get; }   // The name of the registered assembly
    public string Method { get; }     // The method that registered it, for example "App.AppServiceCollectionExtensions.AddApp"

    // Checks if one of the assemblies already has a marker. If yes, it throws. If no, it adds a marker for each one
    public static void Enter(IServiceCollection services, string method, params ReadOnlySpan<string> assemblies);
}
```

- `assemblies` holds all the assemblies that the assembly view covers: this assembly and the assemblies of all imported modules, including indirect imports. They are sorted by name with ordinal comparison.
- A marker is registered as an instance with `IocRegistrationMarker` as the service type. MS.DI does not dispose an object that is registered as an instance. The check scans `services` linearly one time.
- When one of the `assemblies` already has a marker, the method throws `InvalidOperationException`. The message names that assembly, the method that registered it before, and the rule "call only the method of the final assembly". In this case `services` is not modified.
- The guard stops three situations:
  - The same service collection extension is called two times. This includes one call for each of two different sets of tags: all tags MUST be given in the same call.
  - A module is imported, and its own service collection extension is also called separately, in either order.
  - The host calls two service collection extensions that import the same module. This combination is not supported. In this case the user MUST create a final assembly that imports both.
- This is not a compile-time check. A compile-time diagnostic is left for a later evaluation, because the project of the caller does not always have the generator.

## Registration order

The method processes the closed definitions one by one in their [ordering](Analysis.Model.spec.md#ordering) (Analysis model, section "Ordering"): derived registrations first, then concrete registrations. For a single service, MS.DI takes the last registration. For a collection, MS.DI uses the registration order. Thus:

- A concrete registration wins over a derived registration, the same as in the container.
- The order of a collection is the same as in the container, without the merge with the fallback.
- When more than one concrete registration gives the same "service type + key", the last one in the ordering wins. In the container this situation is a compile-time error.
  - The sort name decides the winner. The assembly that declares the registration and the direction of the imports have no effect. **A registration of this assembly does not override a registration of an imported module.** This differs from the MS.DI convention that a later registration overrides an earlier one.
  - To replace an implementation that a module gives, use a different key. As an alternative, the module gives that registration with a tag, and the composition root decides whether to enable it.
  - This applies only inside one service collection extension. A service that the user registers after the call to the service collection extension still overrides it by the rules of MS.DI.

## Tags

At its start, the method converts `tags` to one boolean value for each tag. The registration of each closed definition that has tags is inside the related condition.

- When a derived registration records a [suppressor](Analysis.Model.spec.md#suppression) (Analysis model, section "Suppression"), it is registered only when the suppressor is not included. The method filters first, then suppresses.
- The registrations of decorator chains, wrapper types, and collections follow the closed definition that they belong to.
- The content of a dictionary is decided by the tags at the call to the service collection extension. When one key still has more than one instance definition after concrete registrations win, the service collection extension throws `InvalidOperationException`.

## Registration of closed definitions

### Creation method

The generator generates one private static creation method for each closed definition. Its content is the construction plan:

```csharp
private static global::Mod.Service __Create_Service(global::System.IServiceProvider sp)
    => new global::Mod.Service(
        global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<global::Mod.IRepo>(sp));
```

Each dependency is always resolved from `sp` (see [Dependency resolution](#dependency-resolution)). The target is not decided at compile time.

An `in` or `ref readonly` parameter gets its value through a local variable of the creation method, and the call passes the variable with `in`. A value that is passed directly to a `ref readonly` parameter gives warning CS9193. The generated code MUST NOT have warnings. A `ref` or `out` parameter cannot take a resolved value. The compiler reports that error in the generated code. The generator reports no diagnostic (see the [constitution](../Constitution.md)).

### Holder

MS.DI can address a registration only by "service type + key". All service types of one closed definition must share one instance, and a wrapper type must be able to point to a specific closed definition. For this, the instance of a Singleton or Scoped closed definition is put in a **holder**, which is registered with a key that is private to the generator. A decorator chain has its own holder (see [Decorator chain](#decorator-chain)). A Transient closed definition can have a holder only when it has a decorator chain.

The `SourceGen.Ioc` library gives:

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001")]
public sealed class IocHolder<T> : IDisposable, IAsyncDisposable
{
    public T Value { get; }
}

[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001")]
public static class IocHolder
{
    // On disposal, disposes value
    public static IocHolder<T> Own<T>(T value);
    // On disposal, does not dispose value. It disposes only inner, in sequence (the middle layers of the decorator chain, outer layers first. For Transient, the last one is the implementation instance)
    public static IocHolder<T> Chain<T>(T value, params ReadOnlySpan<object> inner);
    // Holds the Task of an asynchronous service. On disposal, it handles the result of the Task (see "Asynchronous closed definitions")
    public static IocHolder<Task<T>> OwnTask<T>(Task<T> task);
}
```

`IocHolder` is the only type that is responsible for disposal. Synchronous and asynchronous services share it. Thus there is only one set of disposal rules.

- The service type is `IocHolder<instance type>`. When the instance type is inaccessible, it is `IocHolder<object>`. The key is a `private static readonly object` in the generated class.
- The registration uses the holder type and not the real service type, so that it does not pollute the result of `GetKeyedServices<S>(KeyedService.AnyKey)`. MS.DI includes all keyed registrations of the same service type in that result.
- The holder is the only object that MS.DI always tracks. On disposal, it disposes the objects that it owns. The holder of an instance disposes the instance. The holder of a decorator chain disposes the middle layers and, for Transient, also the implementation instance.
- An exception during the disposal of a holder follows the rules of the [container](Container.Output.spec.md#exceptions) (Container output, section "Exceptions"):
  - When the holder owns more than one object and one of them throws, the holder continues to dispose the others and throws after all are done. When there is only one exception, the holder throws it unchanged. When there is more than one, the holder wraps them in an `AggregateException`.
  - `DisposeAsync` calls `IAsyncDisposable.DisposeAsync` first. It calls `IDisposable.Dispose` only when the object does not implement `IAsyncDisposable`.
  - When synchronous `Dispose` finds an object that implements only `IAsyncDisposable`, the holder does not dispose it. The holder creates an `InvalidOperationException`, which is counted the same way. Its message follows MS.DI: the type implements only `IAsyncDisposable`, and the caller should use `DisposeAsync`. MS.DI does the same for the objects that it tracks itself. Thus the result of synchronous disposal is the same for one definition, whichever service type the caller resolved.
  - After a holder throws an exception, MS.DI stops the disposal of the other services, the same as for an ordinary service.

### Registration for each lifetime

`n` is a closed definition. `S` is one of its service types, including its own type.

| Lifetime | Hidden registration | Registration of each service type `S` |
| --- | --- | --- |
| Singleton, Scoped | `IocHolder<instance type>` with the same lifetime. The factory delegate calls the creation method | The same lifetime. The factory delegate takes the instance from the holder and casts it to `S` |
| Transient | None (a service type with a decorator chain can have a holder of the chain, see [Decorator chain](#decorator-chain)) | Transient. The factory delegate calls the creation method directly |
| Instance registration | None | MUST use the overload that **takes the instance directly**. It MUST NOT use a factory delegate. With a factory delegate, MS.DI would dispose that static object |

- For consistency, a Singleton or Scoped closed definition always uses a holder, also when it has only one service type.
- When the service type has a key, the method uses the keyed registration methods. For the key expression, see [Module output](Module.Output.spec.md#key) (section "Key").
- When the type is accessible, the method uses the generic registration methods. A static cast `(S)instance` makes a type error appear at compile time. When the type is inaccessible (an imported internal type), the method gets the `Type` from the [type entry](Module.Output.spec.md#type-entry-and-wrapper-entry) of the module (Module output, section "Type entry and wrapper entry") and creates a non-generic `ServiceDescriptor`.
- An imported closed definition is created with the [construction entry](Module.Output.spec.md#construction-entry) of the module (Module output, section "Construction entry").

### Decorator chain

For a service type `S` that has a decorator chain:

| Lifetime | Hidden registration | Registration of `S` |
| --- | --- | --- |
| Singleton, Scoped | `IocHolder<S>`: its value is the outermost layer, and it owns the middle layers | Takes the outermost layer from the holder of the chain |
| Transient | When one of the middle layers or the implementation instance is disposable: `IocHolder<S>` (Transient). Its value is the outermost layer, and it owns the middle layers and the implementation instance. In all other cases: none. A decorator chain occurs only on a registration of an implementation type, so the generator knows at compile time whether the objects are disposable | When there is a holder of the chain, takes the outermost layer from it. If not, creates the complete chain directly and returns the outermost layer |

Each decorator layer MUST be disposed, and the decorated implementation instance too. MS.DI tracks only the objects that a factory delegate returns. Thus the generated code MUST give each other object to MS.DI to track separately, through a holder:

- The outermost layer: the registration of `S` returns it, and MS.DI tracks it.
- The middle layers: the holder of the chain disposes them, outer layers first and inner layers last.
- The implementation instance: for Singleton and Scoped, the holder of the instance disposes it. For Transient, there is no holder of the instance, and the implementation instance is not the return value of a factory delegate. Thus MS.DI does not track it, and without special handling its `Dispose` is not called (tested, see the ADRs). For this reason the holder of the chain disposes it after the middle layers.

MS.DI disposes the objects that it tracks in the reverse order of their creation. The creation order is the holder of the instance (Singleton and Scoped), the holder of the chain, the outermost layer. Thus the disposal order is the outermost layer, the middle layers, the implementation instance. This is the same as in the container. The facts below hold for all three lifetimes (tested, see the ADRs):

- The order is as given above.
- For Transient, each resolution gets an independent chain, and each object is disposed exactly one time.
- A keyed definition behaves the same.
- The holders are not in the results of `GetServices<S>()` and `GetKeyedServices<S>(AnyKey)`.

### Number of disposals

MS.DI tracks each object that a factory delegate returns. Thus the same Singleton or Scoped instance is disposed more than one time: one time by the holder of the instance, and one time by each service type registration that returns it. For Transient, each service type creates its own instance, and each object is disposed only one time. This is [documented difference 21](../Constitution.md#8-documented-differences-from-msdi) of the constitution. By .NET convention, `Dispose` MUST be safe to call more than one time.

## Asynchronous closed definitions

The service collection extension does not register `Task<T>`. MS.DI tracks and disposes the Task that a factory delegate returns. A Task that is not complete throws when it is disposed, and this stops the disposal of the other services (tested, see the ADRs).

The method registers an asynchronous service with a hidden holder. The holder caches the Task and, on disposal, disposes the result of the Task. The dependency site gets the Task through a stand-in type that disposes nothing. Thus an asynchronous service needs two roles. Two types fill them.

| Role | Type | Does it dispose something? |
| --- | --- | --- |
| Owner: caches the Task and, on disposal, disposes its result | `IocHolder<Task<T>>`, created with `IocHolder.OwnTask` | Yes |
| Stand-in for `Task<S>`: lets a dependency site get the Task by "service type + key" | `IocTask<S>` | No. It does not implement `IDisposable` |

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001")]
public sealed class IocTask<T>(Task<T> task)
{
    public Task<T> Task { get; } = task;
}
```

| Registration | Service type | Key | Content |
| --- | --- | --- | --- |
| Hidden owner | `IocHolder<Task<instance type>>` | The private key | `IocHolder.OwnTask(asynchronous creation method(sp))` |
| Each service type `S` | `IocTask<S>` | The key of the closed definition | A projection of the Task of the owner to `Task<S>` |

- All these registrations have the same lifetime as the closed definition. A Transient closed definition also has an owner, so that MS.DI tracks the owner and disposes the result.
- The sources of the keys:
  - The private key is a `private static readonly object` field in the generated class of the service collection extension. There is one for each closed definition, the same as for the [holder](#holder). Code outside the class cannot get this object, so it cannot collide with another registration.
  - The key of the closed definition is the key that the user gives on the registration attribute. When no key is given, the registration is not keyed.
- When a dependency site asks for `Task<S>`, the generated code resolves `IocTask<S>` from `sp` and reads `Task`. When there is more than one candidate, MS.DI takes the last registration. `IEnumerable<Task<S>>` gets all of them from `sp.GetServices<IocTask<S>>()`.
- The `S` of an asynchronous service has no registration itself. `sp.GetService<S>()` is `null`, and `sp.GetServices<S>()` has only the synchronous candidates ([documented differences 19 and 20](../Constitution.md#8-documented-differences-from-msdi)).

### Disposal of `OwnTask`

The behavior depends on the state of the Task at disposal. It is the same as in the [container](Container.Output.spec.md#asynchronous-closed-definitions-1) (Container output, section "Asynchronous closed definitions" under "Disposal").

| State of the Task | `DisposeAsync` | `Dispose` |
| --- | --- | --- |
| Completed successfully | Disposes the result | The same |
| Faulted or canceled | Skips. The creation method itself disposes the objects that it created when it fails (see [Failure during creation](#failure-during-creation)) | Skips |
| Not complete | Waits for it to complete and ignores its exception. On success, disposes the result | Does not wait. Attaches a continuation that disposes the result after the Task completes successfully |

- An exception from the disposal of the result is **thrown** by the rules of the [holder](#holder), the same as for a synchronous service. MS.DI then stops the disposal of the other services. The holder does not give these exceptions to the dispose-exception callback. When the disposal of an object that MS.DI tracks directly fails, the exception is always thrown. If the holder hid the exception, the same kind of error would or would not stop the disposal, as a function of whether a holder wraps the object.
- Only the continuation in the lower right cell of the table gives its exception to [`IocContainerGlobalOptions.OnDisposeException`](Container.Output.spec.md#dispose-exception-callback) (Container output, section "Dispose-exception callback"). When the continuation runs, `Dispose` returned long before, and no caller can receive the exception. When the result is a decorator chain and more than one layer fails, each exception goes to the callback separately.
- The continuation does not have the limits of synchronous `Dispose`. It disposes the result by the rules of `DisposeAsync` (it calls `IAsyncDisposable.DisposeAsync` first). Thus a result that implements only `IAsyncDisposable` is also disposed.
- A Task that is not complete at synchronous `Dispose` is a situation with **no guarantee** (tested, see the ADRs). The continuation guarantees only that each object is disposed exactly one time in the end. It does not guarantee the order: with a decorator chain, the implementation instance can be disposed before a decorator. It also does not guarantee that the dependencies used during creation are still valid: the scope is already disposed, and a decorator created after that can get a disposed dependency. When there are asynchronous services, the user SHOULD use `DisposeAsync`. It waits for the initialization to complete, and the order and the validity are correct.
- Only the owner disposes the result of an asynchronous definition, one time, whatever the number of its service types. `IocTask<S>` is not disposable, and `Task<S>` is not the return value of a factory delegate. This differs from synchronous Singleton and Scoped definitions (see [Number of disposals](#number-of-disposals)).

### Asynchronous decorator chain

When a service type `S` has a decorator chain, the layers can be created only after the Task of the instance completes. Thus the owner of the chain is also an `OwnTask`:

| Registration | Service type | Key | Content |
| --- | --- | --- | --- |
| Owner of the chain | `IocHolder<Task<IocHolder<S>>>` | The private key | `IocHolder.OwnTask(asynchronous chain-building method(sp))` |
| `S` | `IocTask<S>` | The key of the closed definition | Takes `Value` (the outermost layer) from the Task of the owner of the chain |

- The asynchronous chain-building method waits for the Task of the instance, creates the layers, and returns `IocHolder.Chain<S>(outermost layer, outermost layer, middle layers...)`. `inner` **includes the outermost layer**. `S` itself has no registration, so MS.DI does not track the outermost layer, and the holder MUST dispose it.
- The result of `OwnTask` is an `IocHolder<S>`. To dispose it is to dispose the complete chain, outermost layer first. The hidden owner disposes the implementation instance, for all lifetimes.
- A generated asynchronous method (a creation method or a chain-building method) resolves all its dependencies from `sp` before the first `await`. There are two reasons. After an `await`, the scope can already be disposed. MS.DI also disposes in the reverse order of creation: a dependency that is resolved after an `await` is tracked later and disposed earlier than the object that uses it. This occurs when the Task of the instance is not complete at the time of resolution (tested, see the ADRs).
- The chain-building method gets the owner of the instance from `sp` **last**, after all dependencies of the decorators. The disposal order of MS.DI depends on the sequence of resolution. If the owner of the instance is resolved first, the dependencies of the decorators are disposed between the chain and the implementation instance (tested, see the ADRs).
- Thus the disposal order is: the owner of the chain (outermost layer, middle layers), then the owner of the instance (implementation instance), then the dependencies of the decorators.
- When the `Dispose` of a decorator layer throws, the other layers of the chain are still disposed (the rules of the [holder](#holder)). But MS.DI then stops, and the implementation instance, which comes later, is not disposed.

### Failure during creation

An asynchronous creation method or chain-building method has steps after `new` that can fail: asynchronous injection methods, and the constructors of the later decorator layers. In that case the Task ends as faulted, the owner has nothing to dispose, and the objects that were already created leak (tested, see the ADRs).

- For this reason, the generated asynchronous creation methods and chain-building methods have a try/catch. On failure or cancellation, the method disposes the objects that it already created, outer layers first and inner layers last. Then it throws the original exception again.
- The disposal follows the rules of `DisposeAsync`. An exception from the disposal itself does not replace the original exception. It goes to [`IocContainerGlobalOptions.OnDisposeException`](Container.Output.spec.md#dispose-exception-callback) (Container output, section "Dispose-exception callback").
- The chain-building method disposes only the layers that it created itself. The implementation instance belongs to the owner of the instance, which disposes it.
- With this correction, no object leaks in these cases (tested, see the ADRs): an asynchronous injection method of the instance fails, the constructor of a decorator layer throws, an asynchronous injection method of a decorator layer fails.

Synchronous creation methods and chain-building methods have the same problem, and the same rules apply:

- Creation method: when a step after `new` (the assignment of fields and properties, injection methods) throws, the method disposes the instance that it created. Then it throws the exception again.
- Chain-building method: when the creation of a decorator layer fails, the method disposes the layers that it already created, outer layers first. It disposes the implementation instance also, but only when the instance has no other owner, which is the Transient case. A Singleton or Scoped implementation instance is already in the holder of the instance, which disposes it.
- The generator generates the try/catch only when the instance is disposable and there are steps after `new` that can fail. The generator knows at compile time whether the instance is disposable.
- A synchronous method cannot wait. When the object implements `IDisposable`, the method calls `Dispose`. When the object implements only `IAsyncDisposable`, the method calls `DisposeAsync` but does not wait for it. The exception from that call goes to the dispose-exception callback, both when the call throws synchronously and when the returned Task fails later.
- An exception from the disposal itself does not replace the original exception. It goes to the dispose-exception callback. When there is more than one object to dispose, the method disposes them one by one. When the disposal of one object throws, the method continues to dispose the others, and each exception goes to the dispose-exception callback separately.
- No object leaks, and no object is disposed two times. The caller receives the same original exception object, and MS.DI does not wrap it (tested, see the ADRs).
- MS.DI does not cache a failure. Each retry calls the factory delegate again. MS.DI still tracks the dependencies that a failed attempt resolved, and it disposes them only when the scope or the provider is disposed. When a Singleton fails and is tried again repeatedly, these dependencies collect on the root. This is the original behavior of MS.DI, and this rule does not change it.

## Wrapper types and collections

Closed generics, wrapper types and dictionaries use the same analysis result as the container. The method registers each demand that the generator finds at compile time.

The method registers these only for the [demands](Analysis.Model.spec.md#sources-of-demands) in the assembly view (Analysis model, section "Sources of demands"). Unless stated differently, there is one registration for each "closed definition × service type". The registration points to **that** closed definition, through the holder or the creation method. Thus the rule of MS.DI that the last registration wins, and the collection of all registrations by MS.DI, work without more code.

| Demand | Registration | Lifetime |
| --- | --- | --- |
| `Lazy<S>` | One for each candidate | The same as the closed definition |
| `Func<S>` | One for each candidate. The delegate captures the `sp` of the factory delegate | Singleton when the closed definition is Singleton. If not, Transient |
| `Func<A..., S>` | One for each candidate | Transient |
| `Lazy<Task<S>>`, `Func<Task<S>>`, `Func<A..., Task<S>>` | The same as above. The content comes from `IocTask<S>` (a synchronous service is wrapped with `Task.FromResult`) | The same as above |
| `Task<S>` | **Not registered.** The dependency site gets it from `IocTask<S>`, or wraps a synchronous service with `Task.FromResult` | Not applicable |
| `KeyValuePair<TKey, X>` | One for each candidate, registered with a non-generic `ServiceDescriptor` | Transient |
| `IEnumerable<S>`, `IEnumerable<Lazy<S>>`, `IEnumerable<Func<S>>`, and other collections whose element is a reference type | **Not registered.** MS.DI collects them in registration order | Not applicable |
| `IEnumerable<Task<S>>` | The complete collection is registered as one service (because `Task<S>` has no registration) | Transient |
| A collection whose element is `KeyValuePair<TKey, X>` | The complete collection is registered as one service. With NativeAOT, MS.DI refuses to collect a collection whose element is a value type (tested, see the ADRs) | Transient |
| Other collection types (`IReadOnlyList<X>`, `X[]`, and so on) | The complete collection is registered as one service. The content comes from `IEnumerable<X>` | Transient |
| Dictionary | The complete dictionary is registered as one service | Transient |

- The table applies to the candidates of `S`. When `S` has no candidate, the method registers no wrapper for it. A dependency site that asks for a wrapper of such a service builds the wrapper itself (see [Dependency resolution](#dependency-resolution)).
- When the closed definition is not Singleton, `Func<S>` is registered as Transient and not as Scoped. A Singleton service can legally depend on a `Func<T>` of a Transient service (see [Dependency resolution](Semantics.spec.md#dependency-resolution)). If the registration were Scoped, MS.DI would reject it when `ValidateScopes` is on. "One cached `Func` for each scope" is an optimization of the container and not an observable behavior.
- With NativeAOT, MS.DI refuses to collect a collection whose element is a value type, such as `IEnumerable<KeyValuePair<TKey, T>>`. It throws `InvalidOperationException` at run time, with no warning at publish. A registration of the complete collection as one service works (tested, see the ADRs).
- A keyed wrapper type is registered with the key of the closed definition.
- For an inaccessible wrapper type, the method gets the `Type` from the type entry of the module and creates the object with the [wrapper entry](Module.Output.spec.md#type-entry-and-wrapper-entry) (Module output, section "Type entry and wrapper entry").
- Closed generics: the method registers only the derived registrations that the closing process makes. It does not register the open generic itself ([documented difference 10](../Constitution.md#8-documented-differences-from-msdi)).

## Dependency resolution

The generator generates each argument in a creation method from the shape of the resolution result. It does not select a target at compile time.

| Dependency | Generated expression |
| --- | --- |
| A required single service, a registered wrapper type | `sp.GetRequiredService<X>()` or `sp.GetRequiredKeyedService<X>(key)` |
| Optional, can be null | `sp.GetService<X>()` |
| Optional, has a default value | `sp.GetService<X>() ?? default value` |
| Optional, keeps the initializer | `if (sp.GetService<X>() is { } value) instance.Member = value;` |
| Optional, `X` is a value type (`KeyValuePair<TKey, T>`) | `sp.GetService(typeof(X)) is X value ? value : default value`. The generated code MUST NOT use the generic `GetService<X>()`: when there is no registration, it throws `NullReferenceException` for a value type (tested, see the ADRs). It also MUST NOT use `GetService<X?>()`, which is a different service type |
| `Func<S>`, `Lazy<S>`, `Func<Task<S>>`, `Lazy<Task<S>>`, and `S` has no candidate without tags in the assembly view | `sp.GetService<W>() ?? {wrapper}`, or the keyed version. `W` is the requested wrapper type. When a registration of `W` exists (a candidate with tags that the tags select, or a registration of the user), it is used. Otherwise the dependency site creates the wrapper, and the wrapper resolves `S` from `sp` with `GetRequiredService` when it is called. For `Task<S>`, the wrapper uses `Task.FromResult` |
| `IEnumerable<X>` | `sp.GetServices<X>()` or its keyed version |
| `Task<S>` | See [Asynchronous closed definitions](#asynchronous-closed-definitions) |
| Built-in service | `sp`, or a resolution from `sp` |
| `[ServiceKey]` | The key expression |
| `InheritKey` | Already replaced with the key of the closed definition that it belongs to |
| Inaccessible type | `sp.GetRequiredService(type entry())`, passed as `object` |

Thus MS.DI handles ambiguity and missing dependencies at run time, by its own settings ([documented difference 18](../Constitution.md#8-documented-differences-from-msdi)). The same applies to a captive dependency that is not on a definite edge. The definite check still reports the errors that it can decide at compile time, and for an ambiguity the generator gives a warning for a [definite ambiguity](#definite-ambiguity).

## Checks

- The generator generates the service collection extension only when the [definite check](Analysis.Model.spec.md#two-levels-of-checks) (Analysis model, section "Two levels of checks") has no Error.
- When an open generic must be closed with a type that the composition root cannot write, the generator reports `SGIOC405`: there is no code that it can generate.
- The generator does not check missing dependencies, disposable Transient services, or duplicate keys of a dictionary. For the last one, the method throws when it is called.
- The generator checks only the definite part of ambiguity. The level is Warning (see below).

### Definite ambiguity

The service collection extension is not a closed world. The tags are decided at run time, and the user can also register other services in the `IServiceCollection`. Thus an ambiguity cannot be an Error. But one situation is true for every set of tags.

For each dependency in the assembly view that asks for a single service (its shape is `Deferred`, including wrappers such as `Lazy<S>` and `Func<S>`), the generator finds the candidates by `(S, key)`. It counts only the closed definitions **without tags**:

| Candidates | Result |
| --- | --- |
| Two or more concrete registrations without tags | `SGIOC504` |
| No concrete registration at all (also none with tags), and two or more derived registrations without tags | `SGIOC504` |
| Other | No diagnostic |

- The message lists all candidates. It also names the candidate that wins when no tag is given, which is the last one in the [ordering](Analysis.Model.spec.md#ordering) (Analysis model, section "Ordering").
- The diagnostic is reported at the location of the dependency site (parameter or member). When the dependency site comes from an imported module, the diagnostic is reported on `IocServiceCollectionExtensionsAttribute`. There is one diagnostic for each dependency.
- The diagnostic is reported only in the assembly that generates the service collection extension. For the same situation, a container reports `SGIOC401` (Error). Each one reports independently, and they do not suppress each other.
- The Warning does not affect generation. The user can change its level.

## Requirements for generated code

The requirements are the same as for the [container](Container.Output.spec.md#requirements-for-the-generated-code) (Container output, section "Requirements for the generated code"): the marker at the start of the file, `[GeneratedCode]` on each type that the generator declares, names qualified with `global::`, no `var`, deterministic output, no reflection, and no warnings when published with NativeAOT.

## Example

```csharp
#region Define:
[assembly: IocServiceCollectionExtensions(Name = "App")]

public interface IRepo;
public interface IService : IDisposable;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
internal sealed class Repo : IRepo;

[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)],
    Decorators = [typeof(LoggingService)], Tags = ["feature"])]
internal sealed class Service(IRepo repo, Lazy<IRepo> lazyRepo) : IService
{
    public void Dispose() { }
}

internal sealed class LoggingService(IService inner) : IService
{
    public void Dispose() { }
}
#endregion

#region Generate:
// Illustration: global:: and the full form of some extension methods are omitted
public static class AppServiceCollectionExtensions
{
    private static readonly object __k_Repo = new();
    private static readonly object __k_Service = new();
    private static readonly object __k_Service_IService = new();

    public static IServiceCollection AddApp(this IServiceCollection services, params ReadOnlySpan<string> tags)
    {
        IocRegistrationMarker.Enter(services, "AppServiceCollectionExtensions.AddApp", "App");
        bool tag_feature = tags.Contains("feature");

        // Repo: no tags
        services.AddKeyedSingleton<IocHolder<Repo>>(__k_Repo, static (sp, _) => IocHolder.Own(__Create_Repo(sp)));
        services.AddSingleton<Repo>(static sp => __Get_Repo(sp));
        services.AddSingleton<IRepo>(static sp => (IRepo)__Get_Repo(sp));
        // Lazy<IRepo>: a demand from a dependency of Service
        services.AddSingleton<Lazy<IRepo>>(static sp => new Lazy<IRepo>(() => (IRepo)__Get_Repo(sp)));

        // Service: has tags
        if (tag_feature)
        {
            services.AddKeyedScoped<IocHolder<Service>>(__k_Service, static (sp, _) => IocHolder.Own(__Create_Service(sp)));
            services.AddScoped<Service>(static sp => __Get_Service(sp));
            // The decorator chain of IService has only one layer and no middle layer
            services.AddKeyedScoped<IocHolder<IService>>(__k_Service_IService,
                static (sp, _) => IocHolder.Chain<IService>(new LoggingService((IService)__Get_Service(sp))));
            services.AddScoped<IService>(static sp => sp.GetRequiredKeyedService<IocHolder<IService>>(__k_Service_IService).Value);
        }

        return services;
    }

    private static Repo __Create_Repo(IServiceProvider sp) => new Repo();
    private static Repo __Get_Repo(IServiceProvider sp) => sp.GetRequiredKeyedService<IocHolder<Repo>>(__k_Repo).Value;

    private static Service __Create_Service(IServiceProvider sp)
        => new Service(sp.GetRequiredService<IRepo>(), sp.GetRequiredService<Lazy<IRepo>>());
    private static Service __Get_Service(IServiceProvider sp) => sp.GetRequiredKeyedService<IocHolder<Service>>(__k_Service).Value;
}
#endregion
```

`IocHolder.Own` creates a holder that disposes its value. `IocHolder.Chain` creates a holder that disposes only the middle layers (this example has none). When the scope is disposed, MS.DI calls in sequence: `LoggingService.Dispose` (tracked by the registration of `IService`), the holder of the chain (nothing to do), `Service.Dispose` (by the holder of the instance).

## See also

- [Analysis model](Analysis.Model.spec.md)
- [Module output](Module.Output.spec.md)
- [Container output](Container.Output.spec.md)
- [Diagnostic list](Diagnostics.spec.md)
