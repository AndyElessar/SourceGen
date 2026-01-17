# IServiceCollection register source generator

This source generator automatically generates extension methods for registering services in `Microsoft.Extensions.DependencyInjection.IServiceCollection`.

## Supported Attributes

### Registration Attributes

- `IocRegisterAttribute` - Non-generic version with optional `ServiceTypes` parameter
- `IocRegisterAttribute<T>` - Generic version with single service type
- `IocRegisterAttribute<T1,T2>` - Generic version with two service types
- `IocRegisterAttribute<T1,T2,T3>` - Generic version with three service types
- `IocRegisterAttribute<T1,T2,T3,T4>` - Generic version with four service types
- `IocRegisterForAttribute` - Non-generic version for registering external types
- `IocRegisterForAttribute<T>` - Generic version for registering external types

### Default Settings Attributes

- `IocRegisterDefaultsAttribute` - Non-generic version with `TargetType` parameter
- `IocRegisterDefaultsAttribute<T>` - Generic version for default settings

### Module Import Attributes

- `IocImportModuleAttribute` - Non-generic version with `ModuleType` parameter
- `IocImportModuleAttribute<T>` - Generic version for importing module settings

### Discovery Attributes

- `IocDiscoverAttribute` - Non-generic version with `ClosedGenericType` parameter
- `IocDiscoverAttribute<T>` - Generic version for closed generic discovery

### Injection Attributes

- `IocInjectAttribute` - For field, property, method, or constructor parameter injection
- `InjectAttribute` - Alternative name (compatible with other libraries like `Microsoft.AspNetCore.Components.InjectAttribute`)

## Features

1. Basic:
    - Always register Implementation type itself.\
    - When Service type is open generic type and Implementation type is closed type, make sure to register with closed generic type.

    ```csharp
      #region Define:
      public interface IMyService;
      [IocRegister<IMyService>(ServiceLifetime.Transient)]
      internal class MyServiceImplementation : IMyService;

      public interface IGenericService<T>;
      [IocRegister(ServiceLifetime.Singleton, typeof(IGenericService<>))]
      internal class GenericServiceImplementation<T> : IGenericService<T>;
      [IocRegister(ServiceLifetime.Scoped, typeof(IGenericService<>))]
      internal class ClosedGenericServiceStringImplementation : IGenericService<string>;

      public interface IMyKeyedService;
      [IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = "MyKey")]
      public class MyKeyedServiceImplementation1;
      [IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = MyEnum.EnumValue)]
      public class MyKeyedServiceImplementation2;
      [IocRegister<IMyKeyedService>(ServiceLifetime.Transient, Key = nameof(MyClass.StaticValue), KeyType = KeyType.Csharp)]
      public class MyKeyedServiceImplementation3;

      public static class MyClass
      {
          public static Guid StaticValue => Guid.Newid();
      }
      #endregion

      #region Generate:
      namespace {ProjectRootNamespace};

      public static class ServiceCollectionExtensions
      {
          public static IServiceCollection Add{ProjectName}(this IServiceCollection services)
          {
              //always register Implementation type itself
              services.AddTransient<MyServiceImplementation, MyServiceImplementation>();
              services.AddTransient<IMyService>(sp=>sp.GetRequiredService<MyServiceImplementation>());
              services.AddSingleton(typeof(IGenericService<>), typeof(GenericServiceImplementation<>));
              services.AddScoped<ClosedGenericServiceStringImplementation, ClosedGenericServiceStringImplementation>();
              services.AddScoped<IGenericService<string>>(sp=>sp.GetRequiredService<ClosedGenericServiceStringImplementation>());
              services.AddKeyedTransient<MyKeyedServiceImplementation1, MyKeyedServiceImplementation1>("MyKey");
              services.AddKeyedTransient<IMyKeyedService>("MyKey", (sp, key)=>sp.GetKeyedService<MyKeyedServiceImplementation1>());
              services.AddKeyedTransient<MyKeyedServiceImplementation2, MyKeyedServiceImplementation2>(MyEnum.EnumValue);
              services.AddKeyedTransient<IMyKeyedService>(MyEnum.EnumValue, (sp, key)=>sp.GetKeyedService<MyKeyedServiceImplementation2>());
              services.AddKeyedTransient<MyKeyedServiceImplementation3, MyKeyedServiceImplementation3>(MyClass.StaticValue);
              services.AddKeyedTransient<IMyKeyedService>(MyClass.StaticValue, (sp, key)=>sp.GetKeyedService<MyKeyedServiceImplementation3>());
              
              return services;
          }
      }
      #endregion
      ```

2. When `Decorators` is not empty, generate register code to handle decorator pattern:\
    Only generate decorator when all type arguments constraints are satisfied.

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
    [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
    public sealed class Logger<T> : ILogger<T>
    {
        public void Log(string msg)
        {
            Console.WriteLine(msg);
        }
    }

    public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

    [IocRegisterDefaultSettings(
        typeof(IRequestHandler<,>),
        ServiceLifetime.Singleton,
        Decorators = [typeof(HandlerDecorator1<,>), typeof(HandlerDecorator2<,>)]
    )]
    public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
    {
        TResponse Handle(TRequest request);
    }

    public sealed record TestRequest(int Key) : IRequest<TestRequest, List<string>>;

    [IocRegister]
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

3. When Tags is not empty, generate register extension methods to handle tags:

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
        TagOnly = true)] // Only register in tag-specific methods
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

4. When a class marked with `IocRegisterAttribute`, `IocRegisterForAttribute` and its members or parameters marked with `IocInjectAttribute` or `InjectAttribute`, generate the necessary code to handle the injection.

    Only check with name `IocInjectAttribute` or `InjectAttribute`, so user can use other library's attribute, like `Microsoft.AspNetCore.Components.InjectAttribute`, make sure the Key interpret logic is compatible with `Microsoft.AspNetCore.Components.InjectAttribute`.

    **Important**: Factory method registration is only generated when necessary. The following cases require factory method:
    - Constructor parameter has `[IocInject]` attribute (SourceGen.Ioc-specific, MS.DI cannot handle)
    - Field/Property/Method has `[IocInject]` attribute
    - Decorator pattern is used
    - Factory or Instance is specified

    The following cases are handled natively by MS.DI and do **NOT** require factory method:
    - `[FromKeyedServices]` attribute on constructor parameters
    - `[ServiceKey]` attribute on constructor parameters
    - `IServiceProvider` parameter in constructor

    **Method Parameter Analysis (same as constructor parameters)**:
    Method parameters marked with `[IocInject]` must be analyzed using the same logic as constructor parameters:
    - `[ServiceKey]` attribute: Inject the registration key (or default if non-keyed)
    - `[FromKeyedServices]` attribute: Use keyed service resolution
    - `[IocInject]` attribute with Key: Use keyed service resolution
    - `IServiceProvider` type: Pass the service provider directly
    - Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
    - Built-in types without attributes and without default value: Skip (unresolvable)
    - Parameters with default values: See rule for optional parameters below

    **Property/Field Analysis**:
    Properties and fields marked with `[IocInject]` or `[Inject]` are analyzed for injection:
    - Only non-static properties with a setter (`set` or `init`) are eligible
    - Only non-static, non-readonly fields are eligible
    - `[IocInject]` attribute with Key: Use keyed service resolution (`GetRequiredKeyedService<T>(key)` or `GetKeyedService<T>(key)`)
    - `IServiceProvider` type: Pass the service provider directly
    - Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
    - Built-in types without Key and without initializer: Skip (unresolvable)
    - Nullable annotation (`T?`): Use `GetService<T>()` (non-required) instead of `GetRequiredService<T>()`
    - Property/Field with initializer: Treated as having a default value, use optional resolution

    **Optional Parameter Handling (constructor and method parameters)**:
    When a parameter has a default value:
    - If the type is a built-in type (int, string, etc.) or collection of built-in types: Skip (use default value)
    - If the type is resolvable from DI: Use `GetService<T>()` (non-required) and conditionally assign:
      - If the resolved value is not null: Use the resolved value
      - If the resolved value is null: Do not specify the argument (use default value)

    ```csharp
    #region Define:
    [IocRegister]
    public class MyService([Inject(Key = 10)]IMayServiceDependency1 sd)
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
                    var s0_p0 = sp.GetRequiredKeyedService<IMayServiceDependency1>(10);
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

    **Method with `[ServiceKey]` and `[FromKeyedServices]` on parameters**:

    ```csharp
    #region Define:
    [IocRegister(Key = "MyKey")]
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
    services.AddKeyedSingleton<MyService>("MyKey", (global::System.IServiceProvider sp, object? key) =>
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
    [IocRegister]
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
    services.AddSingleton<MyService>((global::System.IServiceProvider sp) =>
    {
        var s0_m0 = sp.GetService<IOptionalDependency>();
        // timeout is built-in type with default value - skip
        var s0 = new MyService();
        // Use named argument only when value is not null
        if (s0_m0 is not null)
        {
            s0.Initialize(optDep: s0_m0);
        }
        else
        {
            s0.Initialize();
        }
        return s0;
    });
    #endregion
    ```

    **Constructor with optional resolvable parameter**:

    ```csharp
    #region Define:
    [IocRegister]
    public class MyService(IOptionalDependency? optDep = null) : IMyService
    {
        public IOptionalDependency? OptDep { get; } = optDep;
    }
    #endregion

    #region Generate:
    services.AddSingleton<MyService>((global::System.IServiceProvider sp) =>
    {
        var p0 = sp.GetService<IOptionalDependency>();
        // Use named argument only when value is not null
        var s0 = p0 is not null ? new MyService(optDep: p0) : new MyService();
        return s0;
    });
    #endregion
    ```

    **Property and Field injection with nullable and keyed services**:

    ```csharp
    #region Define:
    [IocRegister]
    public class MyService : IMyService
    {
        // Required property injection with key
        [IocInject(Key = "special")]
        public IDependency Dep { get; init; }

        // Nullable property injection (uses GetService instead of GetRequiredService)
        [Inject]
        public IOptionalDependency? OptionalDep { get; set; }

        // Property with initializer (treated as optional)
        [Inject]
        public ILogger Logger { get; set; } = NullLogger.Instance;

        // Field injection
        [Inject]
        private IServiceProvider _serviceProvider;
    }
    #endregion

    #region Generate:
    services.AddSingleton<MyService>((global::System.IServiceProvider sp) =>
    {
        var s0_dep = sp.GetRequiredKeyedService<IDependency>("special");
        var s0_optDep = sp.GetService<IOptionalDependency>();
        var s0_logger = sp.GetService<ILogger>();
        var s0 = new MyService
        {
            Dep = s0_dep,
            OptionalDep = s0_optDep,
            Logger = s0_logger ?? NullLogger.Instance,
            _serviceProvider = sp
        };
        return s0;
    });
    #endregion
    ```

    MS.DI native handling example (no factory needed):

    ```csharp
    #region Define:
    [IocRegister<IMyService>]
    public class MyService(
        [FromKeyedServices("special")] IDependency dep,  // MS.DI handles [FromKeyedServices]
        IServiceProvider sp                              // MS.DI handles IServiceProvider
    ) : IMyService;
    #endregion

    #region Generate:
    // Simple type-based registration - MS.DI handles the special parameters automatically
    services.AddSingleton<MyService, MyService>();
    services.AddSingleton<IMyService, MyService>();
    #endregion
    ```

5. When a class marked with `IocImportModuleAttribute` (or `IocImportModuleAttribute<T>`), generator will get `IocImportModuleAttribute.ModuleType`'s (or `T` type's) assembly's `IocRegisterDefaultsAttribute` as default settings for current assembly.

6. When a open generic registration exists, and a class has register and its constructor has closed generic for open generic registration, generate closed generic registration.

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

    [IocRegisterDefaultSettings(
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

7. Search code: `GetService(Type)`, `GetService<T>()`, `GetRequiredService(Type)`, `GetRequiredService<T>()`, `GetKeyedService(Type, Key)`, `GetKeyedService<T>(Key)`, `GetRequiredKeyedService(Type, Key)`, `GetRequiredKeyedService<T>(Key)`, `GetServices(Type)`, `GetServices<T>()`, `GetKeyedServices(Type)`, `GetKeyedServices<T>()`;  
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

8. MSBuild properties for customizing generated code:\
    **RootNamespace**: Controls the namespace of generated extension class. If not set, falls back to assembly name.\
    **SourceGenIocName**: Controls the method name suffix. If not set, uses assembly name.

    - .csproj:

      ```xml
      <PropertyGroup>
          <RootNamespace>MyCompany.MyProject</RootNamespace>
          <SourceGenIocName>CustomName</SourceGenIocName>
      </PropertyGroup>
      <ItemGroup>
          <CompilerVisibleProperty Include="RootNamespace" />
          <CompilerVisibleProperty Include="SourceGenIocName" />
      </ItemGroup>
      ```

    - generate:

      ```csharp
      namespace MyCompany.MyProject;

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

9. When `Factory` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

    **Factory Method Parameter Analysis**:
    - If parameter type is `IServiceProvider`: Pass the service provider directly
    - If parameter has `[ServiceKey]` attribute and registration has a Key: Pass the Key value
    - Other parameters are not supported and should be ignored or report diagnostic

    ```csharp
    #region Define:
    public interface IMyService;

    [IocRegister(ServiceTypes = [typeof(IMyService)], Factory = nameof(MyServiceFactory.Get))]
    internal sealed class MyService : IMyService;

    public sealed class MyServiceFactory
    {
      // Must be static
      // Parameter IServiceProvider is optional
      public static IMyService Get(IServiceProvider sp)
      {
        //...
      }
    }
    #endregion

    #region Generate:
    services.AddSingleton<IMyService>(sp => MyServiceFactory.Get(sp));
    #endregion
    ```

    **Keyed Factory with `[ServiceKey]` parameter**:

    ```csharp
    #region Define:
    public interface IMyService;

    [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "MyKey", Factory = nameof(MyServiceFactory.Create))]
    internal sealed class MyService : IMyService;

    public sealed class MyServiceFactory
    {
      public static IMyService Create(IServiceProvider sp, [ServiceKey] string key)
      {
        // Use key to customize creation
        return new MyService();
      }
    }
    #endregion

    #region Generate:
    services.AddKeyedSingleton<IMyService>("MyKey", (sp, key) => MyServiceFactory.Create(sp, "MyKey"));
    #endregion
    ```

    **Factory without IServiceProvider parameter**:

    ```csharp
    #region Define:
    public interface IMyService;

    [IocRegister(ServiceTypes = [typeof(IMyService)], Factory = nameof(MyServiceFactory.Create))]
    internal sealed class MyService : IMyService;

    public sealed class MyServiceFactory
    {
      public static IMyService Create()
      {
        return new MyService();
      }
    }
    #endregion

    #region Generate:
    services.AddSingleton<IMyService>(sp => MyServiceFactory.Create());
    #endregion
    ```

10. When `Instance` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

    ```csharp
    #region Define:
    public interface IMyService;

    [IocRegister(ServiceTypes = [typeof(IMyService), Instance = nameof(Default)])]
    internal sealed class MyService : IMyService
    {
      // Must be static
      public static MyService Default = new MyService();
    }
    #endregion

    #region Generate:
    // When specify Instance, only allow AddSingleton or AddKeyedSingleton
    services.AddSingleton<IMyService>(MyService.Default);
    #endregion
    ```

11. When `IocDiscoverAttribute` (or `IocDiscoverAttribute<T>`) is exists, collect `IocDiscoverAttribute.ClosedGenericType` (or `T`) for generate factory code for open generic registrations.

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
