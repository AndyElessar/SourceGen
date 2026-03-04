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

## Resolution Rules

1. The return type is matched against registered service types (using fully qualified name)
2. If `[IocInject]` attribute is present, the `Key` value is used for keyed service resolution
3. If a matching internal resolver exists, it is called directly (fast-path)
4. If no internal resolver exists but `IntegrateServiceProvider = true`, fallback to `GetService`/`GetRequiredService`
5. If no resolver exists and `IntegrateServiceProvider = false`, nullable returns `default`, non-nullable throws

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

    public partial global::ICache GetRedisCache() => GetCache_redis();

    #endregion
}
#endregion
```

## Analyzer Diagnostic

When `IntegrateServiceProvider = false` and a non-nullable partial accessor's return type is not registered, report error `SGIOC021: Unable to resolve service '{ServiceType}' for partial accessor '{MemberName}' in container '{ContainerType}'.`

## See Also

- [Container Options](Container.Options.md)
- [Basic Container Generation](Container.Basic.md)
