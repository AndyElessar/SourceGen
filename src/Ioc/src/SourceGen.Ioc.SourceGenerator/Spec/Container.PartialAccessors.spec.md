# Partial Accessor (Fast-Path Service Resolution)

## Overview

Users can declare partial methods or partial properties in the `[IocContainer]` class. The generator automatically implements them as fast-path accessors for registered services.

## Supported Patterns

|Pattern|Declaration|Generated Implementation|
|:---|:---|:---|
|Partial method|`public partial IMyService GetMyService();`|`public partial IMyService GetMyService() => GetMyService_Resolve();`|
|Partial property|`public partial IMyService MyService { get; }`|`public partial IMyService MyService { get => GetMyService_Resolve(); }`|
|Nullable (optional)|`public partial IMyService? GetMyService();`|Returns `null` if unresolvable (no throw)|
|Keyed service|`[IocInject("key")] public partial IMyService GetMyService();`|Resolves service with specified key|
|Task of an async-init service|`public partial Task<IMyService> GetMyServiceAsync();`|`public partial Task<IMyService> GetMyServiceAsync() => GetMyService_ResolveAsync_As_IMyService();` (no `async` modifier: returns the cached task, or its cached `Task<IMyService>` projection; a transient service returns `__ProjectTask<IMyService, MyService>(CreateMyService_ResolveAsync())`)|
|Task of a synchronous service|`public partial Task<IMyService> GetMyServiceAsync();`|`public partial Task<IMyService> GetMyServiceAsync() => Task.FromResult((IMyService)GetMyService());` (no `async` modifier: only an awaited resolver needs it)|
|Wrapper of a service|`public partial Lazy<IMyService> LazyService { get; }`, `public partial Func<IMyService> GetFactory();`|The container's shared `Lazy<IMyService>` / `Func<IMyService>` field, like a constructor dependency of that type|
|Collection of a service|`public partial IEnumerable<IMyService> GetServices();`, `public partial IMyService[] Services { get; }`|The container's collection resolver (a new array per call)|
|Service of an imported module|`public partial IModuleService GetModuleService();`|`(IModuleService)GetRequiredService(typeof(IModuleService))`, which looks the service up in the merged resolvers|

## Resolution Rules

The return type of an accessor is resolved exactly like a constructor dependency of the same type and key (see [Container Injection](Container.Injection.spec.md)), with the accessor's nullability as the dependency's optionality:

1. If `[IocInject]` attribute is present, the `Key` value is used for keyed service resolution.
2. If the container registers the service, its internal resolver is called directly (fast-path).
3. Wrapper (`Lazy<T>`, `Func<T>`, `KeyValuePair<K, V>`, dictionaries) and collection return types are resolved like dependencies of that type. Their return types count as wrapper demand, so the container generates the shared `Lazy<T>` / `Func<T>` fields and collection resolvers they use.
4. `Task<T>` of a service the container does not register itself returns the `Task<T>` the container resolves (an async-init service of an imported module), otherwise the synchronous service wrapped in a completed task: `GetService(typeof(Task<T>)) as Task<T> ?? Task.FromResult((T)GetRequiredService(typeof(T)))`.
5. Any other service the container does not register itself is resolved through `GetService` (nullable accessor) or `GetRequiredService` (non-nullable accessor). These look the service up in the merged resolvers, which include the services of imported modules, and then in the fallback provider when `IntegrateServiceProvider = true`. A non-nullable accessor of a service that nothing registers throws `InvalidOperationException` when it is called; `SGIOC021` reports it at compile time when `IntegrateServiceProvider = false`.
6. A synchronous accessor of an async-init service (reported by `SGIOC027`) blocks on the service's initialization task.

## Naming Conflict Handling

If a user-declared partial method name conflicts with an internally generated resolver method name (e.g., both named `GetMyService`), the internal resolver is automatically renamed with a `_Resolve` suffix to avoid compilation errors.

## Example

```csharp
#region Define:
public interface IMyService;
public interface IOtherService;

[IocRegister<IMyService>(ServiceLifetime.Singleton)]
internal class MyService : IMyService;

[IocRegister<IOtherService>(ServiceLifetime.Singleton)]
internal class OtherService : IOtherService;

public interface ICache;
[IocRegister<ICache>(ServiceLifetime.Singleton, Key = "redis")]
internal class RedisCache : ICache;

public interface IUnknownService;

[IocContainer]
public partial class AppContainer
{
    // Fast-path method accessor
    public partial global::IMyService GetMyService();

    // Fast-path property accessor
    public partial global::IOtherService OtherService { get; }

    // Nullable (optional) accessor
    public partial global::IUnknownService? TryGetUnknown();

    // Keyed service accessor
    [IocInject("redis")]
    public partial global::ICache GetRedisCache();
}
#endregion

#region Generate:
partial class AppContainer
{
    #region Partial Accessor Implementations

    public partial global::IMyService GetMyService() => GetMyService_Resolve();

    public partial global::IOtherService OtherService { get => GetOtherService(); }

    public partial global::IUnknownService? TryGetUnknown() => GetService(typeof(global::IUnknownService)) as global::IUnknownService;

    public partial global::ICache GetRedisCache() => GetRedisCache__redis_();

    #endregion
}
#endregion
```

### Imported Modules and Wrappers

```csharp
#region Define:
public interface IModuleService;
internal class ModuleService : IModuleService;

[IocRegisterFor<ModuleService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IModuleService)])]
[IocContainer(ExplicitOnly = true)]
public partial class ServiceModule;

[IocImportModule<ServiceModule>]
[IocRegisterFor<MyService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public partial class AppContainer
{
    public partial IModuleService GetModuleService();
    public partial Task<IModuleService> GetModuleServiceAsync();
    public partial Lazy<IMyService> LazyService { get; }
    public partial IMyService[] Services { get; }
}
#endregion

#region Generate:
partial class AppContainer
{
    public partial global::IModuleService GetModuleService() => (global::IModuleService)GetRequiredService(typeof(global::IModuleService));

    public partial global::System.Threading.Tasks.Task<global::IModuleService> GetModuleServiceAsync() => (GetService(typeof(global::System.Threading.Tasks.Task<global::IModuleService>)) as global::System.Threading.Tasks.Task<global::IModuleService> ?? global::System.Threading.Tasks.Task.FromResult((global::IModuleService)GetRequiredService(typeof(global::IModuleService))));

    public partial global::System.Lazy<global::IMyService> LazyService { get => _lazy_IMyService_myService; }

    public partial global::IMyService[] Services { get => GetAllIMyServiceArray(); }
}
#endregion
```

## Analyzer Diagnostic

When `IntegrateServiceProvider = false` and a non-nullable partial accessor's return type is registered neither in the container nor in its imported modules, report error `SGIOC021: Unable to resolve service '{ServiceType}' for partial accessor '{MemberName}' in container '{ContainerType}'.` See [SGIOC021](../Analyzer/Spec/SPEC.spec.md#sgioc021---error---design---unable-to-resolve-partial-accessor-service) for the full rules.

## See Also

- [Container Options](Container.Options.spec.md)
- [Basic Container Generation](Container.Basic.spec.md)
- [Module Import](Container.ImportModule.spec.md)
