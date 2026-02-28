# IServiceCollection register source generator

This source generator automatically generates extension methods for registering services in `Microsoft.Extensions.DependencyInjection.IServiceCollection`.

## Supported Attributes

### Registration Attributes

- `IocRegisterAttribute` - Non-generic version with optional `ServiceTypes` parameter
- `IocRegisterAttribute<T>` - Generic version with single service type
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

### Generic Factory Attributes

- `IocGenericFactoryAttribute` - Map closed generic service type to generic factory method's type parameters

## Features

### 1. Basic Registration

- Always register Implementation type itself.
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

### 2. Decorator Pattern

When `Decorators` is not empty, generate register code to handle decorator pattern:\
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

### 3. Tag-Based Registration

When Tags is not empty, generate a single extension method with `params IEnumerable<string> tags` parameter to handle tag-based registration:

#### **Registration Logic** (Mutually Exclusive Model)

- Services **without** Tags: Only registered when no tags are passed (`!tags.Any()`)
- Services **with** Tags: Only registered when passed tags match (at least one tag matches)
- Tags act as mutually exclusive group selectors - passing tags switches from default to tagged services

#### **Generated Code Pattern**

- Use `!tags.Any()` for services without tags (only when no tags passed)
- Use `tags.Contains("TagName")` for tag matching (extension method syntax)
- Group registrations by tag conditions to minimize runtime checks

    ```csharp
    #region Define:
    public interface IMyTaggedService;

    [IocRegister(
        Lifetime = ServiceLifetime.Singleton,
        ServiceTypes = [typeof(IMyTaggedService)],
        Tags = ["Tag1", "Tag2"])]
    public class MyTaggedServiceImplementation : IMyTaggedService;

    public interface INoTagService;

    [IocRegister(Lifetime = ServiceLifetime.Singleton)]
    public class NoTagServiceImplementation : INoTagService;
    #endregion

    #region Generate:
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection Add{ProjectName}(this IServiceCollection services, params IEnumerable<string> tags)
        {
            // Services without tags - only register when no tags passed
            if (!tags.Any())
            {
                services.AddSingleton<NoTagServiceImplementation, NoTagServiceImplementation>();
                services.AddSingleton<INoTagService>(sp => sp.GetRequiredService<NoTagServiceImplementation>());
            }

            // Services with tags - only register when tags match
            if (tags.Contains("Tag1") || tags.Contains("Tag2"))
            {
                services.AddSingleton<MyTaggedServiceImplementation, MyTaggedServiceImplementation>();
                services.AddSingleton<IMyTaggedService>(sp => sp.GetRequiredService<MyTaggedServiceImplementation>());
            }

            return services;
        }
    }
    #endregion

    #region Usage:
    // Register only services without tags (default/development behavior)
    services.Add{ProjectName}();

    // Register only services matching "Tag1" (NOT services without tags)
    services.Add{ProjectName}("Tag1");

    // Register only services matching "Tag1" or "Tag2" (NOT services without tags)
    services.Add{ProjectName}("Tag1", "Tag2");

    // Using array
    string[] myTags = ["Tag1", "Tag2"];
    services.Add{ProjectName}(myTags);
    #endregion
    ```

### 4. Injection (Field / Property / Method / Constructor)

When a class marked with `IocRegisterAttribute`, `IocRegisterForAttribute` and its members or parameters marked with `IocInjectAttribute` or `InjectAttribute`, generate the necessary code to handle the injection.

Only check with name `IocInjectAttribute` or `InjectAttribute`, so user can use other library's attribute, like `Microsoft.AspNetCore.Components.InjectAttribute`, make sure the Key interpret logic is compatible with `Microsoft.AspNetCore.Components.InjectAttribute`.

**Important**: Factory method registration is only generated when necessary. The following cases require factory method:

- Constructor parameter has `[IocInject]` attribute (SourceGen.Ioc-specific, MS.E.DI cannot handle)
- Field/Property/Method has `[IocInject]` attribute
- Decorator pattern is used
- Factory or Instance is specified

The following cases are handled natively by MS.E.DI and do **NOT** require factory method:

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
- Wrapper types: Generate inline wrapper construction (see **Wrapper Type Resolution** below)
- Parameters with default values: See rule for optional parameters below

**Property/Field Analysis**:
Properties and fields marked with `[IocInject]` or `[Inject]` are analyzed for injection:

- Only non-static properties with a setter (`set` or `init`) are eligible
- Only non-static, non-readonly fields are eligible
- `[IocInject]` attribute with Key: Use keyed service resolution (`GetRequiredKeyedService<T>(key)` or `GetKeyedService<T>(key)`)
- `IServiceProvider` type: Pass the service provider directly
- Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
- Wrapper types: Generate inline wrapper construction (see **Wrapper Type Resolution** below)
- Nullable annotation (`T?`): Use `GetService<T>()` (non-required) instead of `GetRequiredService<T>()`
- Property/Field with initializer: Treated as having a default value, use optional resolution

**Optional Parameter Handling (constructor and method parameters)**:
When a parameter has a default value:

- Use `GetService<T>()` (non-required) and conditionally assign:
  - If the resolved value is not null: Use the resolved value
  - If the resolved value is null: Do not specify the argument (use default value)

**Wrapper Type Resolution**:
When a parameter or member type is a recognized wrapper type, the generator resolves it differently depending on whether it is a **direct wrapper** (inner type is a plain service) or a **nested wrapper** (inner type is itself a wrapper).

**Direct `Lazy<T>` / `Func<...>`**: Standalone registrations are emitted so wrapper services can be resolved directly from DI. Lifetimes and tags are inherited from the matched inner service `T`.

| Wrapper Type | Standalone Registration | Consumer Resolution |
| --- | --- | --- |
| `Lazy<T>` | `services.AddXXX<Lazy<T>>(sp => new Lazy<T>(() => sp.GetRequiredService<T>()))` | `sp.GetRequiredService<Lazy<T>>()` |
| `Func<T>` | `services.AddXXX<Func<T>>(sp => new Func<T>(() => sp.GetRequiredService<T>()))` | `sp.GetRequiredService<Func<T>>()` |
| `Func<T1, ..., TReturn>` | `services.AddXXX<Func<T1,...,TReturn>>(sp => new Func<T1,...,TReturn>((arg0,...) => ...))` | `sp.GetRequiredService<Func<T1,...,TReturn>>()` |
| `KeyValuePair<K, V>` | `services.Add(new ServiceDescriptor(typeof(KVP<K,V>), sp => ..., lifetime))` | (used by Dictionary resolution) |
| `IDictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |
| `IReadOnlyDictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |
| `Dictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |

- **Nullable wrapper types**: Use `GetService<T>()` (optional) instead of `GetRequiredService<T>()`
- **Nested wrappers**: Supported up to 2 levels. Inner wrapper types are recursively constructed **inline** (no standalone registration).
  - `Lazy<Func<T>>` → `new Lazy<Func<T>>(() => new Func<T>(() => sp.GetRequiredService<T>()))`
  - `Func<Lazy<T>>` → `new Func<Lazy<T>>(() => new Lazy<T>(() => sp.GetRequiredService<T>()))`
  - `Lazy<IEnumerable<T>>` → `new Lazy<IEnumerable<T>>(() => sp.GetServices<T>())`
  - `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` — Consumers resolve via `sp.GetServices<Lazy<T>>()` (uses standalone registrations)
- **Multi-parameter Func matching**: For `Func<T1,...,TN-1,TReturn>`, constructor parameters and injectable members are matched by type against Func inputs using first-unused semantics. Unmatched dependencies are resolved from DI.
- **Nested multi-parameter Func**: Not supported (e.g., `Lazy<Func<string, IService>>` with input parameters).
- **Open generic dependencies**: Wrapper inner types that reference closed generics trigger automatic closed generic registration (e.g., `Lazy<IHandler<TestEntity>>` → registers `Handler<TestEntity>`)
- **Factory method requirement**: Only **nested** wrapper types and **nullable** direct Lazy/Func types trigger factory method registration. Direct non-nullable Lazy/Func types resolve from their standalone registrations.
- **Tag-awareness**: Standalone `Lazy<T>`/`Func<T>`/`KeyValuePair<K, T>` registrations inherit the tags of the inner service `T` and are emitted within the same tag conditional block.

    ```csharp
    #region Define:
    [IocRegister(Lifetime = ServiceLifetime.Singleton)]
    public class Consumer(Lazy<IMyService> lazyService, Func<IOtherService> factory) { }
    #endregion

    #region Generate:
    // Standalone wrapper registrations
    services.AddSingleton<Lazy<IMyService>>(sp => new Lazy<IMyService>(() => sp.GetRequiredService<IMyService>()));
    services.AddSingleton<Func<IOtherService>>(sp => new Func<IOtherService>(() => sp.GetRequiredService<IOtherService>()));

    // Consumer resolves wrappers from DI
    services.AddSingleton<Consumer>((IServiceProvider sp) =>
    {
        var p0 = sp.GetRequiredService<global::System.Lazy<IMyService>>();
        var p1 = sp.GetRequiredService<global::System.Func<IOtherService>>();
        var s0 = new Consumer(p0, p1);
        return s0;
    });
    #endregion
    ```

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
        var s0_m1 = (int)(sp.GetService(typeof(int)) ?? 30);
        var s0 = new MyService();
        // Use named argument only when value is not null
        if (s0_m0 is not null)
        {
            s0.Initialize(optDep: s0_m0, timeout: s0_m1);
        }
        else
        {
            s0.Initialize(timeout: s0_m1);
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

    MS.E.DI native handling example (no factory needed):

    ```csharp
    #region Define:
    [IocRegister<IMyService>]
    public class MyService(
        [FromKeyedServices("special")] IDependency dep,  // MS.E.DI handles [FromKeyedServices]
        IServiceProvider sp                              // MS.E.DI handles IServiceProvider
    ) : IMyService;
    #endregion

    #region Generate:
    // Simple type-based registration - MS.E.DI handles the special parameters automatically
    services.AddSingleton<MyService, MyService>();
    services.AddSingleton<IMyService, MyService>();
    #endregion
    ```

### 5. Import Module

When a class marked with `IocImportModuleAttribute` (or `IocImportModuleAttribute<T>`), generator will get `IocImportModuleAttribute.ModuleType`'s (or `T` type's) assembly's `IocRegisterDefaultsAttribute` as default settings for current assembly.

### 6. Closed Generic Registration from Open Generic

When a open generic registration exists, and a class has register and its constructor has closed generic for open generic registration, generate closed generic registration.

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

### 7. IServiceProvider Invocation Discovery

Search code: `GetService(Type)`, `GetService<T>()`, `GetRequiredService(Type)`, `GetRequiredService<T>()`, `GetKeyedService(Type, Key)`, `GetKeyedService<T>(Key)`, `GetRequiredKeyedService(Type, Key)`, `GetRequiredKeyedService<T>(Key)`, `GetServices(Type)`, `GetServices<T>()`, `GetKeyedServices(Type)`, `GetKeyedServices<T>()`;\
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

### 8. MSBuild Properties

MSBuild properties for customizing generated code:\
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

### 9. Factory Method Registration

When `Factory` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

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

**Factory in DefaultSettings**:

When `Factory` is specified in `IocRegisterDefaultsAttribute`, it applies as the default factory for all services implementing the target type. Explicit `Factory` specified in `IocRegisterAttribute` takes precedence over the default.

**Important**: When using Factory in DefaultSettings:

- The factory method must be compatible with all implementations that match the target service type
- The factory method should typically return the target service type (interface/base class)
- Factory is only applied when the registration doesn't have its own explicit Factory

```csharp
#region Define:
public interface IMyHandler { void Handle(); }

// DefaultSettings with Factory - applies to all IMyHandler implementations
[assembly: IocRegisterDefaults(
    typeof(IMyHandler),
    ServiceLifetime.Scoped,
    Factory = nameof(HandlerFactory.Create))]

public static class HandlerFactory
{
    public static IMyHandler Create(IServiceProvider sp)
    {
        // Custom creation logic for all handlers
        var handler = sp.GetRequiredService<MyHandlerImpl>();
        // Additional setup...
        return handler;
    }
}

[IocRegister]
public class MyHandlerImpl : IMyHandler
{
    public void Handle() { }
}

// This handler uses explicit Factory, overrides default
[IocRegister(Factory = nameof(SpecialHandlerFactory.Create))]
public class SpecialHandler : IMyHandler
{
    public void Handle() { }
}

public static class SpecialHandlerFactory
{
    public static IMyHandler Create(IServiceProvider sp) => new SpecialHandler();
}
#endregion

#region Generate:
// MyHandlerImpl uses Factory from DefaultSettings
services.AddScoped<IMyHandler>(sp => HandlerFactory.Create(sp));

// SpecialHandler uses its own explicit Factory
services.AddScoped<IMyHandler>(sp => SpecialHandlerFactory.Create(sp));
#endregion
```

**Generic DefaultSettings with Factory**:

```csharp
#region Define:
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

[assembly: IocRegisterDefaults<IRequestHandler<,>>(
    ServiceLifetime.Singleton,
    Factory = nameof(HandlerFactory.CreateHandler))]

public static class HandlerFactory
{
    public static object CreateHandler(IServiceProvider sp, [ServiceKey] object? key)
    {
        // Factory for all IRequestHandler implementations
        // Note: For generic handlers, factory typically creates the concrete handler
        // and returns it as the interface type
    }
}

[IocRegister]
public class QueryHandler : IRequestHandler<QueryRequest, QueryResponse>
{
    public QueryResponse Handle(QueryRequest request) => new();
}
#endregion

#region Generate:
services.AddSingleton<IRequestHandler<QueryRequest, QueryResponse>>(sp => HandlerFactory.CreateHandler(sp, null));
#endregion
```

### 10. Instance Registration

When `Instance` is specify in `IocRegisterAttribute` or `IocRegisterForAttribute`:

```csharp
#region Define:
public interface IMyService;

[IocRegister(ServiceTypes = [typeof(IMyService)], Instance = nameof(Default))]
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

### 11. Explicit Closed Generic Discovery

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

### 12. Generic Factory Type Mapping

When registrations is specified `IocRegisterAttribute.Factory` or `IocRegisterDefaultsAttribute.Factory` and factory method is generic, the method must be marked with `[IocGenericFactory]` attribute to provide type parameter mapping information.

**Purpose**: When a generic factory method is used with open generic service registration, the generator needs to know how to map the actual type arguments from the discovered closed generic type to the factory method's type parameters.

**Attribute Signature**:

```csharp
[IocGenericFactory(params Type[] genericTypeMap)]
```

**Parameters**:

- First type: Service type template with placeholder types (e.g., `IRequestHandler<Task<int>>`)
- Following types: Placeholder types that correspond to factory method's type parameters in order

**Rules**:

- The factory method must have the same number of type parameters as placeholder types provided
- Each placeholder type should be unique - duplicate placeholders cannot be distinguished and will not generate registration
- The service type template must match the structure of the open generic service type in the `IocRegisterDefaults`
- If `[IocGenericFactory]` is missing on a generic factory method, SGIOC016 diagnostic is reported

**Single Type Parameter Example**:

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

**Two Type Parameters Example**:

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

**Reversed Mapping Example** (type parameter order differs from service type order):

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

**With IServiceProvider Parameter**:

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

**Invalid: Duplicate Placeholder Types** (registration is NOT generated):

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

**Missing Attribute Diagnostic** (SGIOC016):

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

### 13. KeyValuePair and Dictionary Registration

When consumer dependencies include `KeyValuePair<K, V>`, `IDictionary<K, V>`, `IReadOnlyDictionary<K, V>`, `Dictionary<K, V>`, or `IEnumerable<KeyValuePair<K, V>>`, the generator also produces explicit `KeyValuePair<K, V>` service registrations for each matching keyed service.

**Purpose**: `IDictionary<K, V>` and similar types resolve via `sp.GetServices<KeyValuePair<K, V>>().ToDictionary()`. For this to work, `KeyValuePair<K, V>` must be registered as a service. Without explicit registrations, `GetServices<KeyValuePair<K, V>>()` returns an empty collection.

**Behavior**:

- Triggered by consumer dependencies — only keyed services that match a needed `(K, V)` pair produce KVP registrations
- **Key type filtering**: a keyed service is only matched when its `KeyValueType` is compatible with the consumer's key type `K`:
  - `K` is `object` → matches **all** keyed services (regardless of their key type)
  - `KeyValueType` is `null` (e.g., `KeyType=Csharp` without `nameof()`) → matches only `object` consumers
  - Otherwise → `KeyValueType.Name` must match `K` exactly (case-sensitive)
- Lifetime matches the keyed value service's lifetime
- Uses `ServiceDescriptor` directly because `KeyValuePair<K, V>` is a struct (generic `AddXxx<T>` has a `class` constraint)
- Registrations are emitted after all normal service registrations, before `return services;`

```csharp
#region Define:
[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "h1")]
public class Handler1 : IHandler { }

[IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "h2")]
public class Handler2 : IHandler { }

[IocRegister(Lifetime = ServiceLifetime.Singleton)]
public class Registry(IDictionary<string, IHandler> handlers) { }
#endregion

#region Generate:
services.AddKeyedSingleton<Handler1, Handler1>("h1");
services.AddKeyedSingleton<IHandler>("h1", (sp, key) => sp.GetRequiredKeyedService<Handler1>(key));
services.AddKeyedScoped<Handler2, Handler2>("h2");
services.AddKeyedScoped<IHandler>("h2", (sp, key) => sp.GetRequiredKeyedService<Handler2>(key));
services.AddSingleton<Registry>((sp) =>
{
    var p0 = sp.GetServices<KeyValuePair<string, IHandler>>().ToDictionary();
    return new Registry(p0);
});

// KeyValuePair registrations for keyed services
services.Add(new ServiceDescriptor(typeof(KeyValuePair<string, IHandler>),
    (sp) => (object)new KeyValuePair<string, IHandler>("h1", sp.GetRequiredKeyedService<IHandler>("h1")),
    ServiceLifetime.Singleton));
services.Add(new ServiceDescriptor(typeof(KeyValuePair<string, IHandler>),
    (sp) => (object)new KeyValuePair<string, IHandler>("h2", sp.GetRequiredKeyedService<IHandler>("h2")),
    ServiceLifetime.Scoped));
#endregion
```
