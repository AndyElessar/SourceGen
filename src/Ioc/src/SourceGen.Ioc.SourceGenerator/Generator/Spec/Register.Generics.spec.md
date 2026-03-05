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
services.AddSingleton<TestHandler<TestEntity>>();
services.AddSingleton<IRequestHandler<TestRequest<TestEntity>, List<TestEntity>>>((IServiceProvider sp) =>
{
    var s0 = sp.GetRequiredService<TestHandler<TestEntity>>();

    var s1 = new HandlerDecorator2<TestRequest<TestEntity>, List<TestEntity>>(s0);

    var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>>>();
    var s2 = new HandlerDecorator1<TestRequest<TestEntity>, List<TestEntity>>(s1, s2_p0);
    return s2;
});
#endregion
```

## Feature 2: Explicit Closed Generic Discovery

When `IocDiscoverAttribute` (or `IocDiscoverAttribute<T>`) is exists, collect `IocDiscoverAttribute.ClosedGenericType` (or `T`) for generate factory code for open generic registrations.

```csharp
#region Define:
public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

[IocRegister]
public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>;

public class ViewModel
{
    // Non-generic version with type parameter
    [IocDiscover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
    // Or generic version
    // [IocDiscover<IRequestHandler<TestRequest<string>, List<string>>>]
    public void DoAction()
    {
    var response = Mediator.Send(new TestRequest<string>());
    }
}
#endregion

#region Generate:
services.AddSingleton<IRequestHandler<TestRequest<string>, List<string>>>((IServiceProvider sp) =>
{
    var s0 = sp.GetRequiredService<TestRequestHandler<string>>();
    return s0;
});
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

- The factory method must have the same number of type parameters as placeholder types provided
- Each placeholder type should be unique - duplicate placeholders cannot be distinguished and will not generate registration
- The service type template must match the structure of the open generic service type in the `IocRegisterDefaults`
- If `[IocGenericFactory]` is missing on a generic factory method, SGIOC016 diagnostic is reported

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
services.AddSingleton<IRequestHandler<Task<Entity>>>(sp =>
    (IRequestHandler<Task<Entity>>)FactoryContainer.Create<Entity>());
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
services.AddSingleton<IRequestHandler<Task<Entity>, List<Dto>>>(sp =>
    (IRequestHandler<Task<Entity>, List<Dto>>)FactoryContainer.Create<Entity, Dto>());
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
services.AddSingleton<IRequestHandler<Task<Entity>, List<Dto>>>(sp =>
    (IRequestHandler<Task<Entity>, List<Dto>>)FactoryContainer.Create<Dto, Entity>());
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

public class Entity { }

[IocDiscover<IRequestHandler<Task<Entity>>>]
public sealed class App { }
#endregion

#region Generate:
// Factory method receives IServiceProvider
services.AddSingleton<IRequestHandler<Task<Entity>>>(sp =>
    (IRequestHandler<Task<Entity>>)FactoryContainer.Create<Entity>(sp));
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

public class Entity { }
public class Dto { }

[IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
public sealed class App { }
#endregion

#region Generate:
// NO factory registration is generated because placeholders are not unique
// Only the implementation type itself is registered
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
