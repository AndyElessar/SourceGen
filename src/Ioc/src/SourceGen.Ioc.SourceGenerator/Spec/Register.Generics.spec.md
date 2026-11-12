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

A dependency MUST NOT close an implementation that was closed to reach it with a type argument the new one contains, because such dependencies never end: closing `Node<T>(Lazy<INode<List<T>>> next)` as `Node<string>` does not close `Node<List<string>>`, and `INode<List<string>>` is resolved like an unregistered service. A dependency rejected on one path MUST still be closed when another registration collects it on a path it does not expand, independently of the order in which the dependencies are discovered.

Decorator dependencies of an open generic registration, such as `ILogger<HandlerDecorator1<TRequest, TResponse>>`, still contain its type parameters and MUST NOT be collected as closed generic dependencies. Only their closed forms, from closed generic registrations, are collected.

### Type Argument Mapping

The implementation's type arguments are found by matching the service type as the implementation declares it (one of its interfaces, one of its base classes, or itself) against the closed service type, at every nesting level. Type parameter names and order MAY differ from the service type's definition. A closed generic registration MUST NOT be generated when the closed service type does not have the declared shape, when one type parameter would stand for two different types, or when a type parameter of the implementation is not determined.

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
- Tuple element names are part of a type here, although C# converts between them by identity: `(int A, int B)` does not satisfy `where T : U` with `U = (int X, int Y)`, so the registration is not closed and SGIOC018 is reported.
- A type with required members, declared or inherited, only satisfies `new()` when its parameterless constructor has `[SetsRequiredMembers]` (CS9040).
- The type arguments of a closed decorator keep their hierarchy, so the dependencies of the decorator, such as `IRepo<T>` in `Decorator<T>(IService<T> inner, IRepo<T> repo)`, are closed with the same constraint checks as other dependencies.
- Covariant conversions are followed one level, with a type argument replaced by one of its interfaces or base classes (`List<string>` satisfies `IEnumerable<object>`); contravariant and nested variant conversions are not. At most 64 conversions are enumerated for each covariant interface, so a type argument with many interfaces may not satisfy a conversion beyond them. An interface type argument does not convert to `object` this way, because an interface has no base class; this is tracked in [#173](https://github.com/AndyElessar/SourceGen/issues/173). Arrays satisfy constraints to the interfaces they implement (`int[]` satisfies `IEnumerable<int>`, `string[]` satisfies `IEnumerable<object>`).
- The covariant conversions of a type argument of a dependency of a closed generic registration are not recomputed for its substituted type arguments: `List<T>` in `Outer<T>(IRepo<List<T>>)` keeps `IEnumerable<string>` for `Outer<string>`, but not `IEnumerable<object>`. This is a current limitation, tracked in [#173](https://github.com/AndyElessar/SourceGen/issues/173).
- A generic value type that is a type argument of a dependency of a closed generic registration, such as `KeyValuePair<T, int>` in `Outer<T>(IRepo<KeyValuePair<T, int>>)`, does not satisfy `unmanaged`, because whether it is unmanaged depends on its fields.

| Implementation | Closed service type | Closed implementation |
| :--- | :--- | :--- |
| `Repo<T> : IRepo<T> where T : struct` | `IRepo<int>` | `Repo<int>` |
| `Repo<T> : IRepo<T> where T : struct` | `IRepo<string>` | none |
| `Repo<T> : IRepo<T> where T : IEntity` | `IRepo<Entity>` (`Entity : IEntity`) | `Repo<Entity>` |

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

- The factory method must have the same number of type parameters as placeholder types provided, and each placeholder type must appear in the service type template; otherwise SGIOC016 is reported and the registration is not generated
- Each placeholder type should be unique - duplicate placeholders cannot be distinguished and will not generate registration
- The service type template must match the structure of the open generic service type in the `IocRegisterDefaults`
- If `[IocGenericFactory]` is missing on a generic factory method, SGIOC016 diagnostic is reported
- The factory is closed per service type, so its result is the service type's own instance: it MUST be cast to the service type, and the implementation type MUST keep resolving its own (constructed) registration
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
