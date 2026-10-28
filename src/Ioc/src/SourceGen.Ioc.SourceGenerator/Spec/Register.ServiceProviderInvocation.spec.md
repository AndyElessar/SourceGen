# IServiceProvider Invocation Discovery

## Overview

The generator automatically discovers service types used in `IServiceProvider` method calls and generates registrations for closed generic types discovered through invocations.

## Feature: IServiceProvider Invocation Discovery

Search code for: `GetService(Type)`, `GetService<T>()`, `GetRequiredService(Type)`, `GetRequiredService<T>()`, `GetKeyedService(Type, Key)`, `GetKeyedService<T>(Key)`, `GetRequiredKeyedService(Type, Key)`, `GetRequiredKeyedService<T>(Key)`, `GetServices(Type)`, `GetServices<T>()`, `GetKeyedServices(Type)`, `GetKeyedServices<T>()`.

If Type is closed generic from open generic registration, generate factory to register class.

```csharp
#region Method Call:
// With the open generic handler registration and decorators of Generic Registration Feature 1
var handler = serviceProvider.GetRequiredService<IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>>>();
#endregion

#region Generate:
services.AddSingleton<QueryRequestHandler<TestEntity>, QueryRequestHandler<TestEntity>>();
services.AddKeyedSingleton<IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>>>("SourceGen.Ioc.Decorated:IRequestHandler<QueryRequest<TestEntity>, System.Collections.Generic.List<TestEntity>>|QueryRequestHandler<TestEntity>", (IServiceProvider sp, object? key) =>
{
    var s0 = sp.GetRequiredService<QueryRequestHandler<TestEntity>>();
    var s1 = new HandlerDecorator2<QueryRequest<TestEntity>, List<TestEntity>>(s0);
    var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<QueryRequest<TestEntity>, List<TestEntity>>>>();
    var s2 = new HandlerDecorator1<QueryRequest<TestEntity>, List<TestEntity>>(s1, s2_p0);
    return s2;
});
services.AddSingleton<IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>>>((IServiceProvider sp) => sp.GetRequiredKeyedService<IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>>>("SourceGen.Ioc.Decorated:IRequestHandler<QueryRequest<TestEntity>, System.Collections.Generic.List<TestEntity>>|QueryRequestHandler<TestEntity>"));
#endregion
```

## See Also

- [Generic Registration](Register.Generics.spec.md)
- [Container Generics](Container.Generics.spec.md)
