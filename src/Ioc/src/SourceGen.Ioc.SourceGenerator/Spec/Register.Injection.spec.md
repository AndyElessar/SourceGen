# Injection (Field / Property / Method / Constructor)

## Overview

Full support for member and constructor injection via `[IocInject]` and `[Inject]` attributes. The generator handles factory method generation when necessary and supports complex scenarios including optional parameters, keyed services, and wrapper types.

## Feature: Injection (Field / Property / Method / Constructor)

When a class marked with `IocRegisterAttribute`, `IocRegisterForAttribute` and its members or parameters marked with `IocInjectAttribute` or `InjectAttribute`, generate the necessary code to handle the injection.

Only check with name `IocInjectAttribute` or `InjectAttribute`, so user can use other library's attribute, like `Microsoft.AspNetCore.Components.InjectAttribute`, make sure the Key interpret logic is compatible with `Microsoft.AspNetCore.Components.InjectAttribute`.

### Factory Method Generation Requirements

**Important**: Factory method registration is only generated when necessary. The following cases require factory method:

- Constructor parameter has `[IocInject]` attribute (SourceGen.Ioc-specific, MS.E.DI cannot handle)
- Field/Property/Method has `[IocInject]` attribute
- Decorator pattern is used
- Factory or Instance is specified

The following cases are handled natively by MS.E.DI and do **NOT** require factory method:

- `[FromKeyedServices]` attribute on constructor parameters
- `[ServiceKey]` attribute on constructor parameters
- `IServiceProvider` parameter in constructor

### Method Parameter Analysis

Method parameters marked with `[IocInject]` must be analyzed using the same logic as constructor parameters:

- `[ServiceKey]` attribute: Inject the registration key (or default if non-keyed)
- `[FromKeyedServices]` attribute: Use keyed service resolution
- `[IocInject]` attribute with Key: Use keyed service resolution
- `IServiceProvider` type: Pass the service provider directly
- Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
- Wrapper types: Generate inline wrapper construction (see **Wrapper Type Resolution** below)
- Parameters with default values: See rule for optional parameters below

### Property/Field Analysis

Properties and fields marked with `[IocInject]` or `[Inject]` are analyzed for injection:

- Only non-static properties with a setter (`set` or `init`) are eligible
- Only non-static, non-readonly fields are eligible
- `[IocInject]` attribute with Key: Use keyed service resolution (`GetRequiredKeyedService<T>(key)` or `GetKeyedService<T>(key)`)
- `IServiceProvider` type: Pass the service provider directly
- Collection types (`IEnumerable<T>`, `T[]`, `IReadOnlyList<T>`, etc.): Use `GetServices<T>()` or `GetKeyedServices<T>(key)`
- Wrapper types: Generate inline wrapper construction (see **Wrapper Type Resolution** below)
- Nullable annotation (`T?`): Use `GetService<T>()` (non-required) instead of `GetRequiredService<T>()`
- Property/Field with initializer: Treated as having a default value, use optional resolution. An initializer of `null`, `null!`, `default` or `default!` marks a required member, not a default value.

### Optional Parameter Handling (constructor and method parameters)

When a parameter has a default value, it MUST receive the resolved service, or its default value when the service is not registered. The Register and Container output MUST compile for any parameter type:

|Parameter|Resolution|
|---|---|
|Default value `null` and nullable type (`IDependency? dep = null`, `int? count = null`)|`sp.GetService<T>() ?? default`|
|Any other default value (`int timeout = 30`, `string name = "x"`, `Mode mode = Mode.Safe`)|`(T)(sp.GetService(typeof(T)) ?? defaultValue)`|
|Non-nullable type defaulting to `null` or `default` (`Settings settings = default`)|`(T)(sp.GetService(typeof(T)) ?? default(T)!)`|

- The untyped `GetService(typeof(T))` form is required because `GetService<T>()` returns `default` for an unregistered value type, which cannot be told apart from a registered value.
- Keyed parameters use `GetKeyedService<T>(key)` / `GetKeyedService(typeof(T), key)` in the same way.
- An enum default value MUST be cast to the enum type (`(Mode)(1)`), because the parameter symbol stores its underlying constant.
- Properties and fields with a non-null initializer use the same untyped form with the initializer expression.

### Wrapper Type Resolution

When a parameter or member type is a recognized wrapper type, the generator resolves it differently depending on whether it is a **direct wrapper** (inner type is a plain service) or a **nested wrapper** (inner type is itself a wrapper).

**Direct `Lazy<T>` / `Func<...>`**: Standalone registrations are emitted so wrapper services can be resolved directly from DI. Lifetimes and tags are inherited from the matched inner service `T`.

One `Lazy<T>` / parameterless `Func<T>` registration is emitted per registration of `T`, so `IEnumerable<Lazy<T>>` contains one wrapper per implementation. Each MUST resolve that registration's instance, the same instance as resolving `T`:

|Registration of `T`|Wrapper registration|Resolves|
|---|---|---|
|Non-keyed|`services.AddXXX<Lazy<T>>(sp => ...)`|`sp.GetRequiredService<Impl>()`|
|Non-keyed, decorated|`services.AddXXX<Lazy<T>>(sp => ...)`|`sp.GetRequiredKeyedService<T>("SourceGen.Ioc.Decorated:{T}\|{Impl}")`, the hidden key of the decorator chain (see [Decorators](Register.Decorators.spec.md))|
|Keyed|`services.AddKeyedXXX<Lazy<T>>(key, (sp, key) => ...)`|`sp.GetRequiredKeyedService<Impl>(key)`|
|Keyed, decorated|`services.AddKeyedXXX<Lazy<T>>(key, (sp, key) => ...)`|`sp.GetRequiredKeyedService<T>(key)`|

A keyed registration MUST NOT emit a non-keyed wrapper registration.

`Func<T1, ..., TReturn>` with input parameters is a factory: each call MUST construct a **new** implementation instance from the arguments, also for singleton and scoped registrations, because the arguments may differ between calls (unlike the parameterless `Func<T>`, which follows the lifetime). The call MUST then wrap the instance in the registration's decorators, also new per call; the arguments are only matched to the implementation, and decorator dependencies are resolved from DI. A keyed registration MUST be registered as a keyed Func (`services.AddKeyedXXX<Func<T1,...,TReturn>>(key, (sp, key) => ...)`), resolved through `[FromKeyedServices(key)]`, and the implementation's `[ServiceKey]` parameters MUST receive the registration key.

| Wrapper Type | Standalone Registration | Consumer Resolution |
| --- | --- | --- |
| `Lazy<T>` | `services.AddXXX<Lazy<T>>(sp => new Lazy<T>(() => sp.GetRequiredService<Impl>(), LazyThreadSafetyMode.ExecutionAndPublication))` (resolution as in the table above) | `sp.GetRequiredService<Lazy<T>>()` |
| `Func<T>` | `services.AddXXX<Func<T>>(sp => new Func<T>(() => sp.GetRequiredService<Impl>()))` (resolution as in the table above) | `sp.GetRequiredService<Func<T>>()` |
| `Func<T1, ..., TReturn>` | `services.AddXXX<Func<T1,...,TReturn>>(sp => new Func<T1,...,TReturn>((arg0,...) => ...))` | `sp.GetRequiredService<Func<T1,...,TReturn>>()` |
| `KeyValuePair<K, V>` | `services.Add(new ServiceDescriptor(typeof(KVP<K,V>), sp => ..., lifetime))` | (used by Dictionary resolution) |
| `IDictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |
| `IReadOnlyDictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |
| `Dictionary<K, V>` | — | `sp.GetServices<KeyValuePair<K, V>>().ToDictionary(...)` |

- **Nullable wrapper types**: Use `GetService<T>()` (optional) instead of `GetRequiredService<T>()`
- **Nested wrappers**: Non-collection outer wrappers (`Lazy<T>`, `Func<T>`) are recursively resolved to arbitrary depth via inline construction. Collection outer wrappers support at most **1 level of inner wrapping** (2 levels total). Inner wrapper types are constructed **inline** (no standalone registration).
  - `Lazy<Func<T>>` → `new Lazy<Func<T>>(() => new Func<T>(() => sp.GetRequiredService<T>()))`
  - `Func<Lazy<T>>` → `new Func<Lazy<T>>(() => new Lazy<T>(() => sp.GetRequiredService<T>()))`
  - `Lazy<IEnumerable<T>>` → `new Lazy<IEnumerable<T>>(() => sp.GetServices<T>())`
  - `IEnumerable<Lazy<T>>` / `IEnumerable<Func<T>>` — Consumers resolve via `sp.GetServices<Lazy<T>>()` (uses standalone registrations)
  - Collection outer wrapper with 3+ levels (e.g., `IEnumerable<Lazy<Func<T>>>`) is **not** supported. No wrapper registrations are emitted; the consumer is registered with a plain `AddXXX<Consumer, Consumer>()` call and the parameter is left to MS.DI runtime resolution.
  - `ValueTask<T>` is **not** a recognized wrapper type in any context. Only `Task<T>` is supported for async-init wrapping. When used as a partial accessor return type: if the target service uses async-init, `SGIOC029` is reported; otherwise `SGIOC021` is reported.
- **Multi-parameter Func matching**: For `Func<T1,...,TN-1,TReturn>`, constructor parameters and injectable members are matched by type against Func inputs using first-unused semantics. Unmatched dependencies are resolved from DI.
- **Nested multi-parameter Func**: Not supported (e.g., `Lazy<Func<string, IService>>` with input parameters).
- **Open generic dependencies**: Wrapper inner types that reference closed generics trigger automatic closed generic registration (e.g., `Lazy<IHandler<TestEntity>>` → registers `Handler<TestEntity>`)
- **Factory method requirement**: Only **nested** wrapper types and **nullable** direct Lazy/Func types trigger factory method registration. Direct non-nullable Lazy/Func types resolve from their standalone registrations.
- **Tag-awareness**: Standalone `Lazy<T>`/`Func<T>`/`KeyValuePair<K, T>` registrations inherit the tags of the inner service `T` and are emitted within the same tag conditional block.

### Async Method Injection

`AsyncMethodInject` extends post-construction member injection with awaited initialization methods. The generator MUST treat async method injection as a distinct injection stage that runs after all synchronous injection steps.

#### Classification Rules

|Condition|Required behavior|
|:--------|:----------------|
|Method has `[IocInject]`/`[Inject]`, is an ordinary instance method, returns non-generic `Task`, and `AsyncMethodInject` is enabled|MUST classify the member as `InjectionMemberType.AsyncMethod`.|
|Method has `[IocInject]`/`[Inject]` and returns `void`|MUST continue to classify the member as `InjectionMemberType.Method`.|
|Method returns `Task<T>`|MUST NOT classify the member as async method injection. `Task<T>` is not a supported injection-method return type.|
|Method returns `ValueTask` or `ValueTask<T>`|MUST NOT classify the member as async method injection.|
|Method returns `Task` but `AsyncMethodInject` is disabled|The generator MUST treat the member as feature-gated; the analyzer owns the user-facing warning via `SGIOC022`.|

#### Ordering Contract

The generator MUST emit member injection in the following fixed stage order. Source declaration order applies **within** each stage.

|Stage|Members|Emission rule|
|:----|:------|:------------|
|1|Properties|MUST be assigned first, in source declaration order.|
|2|Fields|MUST be assigned second, in source declaration order.|
|3|Synchronous methods|MUST be invoked third, in source declaration order.|
|4|Async methods|MUST be awaited last, in source declaration order.|

```mermaid
flowchart LR
    A[Construct instance] --> B[Assign properties]
    B --> C[Assign fields]
    C --> D[Call sync inject methods]
    D --> E[await async inject methods]
    E --> F[Return completed Task<TService>]
```

#### Register-Path Generation

When a registration contains one or more async inject methods, the registration generator MUST emit a `Task<TImplementation>` registration, and a `Task<TService>` registration for each service type that awaits it. The `Task<TImplementation>` factory MUST create an `async Task<TImplementation> Init()` local function, perform synchronous injection first, then `await` each async inject method in stage order.

```csharp
#region Define:
using System.Threading.Tasks;

public interface ILogger { }
public interface IAsyncInitializer
{
    Task InitializeAsync();
}

public interface IMyService { }

[IocRegister(ServiceTypes = [typeof(IMyService)], Lifetime = ServiceLifetime.Singleton)]
public class MyService : IMyService
{
    [IocInject]
    public ILogger Logger { get; set; } = default!;

    [IocInject]
    public void InitializeSync()
    {
    }

    [IocInject]
    public async Task InitializeAsync(IAsyncInitializer initializer)
    {
        await initializer.InitializeAsync();
    }
}
#endregion

#region Generate:
services.AddSingleton<Task<MyService>>((IServiceProvider sp) =>
{
    async Task<MyService> Init()
    {
        var s0_p0 = sp.GetRequiredService<ILogger>();
        var s0_m1 = sp.GetRequiredService<IAsyncInitializer>();
        var s0 = new MyService() { Logger = s0_p0 };
        s0.InitializeSync();
        await s0.InitializeAsync(s0_m1);
        return s0;
    }

    return Init();
});
services.AddSingleton<Task<IMyService>>(async (IServiceProvider sp) => await sp.GetRequiredService<Task<MyService>>());
#endregion
```

#### `WrapperKind.Task` Detection and Resolution

`Task<T>` is the async-init wrapper for consumer dependencies and partial accessors. The generator MUST only recognize the direct, non-nested form. Resolution MUST distinguish whether `T` itself has an async-init service path or only a synchronous service path.

|Requested type|Classification|Resolution behavior|
|:-------------|:-------------|:------------------|
|`Task<T>` where `T` is a non-wrapper service type|`WrapperKind.Task`|MUST classify as `WrapperKind.Task`. Resolution MUST follow the async-init vs. sync-only rules below.|
|`Task<Lazy<T>>`|Unsupported|MUST NOT be classified as `WrapperKind.Task`.|
|`Lazy<Task<T>>`|Unsupported|MUST NOT be classified as a supported nested wrapper combination.|
|`IEnumerable<Task<T>>`|Unsupported|MUST NOT be classified as a supported collection wrapper.|

|Inner service `T` shape|Register-path behavior|Container-path behavior|
|:----------------------|:---------------------|:----------------------|
|Async-init service (`T` resolves through generated `Task<T>` init path)|MUST resolve `Task<T>` directly via `sp.GetRequiredService<Task<T>>()` or `sp.GetService<Task<T>>()`, depending on requiredness/default handling rules.|MUST call the generated async resolver path for `Task<T>` directly.|
|Sync-only service (`T` only has synchronous resolution)|MUST wrap the synchronous resolution as `Task.FromResult(sp.GetRequiredService<T>())` or the matching optional/default-aware `Task.FromResult(...)` form.|MUST wrap the sync resolver result as `Task.FromResult(self.GetT_Resolve())` or the equivalent generated sync resolver call.|

```csharp
// Invalid: nested async wrappers are not supported.
[IocRegister]
public class BadConsumer
{
    [IocInject]
    public void Initialize(Task<Lazy<IMyService>> service)
    {
    }
}
```

### Examples

**Basic Injection**:

```csharp
#region Define:
public interface IMayServiceDependency1;
public interface IMayServiceDependency2;
public interface IMayServiceDependency3;

[IocRegister(ServiceLifetime.Singleton)]
public class MyService([IocInject(Key = 10)] IMayServiceDependency1 sd)
{
    private readonly IMayServiceDependency1 sd = sd;

    [IocInject]
    public IMayServiceDependency2 Dependency { get; init; } = default!;

    [IocInject]
    public void Initialize(IMayServiceDependency3 sd3)
    {
        // Initialization code
    }
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    var p0 = sp.GetRequiredKeyedService<IMayServiceDependency1>(10);
    var s0_p0 = sp.GetRequiredService<IMayServiceDependency2>();
    var s0_m1 = sp.GetRequiredService<IMayServiceDependency3>();
    var s0 = new MyService(p0) { Dependency = s0_p0 };
    s0.Initialize(s0_m1);
    return s0;
});
#endregion
```

**Method with `[ServiceKey]` and `[FromKeyedServices]` on parameters**:

```csharp
#region Define:
public interface IMyService;
public interface IDependency;

[IocRegister(ServiceLifetime.Singleton, Key = "MyKey")]
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
services.AddKeyedSingleton<MyService>("MyKey", (IServiceProvider sp, object? key) =>
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
public interface IMyService;
public interface IOptionalDependency;

[IocRegister(ServiceLifetime.Singleton)]
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
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    // An unregistered service gets the parameter's default value (see Optional Parameter Handling)
    var s0_m0 = sp.GetService<IOptionalDependency>() ?? default;
    var s0_m1 = (int)(sp.GetService(typeof(int)) ?? 30);
    var s0 = new MyService();
    s0.Initialize(s0_m0, s0_m1);
    return s0;
});
#endregion
```

**Constructor with optional resolvable parameter**:

```csharp
#region Define:
public interface IMyService;
public interface IOptionalDependency;

[IocRegister(ServiceLifetime.Singleton)]
public class MyService(IOptionalDependency? optDep = null) : IMyService
{
    public IOptionalDependency? OptDep { get; } = optDep;
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    var p0 = sp.GetService<IOptionalDependency>() ?? default;
    var s0 = new MyService(p0);
    return s0;
});
#endregion
```

**Property and Field injection with nullable and keyed services**:

```csharp
#region Define:
public interface IMyService;
public interface IDependency;
public interface IOptionalDependency;
public interface ILogger;
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();
}

[IocRegister(ServiceLifetime.Singleton)]
public class MyService : IMyService
{
    // Required property injection with key (default! marks it required, not a default value)
    [IocInject(Key = "special")]
    public IDependency Dep { get; init; } = default!;

    // Nullable property injection (uses GetService instead of GetRequiredService)
    [IocInject]
    public IOptionalDependency? OptionalDep { get; set; }

    // Property with initializer (treated as optional)
    [IocInject]
    public ILogger Logger { get; set; } = NullLogger.Instance;

    // Field injection (requires the FieldInject feature; the field must be accessible)
    [IocInject]
    internal IServiceProvider ServiceProvider = default!;
}
#endregion

#region Generate:
services.AddSingleton<MyService>((IServiceProvider sp) =>
{
    var s0_p0 = sp.GetRequiredKeyedService<IDependency>("special");
    var s0_p1 = sp.GetService<IOptionalDependency>();
    var s0_p2 = (ILogger)(sp.GetService(typeof(ILogger)) ?? NullLogger.Instance);
    var s0_p3 = sp;
    var s0 = new MyService() { Dep = s0_p0, OptionalDep = s0_p1, Logger = s0_p2, ServiceProvider = s0_p3 };
    return s0;
});
#endregion
```

**MS.E.DI native handling example (no factory needed)**:

```csharp
#region Define:
public interface IMyService;
public interface IDependency;

[IocRegister<IMyService>(ServiceLifetime.Singleton)]
public class MyService(
    [FromKeyedServices("special")] IDependency dep,  // MS.E.DI handles [FromKeyedServices]
    IServiceProvider sp                              // MS.E.DI handles IServiceProvider
) : IMyService;
#endregion

#region Generate:
// Simple type-based registration - MS.E.DI handles the special parameters automatically
services.AddSingleton<MyService, MyService>();
services.AddSingleton<IMyService>((IServiceProvider sp) => sp.GetRequiredService<MyService>());
#endregion
```

**Wrapper type resolution**:

```csharp
#region Define:
public interface IMyService;
public interface IOtherService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
public class MyService : IMyService;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IOtherService)])]
public class OtherService : IOtherService;

[IocRegister(Lifetime = ServiceLifetime.Singleton)]
public class Consumer(Lazy<IMyService> lazyService, Func<IOtherService> factory) { }
#endregion

#region Generate:
// ...MyService and OtherService registrations...

// Consumer resolves the wrappers from DI, so MS.E.DI constructs it directly
services.AddSingleton<Consumer, Consumer>();

// Standalone wrapper registrations for the registered inner services, resolving their implementations
services.AddSingleton<Lazy<IMyService>>((IServiceProvider sp) => new Lazy<IMyService>(() => sp.GetRequiredService<MyService>(), LazyThreadSafetyMode.ExecutionAndPublication));
services.AddSingleton<Func<IOtherService>>((IServiceProvider sp) => new Func<IOtherService>(() => sp.GetRequiredService<OtherService>()));
#endregion
```

## See Also

- [Decorators](Register.Decorators.spec.md)
- [Factory Method Registration](Register.Factory.spec.md)
- [Container Injection](Container.Injection.spec.md)
