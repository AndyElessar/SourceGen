# Container Attribute Options

## Overview

The `[IocContainer]` attribute provides multiple configuration options to customize container behavior including thread safety, eager resolution, service filtering, and resolution strategy.

## Container Attribute Options

```csharp
[IocContainer(
    IntegrateServiceProvider = true,   // Allow fallback to IServiceCollection and implement IServiceProviderFactory
    ExplicitOnly = false,              // Only register explicitly marked services
    IncludeTags = ["Tag1", "Tag2"],    // Only include services with specified tags
    UseSwitchStatement = false,        // Use FrozenDictionary by default; set true to use switch statement
    ThreadSafeStrategy = ThreadSafeStrategy.Lock,  // Thread safety strategy for singleton/scoped resolution
    EagerResolveOptions = EagerResolveOptions.Singleton  // Eager resolution for singleton/scoped services
)]
public partial class MyContainer;
```

|Property|Default|Description|
|:---|:---|:---|
|`IntegrateServiceProvider`|`true`|Whether the container should integrate with external IServiceProvider and implement `IServiceProviderFactory<IServiceCollection>`|
|`ExplicitOnly`|`false`|When true, only register services explicitly marked on the container class|
|`IncludeTags`|`[]`|When non-empty, only include services that have at least one matching tag. Services without tags are excluded.|
|`UseSwitchStatement`|`false`|When true, use cascading `if`/`switch` statements instead of `FrozenDictionary`. Only beneficial for small service counts (≤ 50). **Note**: When there are imported modules (`IocImportModuleAttribute`), `FrozenDictionary` is always used regardless of this setting, because combining services from multiple sources requires dictionary-based lookup.|
|`ThreadSafeStrategy`|`Lock`|Thread safety strategy for singleton and scoped service resolution. See [ThreadSafeStrategy](Container.ThreadSafety.spec.md) for details.|
|`EagerResolveOptions`|`Singleton`|Controls which service lifetimes should be eagerly resolved during container/scope construction. See [EagerResolveOptions](#eagerresolveoptions-enum) for details.|

> **Priority**: `ExplicitOnly` takes precedence over `IncludeTags`. When `ExplicitOnly = true`, `IncludeTags` is ignored.
>
> **Priority**: `IocImportModule` takes precedence over `UseSwitchStatement`. When there are imported modules, `UseSwitchStatement` is ignored and `FrozenDictionary` is always used.

## IntegrateServiceProvider = false

When disabled, the container operates in standalone mode without fallback to external `IServiceProvider`.

```csharp
#region Define:
[IocContainer(IntegrateServiceProvider = false)]
public partial class StandaloneContainer;
#endregion

#region Generate:
partial class StandaloneContainer
{
    // No fallback provider field

    #region Constructors

    public StandaloneContainer()
    {
        _serviceResolvers = _localResolvers.ToFrozenDictionary();
    }

    #endregion

    public object? GetService(Type serviceType)
    {
        ThrowIfDisposed();

        if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, KeyedService.AnyKey), out var resolver))
            return resolver(this);

        return null;
    }
}
#endregion
```

**Analyzer Diagnostic**: When a dependency cannot be resolved and `IntegrateServiceProvider = false`, report error `SGIOC018: Unable to resolve service '{ServiceType}' for container '{ContainerType}'.`

## ExplicitOnly = true

Only register services explicitly marked on the container class:

```csharp
#region Define:
// These are NOT registered (not on container class)
[IocRegister<IService1>]
public class Service1 : IService1;

// This IS registered (on container class)
[IocRegisterFor<Service2>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService2)])]
[IocContainer(ExplicitOnly = true)]
public partial class ExplicitContainer;
#endregion
```

## IncludeTags

When `IncludeTags` is non-empty, the container only includes services that have at least one matching tag. Services without any tags or with non-matching tags are excluded.

> **Note**: `ExplicitOnly` takes precedence over `IncludeTags`. When `ExplicitOnly = true`, `IncludeTags` is ignored.

```csharp
#region Define:
// This IS registered (has matching tag "Feature1")
[IocRegister<IService1>(ServiceLifetime.Singleton, Tags = ["Feature1"])]
public class Service1 : IService1;

// This IS registered (has matching tag "Feature2")
[IocRegister<IService2>(ServiceLifetime.Singleton, Tags = ["Feature2", "Feature3"])]
public class Service2 : IService2;

// This is NOT registered (no matching tag)
[IocRegister<IService3>(ServiceLifetime.Singleton, Tags = ["Feature3"])]
public class Service3 : IService3;

// This is NOT registered (no tags defined)
[IocRegister<IService4>(ServiceLifetime.Singleton)]
public class Service4 : IService4;

[IocContainer(IncludeTags = ["Feature1", "Feature2"])]
public partial class FeatureContainer;
#endregion
```

### Use Cases

1. **Feature Flags**: Include/exclude features at compile time based on build configurations
2. **Module Separation**: Create specialized containers for different deployment scenarios
3. **Testing**: Create test containers with only specific tagged services

## EagerResolveOptions Enum

The `EagerResolveOptions` enum controls which service lifetimes should be eagerly resolved during container/scope construction. Eager services are initialized immediately when the container or scope is created, rather than on first access.

|Option|Value|Description|
|:---|:---|:---|
|`None`|`0`|Do not eagerly resolve any services. All services are lazily resolved on first access.|
|`Singleton`|`1`|Eagerly resolve all singleton services when the root container is created. **(Default)**|
|`Scoped`|`2`|Eagerly resolve all scoped services when a scope is created.|
|`SingletonAndScoped`|`3`|Eagerly resolve both singleton and scoped services.|

> **Note**: `Transient` services are not supported for eager resolution, as they create a new instance on every access by design.

See [Eager Resolution](Container.Performance.spec.md) for detailed generated code examples.

## See Also

- [Thread Safety](Container.ThreadSafety.spec.md)
- [Partial Accessors](Container.PartialAccessors.spec.md)
- [IServiceProviderFactory](Container.Options.spec.md#integrateserviceprovider--false)
