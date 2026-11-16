# Generic Registration

## Overview

Full support for generic service registration including open generic types, closed generic discovery, and generic factory type mapping. The generator handles automatic discovery of closed generic types from open generic registrations.

## Feature 1: Closed Generic Registration from Open Generic

When an open generic registration exists, and a class has register and its constructor has closed generic for open generic registration, generate closed generic registration.

```csharp
#region Define:
public interface ILogger<T>
{
    public void Log(string msg);
}
[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
public sealed class Logger<T> : ILogger<T>
{
    public void Log(string msg)
    {
        Console.WriteLine(msg);
    }
}

public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
public interface IUnitOfWorkFactory
{
    List<T> Query<T>(Guid pk);
}

[IocRegisterDefaults(
    typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>)]
)]
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}

public sealed record TestRequest<T>(Guid PK) : IRequest<TestRequest<T>, List<T>>;

[IocRegister]
internal sealed class TestHandler<T>(
    ILogger<TestHandler<T>> logger,
    IUnitOfWorkFactory factory 
) : IRequestHandler<TestRequest<T>, List<T>>
{
    private readonly ILogger<TestHandler<T>> logger = logger;
    private readonly IUnitOfWorkFactory factory = factory;

    public List<T> Handle(TestRequest<T> request)
    {
        return factory.Query<T>(request.PK);
    }
}

internal sealed class HandlerDecorator1<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner,
    ILogger<HandlerDecorator1<TRequest, TResponse>> logger
) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    private readonly ILogger<HandlerDecorator1<TRequest, TResponse>> logger = logger;

    public TResponse Handle(TRequest request)
    {
        logger.Log(request.ToString() ?? string.Empty);
        Console.WriteLine("HandlerDecorator1: Before handling request");
        var response = inner.Handle(request);
        Console.WriteLine("HandlerDecorator1: After handling request");
        return response;
    }
}

internal sealed class HandlerDecorator2<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> inner
) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
{
    public TResponse Handle(TRequest request)
    {
        Console.WriteLine("HandlerDecorator2: Before handling request");
        var response = inner.Handle(request);
        Console.WriteLine("HandlerDecorator2: After handling request");
        return response;
    }
}

public class TestEntity;

[IocRegister]
internal sealed class ViewModel(IRequestHandler<TestRequest<TestEntity>, List<TestEntity>> handler)
{
    private readonly IRequestHandler<TestRequest<TestEntity>, List<TestEntity>> handler = handler;

    public List<TestEntity> Query(Guid pk)
    {
        return handler.Handle(new TestRequest<TestEntity>(pk));
    }
} 
#endregion

#region Generate:
// ...open generic registrations...

// Closed generic registration discovered from ViewModel's constructor; decorated like other registrations (see Register.Decorators)
services.AddSingleton<TestHandler<TestEntity>, TestHandler<TestEntity>>();
services.AddKeyedSingleton<IRequestHandler<TestRequest<TestEntity>, List<TestEntity>>>("SourceGen.Ioc.Decorated:IRequestHandler<TestRequest<TestEntity>, System.Collections.Generic.List<TestEntity>>|TestHandler<TestEntity>", (IServiceProvider sp, object? key) =>
{
    var s0 = sp.GetRequiredService<TestHandler<TestEntity>>();
    var s1 = new HandlerDecorator2<TestRequest<TestEntity>, List<TestEntity>>(s0);
    var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>>>();
    var s2 = new HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>(s1, s2_p0);
    return s2;
});
services.AddSingleton<IRequestHandler<TestRequest<TestEntity>, List<TestEntity>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<TestRequest<TestEntity>, List<TestEntity>>>("SourceGen.Ioc.Decorated:IRequestHandler<TestRequest<TestEntity>, System.Collections.Generic.List<TestEntity>>|TestHandler<TestEntity>"));

// TestHandler<TestEntity>'s ILogger<TestHandler<TestEntity>> dependency is closed in turn from the open generic Logger<T>
services.AddSingleton<Logger<TestHandler<TestEntity>>, Logger<TestHandler<TestEntity>>>();
services.AddSingleton<ILogger<TestHandler<TestEntity>>>((IServiceProvider sp) => sp.GetRequiredService<Logger<TestHandler<TestEntity>>>());

// The decorator's ILogger<HandlerDecorator1<...>> dependency is also closed from the open generic Logger<T>
services.AddSingleton<Logger<HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>>, Logger<HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>>>();
services.AddSingleton<ILogger<HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>>>((IServiceProvider sp) => sp.GetRequiredService<Logger<HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>>>());
#endregion
```

The constructor parameters and injection members (properties, fields, and method parameters with `[IocInject]` / `[Inject]`) of a closed generic registration MUST have the type parameters of the open generic implementation substituted by its type arguments, and MUST be collected as closed generic dependencies in turn. For example, closing `Outer<T>(IRepo<T> repo)` as `Outer<string>` collects `IRepo<string>`, and `ILogger<Outer<T>>` becomes `ILogger<Outer<string>>`; an `[IocInject] IValidator<T>` property of `Outer<T>` is injected as `IValidator<string>`. The initializer of an injected property or field, copied as its fallback value, MUST have its type parameter references substituted too (`new global::Ns.Box<T>()` becomes `new global::Ns.Box<string>()`), without changing member names such as `x.T`; `nameof(T)` becomes the literal `"T"`. Wrapped dependencies MUST be unwrapped like direct dependencies (`Lazy<T>`, `Func<T>`, collections, dictionaries, and `KeyValuePair<TKey, T>`), so `Lazy<IRepo<T>>` collects `IRepo<string>`.

Dependencies that differ only in tuple element names, such as `IHandler<(Entity A, string B)>` and `IHandler<(Entity, string)>`, are one runtime type. Each open generic registration MUST be closed at most once for each runtime type and set of decorators, written with the first spelling (in the order dependencies are collected) it is closed for; the other spellings are resolved by that registration. Decorator constraints compare tuple element names like the implementation's (see [Decorators](Register.Decorators.spec.md)), so a spelling that closes the registration with other decorators MUST close it again, as for another runtime type: with `Deco<T, U> where T : U` decorating `Handler<T, U>`, dependencies on `IHandler<(int A, int B), (int X, int Y)>` and `IHandler<(int X, int Y), (int X, int Y)>` register `IHandler<...>` undecorated for the first and decorated for the second, in the order the dependencies are collected (MS.DI resolves the last one). Each spelling MUST still select an open generic registration like a dependency of its own, the first one it matches, since generic constraints compare tuple element names (see [Type Argument Mapping](#type-argument-mapping)); the selected registration is not closed again when it is already closed for that runtime type with the same decorators, and the spelling MUST NOT fall through to the next registration then. So with `HandlerA<T, U> : IHandler<T, U> where T : U` registered before `HandlerB<T, U> : IHandler<T, U>`, dependencies on `IHandler<(int A, int B), (int X, int Y)>` and then `IHandler<(int X, int Y), (int X, int Y)>` close `HandlerB` for the first spelling, which does not satisfy the constraint of `HandlerA`, and `HandlerA` for the second, so both are registered as for two runtime types (MS.DI resolves the last one); a third dependency on `IHandler<(int, int), (int, int)>` selects `HandlerA`, which is already closed, so nothing more is registered. A type already registered as written in another spelling MUST NOT be closed again.

A closed generic dependency has the key it is resolved with: none for a non-keyed dependency, otherwise the key of `[FromKeyedServices(key)]` / `[IocInject(key)]` on the parameter, of its injected method, or of an injected property or field; wrappers (`Lazy<T>`, collections, ...) give their inner dependency the same key, except dictionaries (`IDictionary<TKey, T>`, ...) and collections of `KeyValuePair<TKey, T>`, which resolve every keyed registration of `T` (below); a single `KeyValuePair<TKey, T>` resolves `T` with the dependency's key (`new KeyValuePair<TKey, T>(key, ...)`), like other wrappers. Whether a key is given as C# code (`KeyType = KeyType.Csharp`) or as a value MUST be recorded with the key of registrations and dependencies (a value key is stored as its constant written as C# code too). A dependency MUST select the first open generic registration it matches whose closed key (the registration's `Key`, with the type parameters it uses substituted) is the dependency's key, comparing keys as written, so a non-keyed dependency only selects a non-keyed registration. A key given as C# code cannot be compared with another key at compile time, so a keyed dependency that no registration matches with its key MUST select the first keyed registration it matches whose key or the dependency's key is given as C# code (`[FromKeyedServices("node")] INode<int>` closes `Node<T>` registered with `Key = nameof(Name), KeyType = KeyType.Csharp`, and `[IocInject(nameof(Keys.Y), KeyType = KeyType.Csharp)]` closes a registration with `Key = "y"`). Two different value keys MUST NOT match: `[FromKeyedServices("x")] IHandler<Entity>` does not close `Handler<T>` registered with `Key = "y"`, which cannot resolve it, but selects a registration with a C# code key declared after it. A keyed dependency MUST NOT select a non-keyed registration. `GetKeyedService` / `GetRequiredKeyedService` / `GetKeyedServices` invocations, whose key is not known at compile time, MUST select the first keyed registration they match, with its key, and only when there is none the first registration they match (the key may be `null` at runtime): `sp.GetKeyedService<IHandler<Entity>>("k")` closes `KeyedHandler<T>` registered with `Key = "k"` even when a non-keyed `Handler<T>` is registered before it. `[IocDiscover]`, which has no key, MUST select the first registration it matches, with its key. The value type of a dictionary dependency, of a collection of key value pairs, or of a key value pair (or array of them) requested from an `IServiceProvider` MUST close every keyed registration it matches, each with its key, and MUST NOT close a non-keyed registration, which the dictionary does not resolve: `IDictionary<string, IHandler<Entity>>` with `Handler<T>`, `AHandler<T>` (`Key = "a"`) and `BHandler<T>` (`Key = "b"`) closes `AHandler<Entity>` and `BHandler<Entity>`. The dependencies of each closing are collected in turn. `IServiceProvider` invocations MUST unwrap the requested type recursively, like constructor parameters, so `sp.GetService<IEnumerable<KeyValuePair<string, IHandler<Entity>>>>()` closes the keyed registrations of `IHandler<Entity>` and `sp.GetService<Lazy<IHandler<Entity>>[]>()` closes `IHandler<Entity>`. An existing registration only blocks closing for the same key: a dependency MUST NOT be closed when its type, or the closed service type the selected registration would register, is already registered (as written in another spelling, or by an earlier closing) with the same key, while a registration with another key MUST NOT keep it from being closed. With `Handler<T> : IHandler<T>` registered for `IHandler<>`, `[IocRegister(Key = "k", ServiceTypes = [typeof(IHandler<Entity>)])] KeyedHandler`, and a consumer of a non-keyed `IHandler<Entity>`, `Handler<Entity>` is closed and registered for `IHandler<Entity>` without a key; likewise `[IocRegisterFor(typeof(Handler<Entity>), Key = "k")]` does not keep a non-keyed `Handler<Entity>` dependency from closing `Handler<T>`, and a non-keyed `EntityHandler : IHandler<Entity>` does not keep `[IocInject("k")] IHandler<Entity>` from closing `Handler<T>` registered with `Key = "k"`. The analyzer applies the same rule (`SGIOC018`).

A closed generic registration MUST collect only the dependencies of how it creates its instance:

| Closed registration | Instance | Collected dependencies |
| --- | --- | --- |
| Constructor (default) | `new Node<string>(...) { ... }` | constructor parameters and injection members |
| `Factory` | `Factory.Create(...)`, or `Factory.Create<string>(...)` for a generic factory; `Node<string>.Create(...)` for a factory whose path uses the implementation's type parameters | factory method parameters, except `IServiceProvider` and `[ServiceKey]` parameters (none for a `Factory` given as a string, whose parameters are unknown), with the implementation's type parameters substituted (`IRepo<string>` for `Node<T>.Create(IRepo<T> repo)`) |
| `Instance` | `AddSingleton(instance)` | none |

The closed implementation type of a registration with a generic factory is constructed when the mapping does not match it (see [Generic Factory Type Mapping](#feature-3-generic-factory-type-mapping)), so its constructor parameters and injection members are collected; otherwise its factory parameters are collected like a service type's. The factory method parameters are collected from its closed service types the mapping matches. Decorators are applied to `Factory` registrations but not to `Instance` registrations, so the dependencies of an `Instance` registration's decorators MUST NOT be collected. The closed generic dependencies of a non-generic registration, which start the closings, MUST be collected by the same table, with the parameters of a generic factory substituted for each of its service types (see [Factory](Register.Factory.spec.md)), and the dependencies of the decorators applied to it (none for an `Instance`).

An expanding dependency MUST NOT close an implementation, because its closings never end: closing `Node<T>(Lazy<INode<List<T>>> next)` as `Node<string>` does not close `Node<List<string>>`, and `INode<List<string>>` is resolved like an unregistered service. The analyzer reports expanding dependencies with `SGIOC036`, or `SGIOC037` when each of their expanding cycles has a `Lazy<T>` or `Func<...>` dependency (see [Generic Dependency Diagnostics](../Analyzer/Spec/Diagnostics.Generics.spec.md)). An expanding dependency behind `Lazy<T>` or `Func<T>` has a finite object graph, but it is not resolved by generated code either: Register output would have to register the open generic wrappers (`Lazy<>`, which affects every `Lazy<T>` of the service collection, while a delegate type such as `Func<>` cannot be registered as an open generic), and a container would need a generic resolver that calls itself with nested type arguments (`GetNode<T>` resolving `GetNode<List<T>>`), which Native AOT cannot compile because the generic instantiations never end. Expanding dependencies are found on the open generic registrations, so the result does not depend on the order in which dependencies are discovered:

- The dependencies of an open generic registration are those its closed registrations collect (see the table above): the constructor parameters and injection members of a constructed implementation, the parameters of its `Factory` method, and none for an `Instance`. The dependencies of a decorator are its constructor parameters, except the decorated parameter, and its injection members; the decorators of an `Instance` registration are not analyzed.
- The parameters of a `Factory` method are dependencies of the factory for that registration, written with the factory's own type parameters, which are nodes like the type parameters of implementations. The mapping's service type template, with its placeholders replaced by the factory type parameters they stand for, is matched position by position against each service type of the same generic type definition as the implementation declares it (or, for a registration without an implementation, its open service type), like the dependencies below, into factory links that belong to no dependency: each type parameter of the implementation is linked to each factory type parameter whose type argument contains it, nested when the type argument is not the type parameter itself, and a factory type parameter matched below a type parameter of the implementation takes parts of its type argument, which links it without nesting. For `Create<TF>(INode<List<TF>> next)` mapped by `[IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]` for `IRequestHandler<TResponse>`, `TResponse` takes the type argument `Task<TF>`, so `TResponse` is linked to `TF` without nesting, and `TF` is linked nested to the type parameters of the implementations registered for `INode<>`. For `Create<TF>(IOther<TF> other)` mapped by `[IocGenericFactory(typeof(IRequestHandler<int>), typeof(int))]` for `Handler<T> : IRequestHandler<Task<T>>`, `TF` takes the type argument `Task<T>`, so `T` is linked nested to `TF`. A closed dependency collected from a generic factory is compared with the factory's dependencies substituted with the factory's type arguments.
- Each type parameter of an open generic implementation or decorator is linked to the type parameters of every implementation registered for each of its dependencies whose type arguments, matched position by position against the service type as the implementation declares it, contain it. Positions that do not match are skipped. The link is nested when the type argument is not the type parameter itself, such as `T` to `T` through `INode<List<T>>`. A type parameter matched against a deeper part, such as `T` of `IB<T>` against `List<U>` of `B<U> : IB<List<U>>`, takes parts of its type argument, which does not nest it. A dependency that is a type parameter, such as `T dependency` or `Lazy<T>`, can only become a generic type written as a type argument (of a dependency that an open generic registration is registered for, of a closed generic dependency of a non-generic registration or invocation, or of a decorated service type), so it is linked to every type parameter of the implementations registered for those types.
- Each decorator of an open generic registration is linked in the same way from each service type it decorates, written with the implementation's type parameters, without checking its constraints.
- A dependency is expanding when it has a nested link on a cycle of links (the expanding cycles of ECMA-335 II.9.2), or any link on a cycle with a nested decorator or factory link, since decorators and factories are applied without a dependency. Moving a type argument without nesting it, such as `Pair<T, U>(IPair<U, U> next)`, is not expanding. A cycle that nests a type argument and takes it apart again, such as `A<T>(IB<List<T>>)`, `B<V>(IC<V>)`, and `C<W>(IA<W>) : IC<List<W>>`, ends, but is conservatively treated as expanding.
- A closed dependency MUST NOT be closed, for any implementation, when every dependency of the closed generic registration or decorator it was collected from that becomes it is expanding. It MUST still be closed when another registration collects it through a dependency that does not expand, or a non-generic registration collects it. With `Node<T>(IEnumerable<IBridge<T>> bridges)`, `Bridge<T>(INode<List<T>> node)`, and `Other<T>(IBridge<T> bridge)`, `Bridge<string>` is closed for both `Node<string>` and `Other<string>`, and `Node<List<string>>` is not, whichever is discovered first.
- As a safeguard, a dependency collected from a closed generic registration or decorator whose type arguments (including array element types and tuple elements) are nested more than 16 levels deep MUST NOT be closed.

Dependencies and decorator dependencies of an open generic registration, such as `ILogger<HandlerDecorator1<TRequest, TResponse>>`, still contain its type parameters and MUST NOT be collected as closed generic dependencies, including when a type parameter appears only inside an array, such as `INode<T[]>` or `INode<List<T[]>>`. Only their closed forms, from closed generic registrations (`INode<string[]>`), are collected.

### Type Argument Mapping

The implementation's type arguments are found by matching the service type as the implementation declares it (one of its interfaces, one of its base classes, or itself) against the closed service type, at every nesting level. Type parameter names and order MAY differ from the service type's definition. A closed generic registration MUST NOT be generated when the closed service type does not have the declared shape, when one type parameter would stand for two different types, or when a type parameter of the implementation is not determined, such as `V` of `Node<T, V> : Base<T>` for `Base<int>`. This applies however the closed registration creates its instance (the constructor, a `Factory`, an `Instance`, or a `Key` that uses the type parameters, see [Factory](Register.Factory.spec.md#instance-and-factory-precedence)), and to an open generic type registered without interfaces, which is closed like a factory-only registration: the closed type and its `Factory`, `Instance` and `Key` would keep the undetermined type parameter. Such a type MUST also check the constraints of its type parameters like a closed generic registration, since its closed `Factory` and `Key` name the closed type. The analyzer does not count such a closing as a registration either (`SGIOC018`).

| Implementation | Closed service type | Closed implementation |
| :--- | :--- | :--- |
| `Handler<TB, TA> : IHandler<TA, TB>` | `IHandler<Req, Res>` | `Handler<Res, Req>` |
| `TextHandler<TRequest> : IHandler<TRequest, string>` | `IHandler<Req, string>` | `TextHandler<Req>` |
| `TextHandler<TRequest> : IHandler<TRequest, string>` | `IHandler<Req, int>` | none |
| `ListHandler<TItem, TKey> : IHandler<List<TItem>, Dictionary<TKey, TItem>>` | `IHandler<List<Req>, Dictionary<string, Req>>` | `ListHandler<Req, string>` |
| `ListHandler<T> : IHandler<T, List<T>>` | `IHandler<Req, List<Res>>` | none |
| `ArrayHandler<T> : IHandler<T[], T>` | `IHandler<Req[], Req>` | `ArrayHandler<Req>` |
| `GridHandler<T> : IHandler<T[,], T>` | `IHandler<Req[], Req>` | none (array rank differs) |

Array type arguments are matched by element type and rank, including jagged arrays such as `T[][]`.

A closed generic registration MUST NOT be generated when a type argument does not satisfy the generic constraints of the implementation's type parameter (`class`, `struct`, `unmanaged`, `new()`, and type constraints); the next open generic registration of the service type is tried instead, and when none matches, the dependency is resolved like an unregistered service. Constraints are checked like decorator constraints (see [Decorators](Register.Decorators.spec.md)), including for the dependencies of closed generic registrations:

- Every type satisfies a constraint to `object`, such as `where T : U` with `U = object`; base class constraints also apply to structs, enums, and delegates, such as `where T : Enum`.
- Tuple element names are part of a type here, although C# converts between them by identity: `(int A, int B)` does not satisfy `where T : U` with `U = (int X, int Y)`, so the registration is not closed for that spelling and SGIOC018 is reported. When another spelling of the same runtime type closes it, that registration resolves the dependency too (see above), and the analyzer treats the dependency as resolved like the generator, without reporting SGIOC018.
- A type with required members, declared or inherited, only satisfies `new()` when its parameterless constructor has `[SetsRequiredMembers]` (CS9040).
- The type arguments of a closed decorator keep their hierarchy, so the dependencies of the decorator, such as `IRepo<T>` in `Decorator<T>(IService<T> inner, IRepo<T> repo)`, are closed with the same constraint checks as other dependencies.
- Covariant conversions are followed one level: a type argument that is a variant interface or delegate, or implements a variant interface, satisfies the constraint when each covariant (`out`) type argument that is a reference type is the constraint's type argument, `object`, one of its interfaces, or one of its base classes, and every other type argument is the same (`List<string>` and `IEnumerable<IComparable>` satisfy `IEnumerable<object>`, `IGrouping<Many, Many>` satisfies `IGrouping<object, object>`). Contravariant and nested variant conversions are not followed, nor are those of a variant type nested in a generic type. Arrays satisfy constraints to the interfaces they implement (`int[]` satisfies `IEnumerable<int>`, `string[]` satisfies `IEnumerable<object>`).
- The covariant conversions of a type argument of a dependency of a closed generic registration follow its substituted type arguments: `List<T>` in `Outer<T>(IRepo<List<T>>)` satisfies `IEnumerable<object>` for `Outer<string>`, but not for `Outer<int>`.
- A generic value type that is a type argument of a dependency of a closed generic registration, such as `KeyValuePair<T, int>` in `Outer<T>(IRepo<KeyValuePair<T, int>>)`, does not satisfy `unmanaged`, because whether it is unmanaged depends on its fields.

| Implementation | Closed service type | Closed implementation |
| :--- | :--- | :--- |
| `Repo<T> : IRepo<T> where T : struct` | `IRepo<int>` | `Repo<int>` |
| `Repo<T> : IRepo<T> where T : struct` | `IRepo<string>` | none |
| `Repo<T> : IRepo<T> where T : IEntity` | `IRepo<Entity>` (`Entity : IEntity`) | `Repo<Entity>` |

### Open Registrations MS.DI Cannot Construct

The open registration of the Register output (`services.AddSingleton(typeof(IRepo<>), typeof(Repo<>))`) is constructed by MS.DI. It MUST NOT be emitted when MS.DI cannot create its instances: for an `Instance` (see [Factory](Register.Factory.spec.md#instance-and-factory-precedence)), or when the implementation type cannot be constructed, such as the open interface target of `[IocRegisterFor(typeof(IRequestHandler<>), Factory = nameof(Create))]`, or its selected constructor is not public (MS.DI calls public constructors only), such as `Repo<T>` with only an `internal` constructor, whose closed registrations construct it (`sp => new Repo<Entity>()`). Their closed registrations are still generated for the closed types discovered at compile time, like those of a closed registration: a closed implementation type that cannot be constructed is not registered unless the generic factory's mapping matches it (such as `IRequestHandler<Entity>` for the template `IRequestHandler<Task<int>>`), while its service types the mapping matches are. It MUST NOT be emitted either for a `Key` that uses the implementation's type parameters, such as `Node<T>.Name`: an open registration has a single key, while such a key can differ per closed type and does not compile without the type parameters (`SGIOC040`, see [Factory](Register.Factory.spec.md#instance-and-factory-precedence)); its closed registrations read the key from the closed type.

### Types Nested in a Generic Type

Open generic registrations are closed with the type arguments of the registered type itself, so the type arguments of a containing type cannot be closed:

- An open type declared in a generic type, such as `Outer<O>.Node<T>`, `Outer<O>.Plain` or `typeof(Outer<>.Node<>)`, MUST NOT be registered, neither as an open generic registration nor closed for the dependencies discovered later (such as `Outer<int>.INode<string>`). The analyzer reports `SGIOC038`.
- Such a type MUST also be ignored in the other type arguments of registration and default settings attributes: `ServiceTypes` (also of a closed implementation, so `typeof(Outer<>.INode<>)` does not register `Outer<int>.INode<string>`), `Decorators`, `ImplementationTypes`, and the interfaces and base classes an open implementation is registered as with `RegisterAllInterfaces` / `RegisterAllBaseClasses` (such as `Outer<T>.INode<T>` of `Node<T>`, which is neither registered for the open generic registration nor for the closed `Node<int>`), and the forms an open decorator declares (such as `Outer<T>.INode<T>` of `NodeDecorator<T>`, so the decorator is not applied to `Outer<int>.INode<int>`). `[IocRegisterDefaults]` whose target is such a type MUST be ignored as a whole, including its `ImplementationTypes`. Use their closed types instead, such as `ServiceTypes = [typeof(Outer<int>.INode<string>)]`.
- A closed type declared in a generic type, such as `[IocRegisterFor(typeof(Outer<int>.Node<string>), ServiceTypes = [typeof(Outer<int>.INode<string>)])]`, MUST be registered like any closed type.
- A type declared in a generic type MUST NOT be matched as a form of its containing type: a dependency `Outer<int>.INode<string>` does not close an open generic registration of `Outer<T>`.

## Feature 2: Explicit Closed Generic Discovery

When `IocDiscoverAttribute` (or `IocDiscoverAttribute<T>`) is exists, collect `IocDiscoverAttribute.ClosedGenericType` (or `T`) for generate factory code for open generic registrations.

```csharp
#region Define:
public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRequestHandler<,>)])]
public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>;

public class ViewModel
{
    // Non-generic version with type parameter
    [IocDiscover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
    // Or generic version
    // [IocDiscover<IRequestHandler<TestRequest<string>, List<string>>>]
    public void DoAction()
    {
        // e.g. mediator.Send(new TestRequest<string>()) resolves IRequestHandler<TestRequest<string>, List<string>> at runtime
    }
}
#endregion

#region Generate:
services.AddSingleton(typeof(TestRequestHandler<>), typeof(TestRequestHandler<>));
// The open IRequestHandler<,> registration is skipped (TestRequest<T> is not a type parameter); the discovered closed type is registered
services.AddSingleton<TestRequestHandler<string>, TestRequestHandler<string>>();
services.AddSingleton<IRequestHandler<TestRequest<string>, List<string>>>((IServiceProvider sp) => sp.GetRequiredService<TestRequestHandler<string>>());
#endregion
```

## Feature 3: Generic Factory Type Mapping

When registrations is specified `IocRegisterAttribute.Factory` or `IocRegisterDefaultsAttribute.Factory` and factory method is generic, the method must be marked with `[IocGenericFactory]` attribute to provide type parameter mapping information.

### Purpose

When a generic factory method is used with open generic service registration, the generator needs to know how to map the actual type arguments from the discovered closed generic type to the factory method's type parameters.

### Attribute Signature

```csharp
[IocGenericFactory(params Type[] genericTypeMap)]
```

### Parameters

- First type: Service type template with placeholder types (e.g., `IRequestHandler<Task<int>>`)
- Following types: Placeholder types that correspond to factory method's type parameters in order

### Rules

- The factory method must have the same number of type parameters as placeholder types provided, each placeholder type MUST be a type without type arguments that is not an array (not `List<int>`, `int?`, `(int, string)` or `int[]`), and each placeholder type MUST appear at least once in the service type template; otherwise SGIOC016 is reported and the registration is not generated. A placeholder MAY appear more than once, such as `int` in `IPair<int, int>` for `Create<T>`: a service type matches only when each position takes the same type argument (`IPair<Entity, Entity>` calls `Create<Entity>`), and a service type that takes different ones, such as `IPair<Entity, Dto>`, does not match (`SGIOC039`)
- Each placeholder type should be unique - duplicate placeholders cannot be distinguished and will not generate registration
- The service type template must match the structure of the open generic service type in the `IocRegisterDefaults`
- If `[IocGenericFactory]` is missing on a generic factory method, SGIOC016 diagnostic is reported
- The factory is closed per service type, so its result is the service type's own instance: it MUST be cast to the service type, and the implementation type MUST keep resolving its own (constructed) registration, unless the mapping matches the implementation type itself, such as the target of `[IocRegisterFor<IRequestHandler<Task<Entity>>>(Factory = ...)]` or of a registration without an implementation type, which then calls the factory like a service type
- The factory's type arguments are found by matching the service type template, with its placeholders replaced by the factory's type parameters, against the closed service type: generic types are matched by their generic type definition position by position, and other types of the template are not compared. Array element types are type arguments too: arrays MUST have the same rank (`IFoo<int[]>` matches `IFoo<Entity[]>` and `IFoo<Entity[][]>`, but not `IFoo<Entity[,]>`). Tuples are matched as `System.ValueTuple` of the same arity element by element, without their element names (`IFoo<(int, string)>` matches `IFoo<(Entity X, string Y)>`), including tuples of more than seven elements. A placeholder that only appears in the type arguments of a containing type, such as `int` in `IFoo<Outer<int>.Inner>`, does not appear in the template (`SGIOC016`): type data only has the type arguments of a type itself (see issue #187). A service type that does not determine every factory type parameter, such as `IRequestHandler<Entity>` or `IDisposable` for the template `IRequestHandler<Task<int>>`, or that gives a repeated placeholder different type arguments, such as `IPair<Entity, Dto>` for the template `IPair<int, int>`, MUST NOT be registered in either output, since the factory cannot be called for it; for a registration without an implementation type, the discovered closed type is not closed by it. The analyzer reports such service types of registrations with `SGIOC039`
- The factory method parameters MUST have the factory's type parameters substituted by the type arguments the mapping gives for the closed service type, and MUST be collected as closed generic dependencies, like constructor parameters: `Create<T>(IRepo<T> repo)` mapped by `[IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]` is called for `IRequestHandler<Task<Entity>>` with a resolved `IRepo<Entity>`, which closes an open generic `IRepo<>` registration
- A non-keyed service type's factory instance MUST be registered under the hidden key `"SourceGen.Ioc.Decorated:{ServiceType}|{ImplementationType}"` with the service type forwarding to it, like a decorator chain (see [Decorators](Register.Decorators.spec.md)), so `Lazy<T>` / `Func<T>` wrappers resolve the same instance; `Decorators` wrap the factory result inside that registration

### Single Type Parameter Example

```csharp
#region Define:
public interface IRequestHandler<TResponse> { }

// Service type is IRequestHandler<>, has 1 type parameter
[IocRegisterDefaults(typeof(IRequestHandler<>),
    ServiceLifetime.Singleton,
    Factory = nameof(FactoryContainer.Create))]
public static class FactoryContainer
{
    // Template: IRequestHandler<Task<int>> where int is placeholder for T
    //                                              ┌──────────────┐
    //                                              │              │int = T (index 0)
    //                                              ↓              ↓
    [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
    public static IRequestHandler<Task<T>> Create<T>() => new Handler<T>();
}

[IocRegister]
public class Handler<T> : IRequestHandler<Task<T>> { }

public class Entity { }

// Discover IRequestHandler<Task<Entity>>
[IocDiscover<IRequestHandler<Task<Entity>>>]
public sealed class App { }
#endregion

#region Generate:
// Type mapping: Task<Entity> matches Task<int> template
// → int placeholder maps to Entity
// → T = Entity
services.AddSingleton(typeof(Handler<>), typeof(Handler<>));
services.AddSingleton<Handler<Entity>, Handler<Entity>>();
services.AddKeyedSingleton<IRequestHandler<Task<Entity>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>>|Handler<Entity>", (IServiceProvider sp, object? key) =>
{
    var s0 = (IRequestHandler<Task<Entity>>)FactoryContainer.Create<Entity>();
    return s0;
});
services.AddSingleton<IRequestHandler<Task<Entity>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<Task<Entity>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>>|Handler<Entity>"));
#endregion
```

### Two Type Parameters Example

```csharp
#region Define:
public interface IRequestHandler<TRequest, TResponse> { }

// Service type is IRequestHandler<,>, has 2 type parameters
[IocRegisterDefaults(typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Factory = nameof(FactoryContainer.Create))]
public static class FactoryContainer
{
    // Template: IRequestHandler<Task<int>, List<decimal>>
    // int = T1 (index 0), decimal = T2 (index 1)
    //                                              ┌─────────────────────────────┐
    //                                              │            ┌────────────────┼──────────────┐
    //                                              ↓            ↓                ↓              ↓
    [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>>), typeof(int), typeof(decimal))]
    public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>()
        => new Handler<T1, T2>();
}

[IocRegister(ServiceTypes = [typeof(IRequestHandler<,>)])]
public class Handler<T1, T2> : IRequestHandler<Task<T1>, List<T2>> { }

public class Entity { }
public class Dto { }

// Discover IRequestHandler<Task<Entity>, List<Dto>>
[IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
public sealed class App { }
#endregion

#region Generate:
// Type mapping:
// - Task<Entity> matches Task<int> → int = Entity → T1 = Entity
// - List<Dto> matches List<decimal> → decimal = Dto → T2 = Dto
services.AddSingleton(typeof(Handler<,>), typeof(Handler<,>));
services.AddSingleton<Handler<Entity, Dto>, Handler<Entity, Dto>>();
services.AddKeyedSingleton<IRequestHandler<Task<Entity>, List<Dto>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>, List<Dto>>|Handler<Entity, Dto>", (IServiceProvider sp, object? key) =>
{
    var s0 = (IRequestHandler<Task<Entity>, List<Dto>>)FactoryContainer.Create<Entity, Dto>();
    return s0;
});
services.AddSingleton<IRequestHandler<Task<Entity>, List<Dto>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<Task<Entity>, List<Dto>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>, List<Dto>>|Handler<Entity, Dto>"));
#endregion
```

### Reversed Mapping Example

Type parameter order can differ from service type order:

```csharp
#region Define:
public interface IRequestHandler<TRequest, TResponse> { }

[IocRegisterDefaults(typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Factory = nameof(FactoryContainer.Create))]
public static class FactoryContainer
{
    // REVERSED: First placeholder (int) maps to T2, second placeholder (decimal) maps to T1
    // decimal = T1, int = T2                       ┌────────────────────────────────────────────┐
    //                                              │            ┌─────────────────┐             │
    //                                              ↓            ↓                 ↓             ↓
    [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>>), typeof(decimal), typeof(int))]
    public static IRequestHandler<Task<T2>, List<T1>> Create<T1, T2>()
        => new Handler<T1, T2>();
}

public class Handler<T1, T2> : IRequestHandler<Task<T2>, List<T1>> { }

public class Entity { }
public class Dto { }

// Discover IRequestHandler<Task<Entity>, List<Dto>>
[IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
public sealed class App { }
#endregion

#region Generate:
// Type mapping:
// - Task<Entity> matches Task<int> → int = Entity → T2 = Entity
// - List<Dto> matches List<decimal> → decimal = Dto → T1 = Dto
// Note: Create<T1, T2> so call is Create<Dto, Entity>
services.AddKeyedSingleton<IRequestHandler<Task<Entity>, List<Dto>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>, List<Dto>>|Handler<Dto, Entity>", (IServiceProvider sp, object? key) =>
{
    var s0 = (IRequestHandler<Task<Entity>, List<Dto>>)FactoryContainer.Create<Dto, Entity>();
    return s0;
});
services.AddSingleton<IRequestHandler<Task<Entity>, List<Dto>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<Task<Entity>, List<Dto>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>, List<Dto>>|Handler<Dto, Entity>"));
#endregion
```

### With IServiceProvider Parameter

```csharp
#region Define:
public interface IRequestHandler<TResponse> { }

[IocRegisterDefaults(typeof(IRequestHandler<>),
    ServiceLifetime.Singleton,
    Factory = nameof(FactoryContainer.Create))]
public static class FactoryContainer
{
    [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
    public static IRequestHandler<Task<T>> Create<T>(IServiceProvider sp)
        => new Handler<T>(sp);
}

public class Handler<T>(IServiceProvider sp) : IRequestHandler<Task<T>> { }

public class Entity { }

[IocDiscover<IRequestHandler<Task<Entity>>>]
public sealed class App { }
#endregion

#region Generate:
// Factory method receives IServiceProvider
services.AddKeyedSingleton<IRequestHandler<Task<Entity>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>>|Handler<Entity>", (IServiceProvider sp, object? key) =>
{
    var s0 = (IRequestHandler<Task<Entity>>)FactoryContainer.Create<Entity>(sp);
    return s0;
});
services.AddSingleton<IRequestHandler<Task<Entity>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<Task<Entity>>>("SourceGen.Ioc.Decorated:IRequestHandler<Task<Entity>>|Handler<Entity>"));
#endregion
```

### Invalid: Duplicate Placeholder Types

Registration is NOT generated when placeholder types are not unique:

```csharp
#region Define:
public interface IRequestHandler<TRequest, TResponse> { }

[IocRegisterDefaults(typeof(IRequestHandler<,>),
    ServiceLifetime.Singleton,
    Factory = nameof(FactoryContainer.Create))]
public static class FactoryContainer
{
    // INVALID: Both placeholders use int - cannot distinguish which maps to T1 vs T2
    [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<int>>), typeof(int), typeof(int))]
    public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>()
        => throw new NotImplementedException();
}

[IocRegister]
public class Handler<T1, T2> : IRequestHandler<Task<T1>, List<T2>> { }

public class Entity { }
public class Dto { }

[IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
public sealed class App { }
#endregion

#region Generate:
// NO factory registration is generated because placeholders are not unique
// Only the implementation type itself is registered
services.AddSingleton(typeof(Handler<,>), typeof(Handler<,>));
services.AddSingleton<Handler<Entity, Dto>, Handler<Entity, Dto>>();
#endregion
```

### Missing Attribute Diagnostic

When a generic factory method is referenced by `IocRegisterDefaults.Factory` or `IocRegister.Factory` but does not have `[IocGenericFactory]` attribute, the analyzer reports SGIOC016 diagnostic:

```csharp
#region Define:
[IocRegisterDefaults(typeof(IRequestHandler<>),
    ServiceLifetime.Singleton,
    Factory = nameof(FactoryContainer.Create))] // ← SGIOC016 reported here
public static class FactoryContainer
{
    // Missing [IocGenericFactory] attribute on generic factory method
    public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
}
#endregion
```

## See Also

- [Container Generics](Container.Generics.spec.md)
- [Factory Method Registration](Register.Factory.spec.md)
