# Service collection register source generator

This source generator automatically generates extension methods for registering services in Microsoft.Extensions.DependencyInjection.

## Collect information

1. Find classes marked with `SourceGen.Ioc.IoCRegisterAttribute` and `SourceGen.Ioc.IoCRegisterForAttribute`.

2. Find `SourceGen.Ioc.IoCRegisterDefaultSettingsAttribute`. If exists, apply its settings as defaults to classes that step 1 finds, unless they override with their own attributes.

3. Service registration settings to collect:
    - Service type (from `TargetServiceType`, `ServiceTypes` or follow settings: `RegisterAllInterfaces`, `RegisterAllBaseClasses`; always register Implementation type itself)
    - Implementation type (from `IoCRegisterForAttribute.TargetType` or the class marked with `IoCRegisterAttribute`) and its constructor's parameters
    - Lifetime (from `IoCRegisterAttribute` or default settings)
    - Key (from `IoCRegisterAttribute.Key`, `IoCRegisterForAttribute.Key` or default settings)
    - Decorators type (from `IoCRegisterAttribute.Decorators`, `IoCRegisterForAttribute.Decorators` or default settings) and its constructor's parameters and its type arguments constraints
    - Tags (from `IoCRegisterAttribute.Tags`, `IoCRegisterForAttribute.Tags` or default settings)
    - Factory (from `IoCRegisterAttribute.Factory`, `IoCRegisterForAttribute.Factory`)
    - Instance (from `IoCRegisterAttribute.Instance`, `IoCRegisterForAttribute.Instance`)
    - Project root namespace (from compilation options)
    - Project name (from compilation options)

4. When `KeyType` is `KeyType.Value`, take `Key` as value, when `KeyType` is `KeyType.Csharp`, take `Key` as C# code snippet.
    - `KeyType.Value`:
      - Primitive types (int, string, bool, etc.) should be represented as itself (e.g., `42`, `"myString"`, `true`)
      - Enum types should be represented as `EnumType.EnumValue`
    - `KeyType.Csharp`:
      - String should be represented literal (e.g. `"MyClass.MyStaticField"` => `Add*Key(MyClass.MyStaticField)`)
      - Can use `nameof()` for compile safety (e.g. `nameof(MyClass.MyStaticField)` => `Add*Key(MyClass.MyStaticField)`)

5. When Service type is generic type and Implementation type is closed type, make sure to register with closed generic type.

6. When multiple default settings are found on an implementation type, use first setting by order:
    1. The one directly on the implementation type
    2. The one on the closest base class
    3. The one on the first interface in `RegistrationData.AllInterfaces`

Generated code example:
```csharp
namespace {ProjectRootNamespace};

public static class ServiceCollectionExtensions
{
  public static IServiceCollection Add{ProjectName}(this IServiceCollection services)
  {
    services.AddTransient<IMyService, MyServiceImplementation>();
    services.AddSingleton(typeof(IGenericService<>), typeof(GenericServiceImplementation<>));
    services.AddScoped<IGenericService<string>, ClosedGenericServiceStringImplementation>();
    services.AddKeyedTransient<IMyKeyedService, MyKeyedServiceImplementation1>("MyKey");
      services.AddKeyedTransient<IMyKeyedService, MyKeyedServiceImplementation2>(MyEnum.EnumValue);
      services.AddKeyedTransient<IMyKeyedService, MyKeyedServiceImplementation3>(MyClass.StaticValue);
    
    return services;
  }
}
```

7. When `Decorators` is not empty, generate register code to handle decortor pattern:
    ```csharp
    #region Define:
    public interface IMyService;

    [IocRegister(
      Lifetime = ServiceLifetime.Singleton,
      ServiceTypes = [typeof(IMyService)],
      Decorators = [typeof(MyServiceDecorator), typeof(MyServiceDecorator2)])]
    public class MyService(ILogger<MyService> logger) : IMyService;

    // IocRegister attribute on decorator is optional
    [IocRegister]
    public class MyServiceDecorator(ILogger<MyServiceDecorator> logger, IMyService myservice) : IMyService
    {
      private readonly IMyService myservice = myservice;
      private readonly ILogger<MyServiceDecorator> logger = logger;
    }

    public class MyServiceDecorator2(ILogger<MyServiceDecorator2> logger, IMyService myservice) : IMyService
    {
      private readonly IMyService myservice = myservice;
      private readonly ILogger<MyServiceDecorator2> logger = logger;
    }
    #endregion

    #region Generate:
    service.AddSingleton<MyService>();
    service.AddSingleton<IMyService>((IServiceProvider sp) =>
    {
      var s0 = sp.GetRequiredService<MyService>();
      var s1_p1 = sp.GetRequiredService<ILogger<MyServiceDecorator2>>();
      var s1 = new MyServiceDecorator2(s1_p1, s0);
      var s2_p1 = sp.GetRequiredService<ILogger<MyServiceDecorator>>();
      var s2 = new MyServiceDecorator(s2_p1, s1);
      return s2;
    });
    #endregion

    #region Define:
    public interface ILogger<T>
    {
      public void Log(string msg);
    }
    [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
    public sealed class Logger<T> : ILogger<T>
    {
      public void Log(string msg)
      {
          Console.WriteLine(msg);
      }
    }

    public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

    [IoCRegisterDefaultSettings(
      typeof(IRequestHandler<,>),
      ServiceLifetime.Singleton,
      Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>)]
    )]
    public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
    {
      TResponse Handle(TRequest request);
    }

    public sealed record TestRequest(int Key) : IRequest<TestRequest, List<string>>;

    [IoCRegister]
    internal sealed class TestHandler : IRequestHandler<TestRequest, List<string>>
    {
      public List<string> Handle(TestRequest request)
      {
          return [.. Enumerable.Range(1, request.Key).Select(i => $"Value {i}")];
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
    #endregion

    #region Generate:
    services.AddSingleton<IRequestHandler<TestRequest, List<string>>>((IServiceProvider sp) =>
    {
      var s0 = sp.GetRequiredService<TestHandler>();

      var s1 = new HandlerDecorator2<TestRequest, List<string>>(s0);

      var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<TestRequest, List<string>>>>();
      var s2 = new HandlerDecorator1<TestRequest, List<string>>(s1, s2_p0);
      return s2;
    });
    #endregion
    ```
    Only generate decorator when all type arguments constraints are satisfied.

8. When Tags is not empty, generate register extension methods to handle tags:
    ```csharp
    #region Define:
    public interface IMyTaggedService;

    [IocRegister(
      Lifetime = ServiceLifetime.Singleton,
      ServiceTypes = [typeof(IMyTaggedService)],
      Tags = ["Tag1", "Tag2"])]
    public class MyTaggedServiceImplementation : IMyTaggedService;

    public interface IMyTaggedService2;

    [IocRegister(
      Lifetime = ServiceLifetime.Singleton,
      ServiceTypes = [typeof(IMyTaggedService2)],
      Tags = ["Tag1"],
      ExcludeFromDefault = true)] // Exclude from default registration
    public class MyTaggedServiceImplementation2 : IMyTaggedService2;
    #endregion

    #region Generate:
    public static class ServiceCollectionExtensions
    {
      public static IServiceCollection Add{ProjectName}(this IServiceCollection services)
      {
          services.AddSingleton<IMyTaggedService, MyTaggedServiceImplementation>();
          return services;
      }
      public static IServiceCollection Add{ProjectName}_{Tag1}(this IServiceCollection services)
      {
          services.AddSingleton<IMyTaggedService, MyTaggedServiceImplementation>();
          services.AddSingleton<IMyTaggedService2, MyTaggedServiceImplementation2>();
          return services;
      }
      public static IServiceCollection Add{ProjectName}_{Tag2}(this IServiceCollection services)
      {
          services.AddSingleton<IMyTaggedService, MyTaggedServiceImplementation>();
          return services;
      }
    }
    #endregion
    ```

9. When a class marked with `IocRegisterAttribute`, `IocRegisterForAttribute` and its members marked with `InjectAttribute`, generate the necessary code to handle the injection.

    Only check with name `InjectAttribute`, so user can use other library's attribute, like `Microsoft.AspNetCore.Components.InjectAttribute`, make sure the Key interpret logic is compatible with `Microsoft.AspNetCore.Components.InjectAttribute`.

    ```csharp
    #region Define:
    [IoCRegister]
    public class MyService(IMayServiceDependency1 sd)
    {
        private readonly IMayServiceDependency1 sd = sd;

        [Inject]
        public IMayServiceDependency2 Dependency { get; init; }

        [Inject]
        public void Initialize(IMayServiceDependency3 sd3)
        {
            // Initialization code
        }
    }
    #endregion

    #region Generate:
    public static class ServiceCollectionExtensions
    {
      public static IServiceCollection Add{ProjectName}(this IServiceCollection services)
      {
          services.AddSingleton<MyService>((IServiceProvider sp) =>
          {
              var s0_p0 = sp.GetRequiredService<IMayServiceDependency1>();
              var s0_p1 = sp.GetRequiredService<IMayServiceDependency2>();
              var s0_p2 = sp.GetRequiredService<IMayServiceDependency3>();
              var s0 = new MyService(s0_p0) { Dependency = s0_p1 };
              s0.Initialize(s0_p2);
              return s0;
          });
          return services;
      }
    }
    #endregion
    ```

10. When a class marked with `ImportModuleAttribute`, generator will get `ImportModuleAttribute.ModuleType`'s and assembly's `IoCRegisterDefaultSettingsAttribute` as default settings for current assembly.

11. When a open generic registration exists, and a class has register and its constructor has closed generic for open generic registration, generate factory to register class.

    ```csharp
    #region Define:
    public interface ILogger<T>
    {
      public void Log(string msg);
    }
    [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
    public sealed class Logger<T> : ILogger<T>
    {
      public void Log(string msg)
      {
          Console.WriteLine(msg);
      }
    }

    public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

    [IoCRegisterDefaultSettings(
      typeof(IRequestHandler<,>),
      ServiceLifetime.Singleton,
      Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>)]
    )]
    public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
    {
      TResponse Handle(TRequest request);
    }

    public sealed record TestRequest<T>(Guid PK) : IRequest<TestRequest<T>, List<T>>;

    [IoCRegister]
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

    [IoCRegister]
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
    services.AddSingleton<TestHandler<TestEntity>>((IServiceProvider sp) =>
    {
      var p0 = sp.GetRequiredService<ILogger<TestHandler<TestEntity>>>();
      var p1 = sp.GetRequiredService<IUnitOfWorkFactory>();
      var s0 = new TestHandler<TestEntity>(p0, p1);
      return s0;
    })
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

12. Search code: `GetService(Type)`, `GetService<T>()`, `GetRequiredService(Type)`, `GetRequiredService<T>()`, `GetKeyedService(Type, Key)`, `GetKeyedService<T>(Key)`, `GetRequiredKeyedService(Type, Key)`, `GetRequiredKeyedService<T>(Key)`, `GetServices(Type)`, `GetServices<T>()`, `GetKeyedServices(Type)`, `GetKeyedServices<T>()`;  
  If Type is closed generic from open generic registration, generate factory to register class.
    ```csharp
    #region Method Call:
    var handler = IServiceProvider.GetRequiredService<IRequestHandler<QueryRequest<TestEntity>>>();
    #endregion

    #region Generate:
    services.AddSingleton<IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>>>((IServiceProvider sp) =>
    {
      var s0 = sp.GetRequiredService<QueryRequestHandler<TestEntity>>();

      var s1 = new HandlerDecorator2<QueryRequest<TestEntity>, List<TestEntity>>(s0);

      var s2_p0 = sp.GetRequiredService<ILogger<HandlerDecorator1<QueryRequest<TestEntity>, List<TestEntity>>>>();
      var s2 = new HandlerDecorator1<QueryRequest<TestEntity>, List<TestEntity>>(s1, s2_p0);
      return s2;
    });
    #endregion
    ```

13. Can define csproj property: SourceGenIocName, allow user control what method name will generate.  
    - .csproj:
      ```xml
      <PropertyGroup>
          <SourceGenIocName>CustomName</SourceGenIocName>
      </PropertyGroup>
      <ItemGroup>
          <CompilerVisibleProperty Include="SourceGenIocName" />
      </ItemGroup>
      ```

    - generate:
      ```csharp
      public static class ServiceCollectionExtensions
      {
          public static IServiceCollection AddCustomName(this IServiceCollection services)
          {
              // ...registration
              return services;
          }

          public static IServiceCollection AddCustomName_Tag1(this IServiceCollection services)
          {
              // ...tag registration
              return services;
          }
      }
      ```

14. When `Factory` is specify in `IoCRegisterAttribute` or `IoCRegisterForAttribute`:
    ```csharp
    #region Define:
    public interface IMySrevice;

    [IoCRegister(ServiceTypes = [typeof(IMySrevice), Factory = nameof(MyServiceFactory.Get)])]
    internal sealed class MyService : IMySrevice;

    public sealed class MyServiceFactory
    {
      // Must be static
      // Parameter IServiceProvider and key/servicekey is optional
      public static IMySrevice Get(IServiceProvider sp)
      {
        //...
      }
    }
    #endregion

    #region Generate
    services.AddSingleton<IMySrevice>(sp => MyServiceFactory.Get(sp));
    #endregion
    ```

15. When `Instance` is specify in `IoCRegisterAttribute` or `IoCRegisterForAttribute`:
    ```csharp
    #region Define:
    public interface IMySrevice;

    [IoCRegister(ServiceTypes = [typeof(IMySrevice), Instance = nameof(Default)])]
    internal sealed class MyService : IMySrevice
    {
      // Must be static
      public static MyService Default = new MyService();
    }
    #endregion

    #region Generate:
    // When specify Instance, only allow AddSingleton or AddKeyedSingleton
    services.AddSingleton<IMySrevice>(MyService.Default);
    #endregion
    ```