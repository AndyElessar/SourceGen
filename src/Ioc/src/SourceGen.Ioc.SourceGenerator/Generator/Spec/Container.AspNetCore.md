# MVC and Blazor Activator Interfaces

## Overview

The container can generate host-specific activator implementations for ASP.NET Core MVC and Blazor when the container partial class explicitly declares these interfaces.

## Interface Support Matrix

|Interface|Generation Trigger|Generated Behavior|
|:---|:---|:---|
|`IControllerActivator`|Container partial class declares `IControllerActivator` **AND** `Microsoft.AspNetCore.Mvc.Core` is referenced|Generates controller activation using `ActivatorUtilities.CreateFactory` and caches factories in `ConcurrentDictionary<Type, ObjectFactory>`|
|`IComponentActivator`|Container partial class declares `IComponentActivator` **AND** `Microsoft.AspNetCore.Components` is referenced|Generates component activation using `ActivatorUtilities.CreateFactory` and caches factories in `ConcurrentDictionary<Type, ObjectFactory>`; includes hot reload cache invalidation handler|
|`IComponentPropertyActivator`|Container partial class declares `IComponentPropertyActivator` **AND** `Microsoft.AspNetCore.Components` ≥ 11 (which exposes the type)|For registered components (when also implementing `IComponentActivator`): returns no-op delegate. For unregistered/unknown components: uses reflection to inject `[Inject]`/`[InjectAttribute]` properties, caching activators in `ConcurrentDictionary<Type, Action<IServiceProvider, IComponent>>`; includes hot reload cache invalidation handler|

Both activators use the container instance (`this`) as the service provider when invoking cached factories.

> **Important**: SourceGen.Ioc's source generator only analyzes `.cs` files. For Blazor components, the `[IocContainer]` attribute and interface declarations (e.g., `: IComponentActivator`) **must** be placed in a code-behind file (`.razor.cs` or a separate `.cs` file), not in a `.razor` file. The source generator cannot read `.razor` files, so attributes declared there will not trigger code generation.

## `IControllerActivator` Example

```csharp
#region Define:
using Microsoft.AspNetCore.Mvc.Controllers;

[IocContainer]
public partial class AppContainer : IControllerActivator;
#endregion

#region Generate:
partial class AppContainer : IControllerActivator, /* ... other interfaces */
{
    private static readonly ConcurrentDictionary<Type, ObjectFactory> _controllerFactoryCache = new();

    object IControllerActivator.Create(ControllerContext context)
    {
        var controllerType = context.ActionDescriptor.ControllerTypeInfo.AsType();
        var instance = GetService(controllerType);
        if(instance is not null) return instance;

        if (!_controllerFactoryCache.TryGetValue(controllerType, out var factory))
        {
            factory = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory(controllerType, Type.EmptyTypes);
            _controllerFactoryCache.TryAdd(controllerType, factory);
        }

        return factory(this, []);
    }

    void IControllerActivator.Release(ControllerContext context, object controller)
    {
        if(controller is IDisposable disposable) disposable.Dispose();
    }
}
#endregion
```

## `IComponentActivator` Example

```csharp
#region Define:
// NOTE: Must be in a .cs file (code-behind), not in a .razor file.
// The source generator only reads .cs files.
using Microsoft.AspNetCore.Components;

[IocContainer]
public partial class AppContainer : IComponentActivator;
#endregion

#region Generate:
[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::MyApp.AppContainer.__HotReloadHandler))]

partial class AppContainer : IComponentActivator, /* ... other interfaces */
{
    private static readonly ConcurrentDictionary<Type, ObjectFactory> _componentFactoryCache = new();

    IComponent IComponentActivator.CreateInstance(Type componentType)
    {
        var instance = GetService(componentType);
        if(instance is IComponent component) return component;

        if (!_componentFactoryCache.TryGetValue(componentType, out var factory))
        {
            factory = global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory(componentType, Type.EmptyTypes);
            _componentFactoryCache.TryAdd(componentType, factory);
        }

        return (IComponent)factory(this, []);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class __HotReloadHandler
    {
        public static void ClearCache(Type[]? _)
        {
            _componentFactoryCache.Clear();
        }
    }
}
#endregion
```

## `IComponentPropertyActivator` Example

```csharp
#region Define:
// NOTE: Must be in a .cs file (code-behind), not in a .razor file.
using Microsoft.AspNetCore.Components;

[IocContainer]
public partial class AppContainer : IComponentActivator, IComponentPropertyActivator;
#endregion

#region Generate:
[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::MyApp.AppContainer.__HotReloadHandler))]

partial class AppContainer : IComponentActivator, IComponentPropertyActivator, /* ... other interfaces */
{
    private static readonly ConcurrentDictionary<Type, ObjectFactory> _componentFactoryCache = new();
    private static readonly ConcurrentDictionary<Type, Action<IServiceProvider, IComponent>> _propertyActivatorCache = new();

    global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent> global::Microsoft.AspNetCore.Components.IComponentPropertyActivator.GetActivator(
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] global::System.Type componentType)
    {
        if (!_propertyActivatorCache.TryGetValue(componentType, out var activator))
        {
            // When also implementing IComponentActivator, registered components
            // already have property injection done by the container's resolver → no-op
            activator = _serviceResolvers.ContainsKey(new ServiceIdentifier(componentType, KeyedService.AnyKey))
                ? static (_, _) => { }
                : CreateComponentPropertyInjector(componentType);
            _propertyActivatorCache.TryAdd(componentType, activator);
        }
        return activator;
    }

    private static Action<IServiceProvider, IComponent> CreateComponentPropertyInjector(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
    {
        // Uses reflection to find [InjectAttribute] properties and create injection delegate
        // Supports keyed services via InjectAttribute.Key
        // ...
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class __HotReloadHandler
    {
        public static void ClearCache(Type[]? _)
        {
            _componentFactoryCache.Clear();
            _propertyActivatorCache.Clear();
        }
    }
}
#endregion
```

## Key Design Notes for `IComponentPropertyActivator`

1. **Version Detection**: `IComponentPropertyActivator` type only exists in `Microsoft.AspNetCore.Components` v11+. Detection uses `GetTypeByMetadataName()` — if the type doesn't exist, generation is skipped.

2. **No-op Optimization**: When the container also implements `IComponentActivator`, registered components already have their `[Inject]` properties injected during `CreateInstance` (which routes through the container's resolver with property/field/method injection). In this case, `GetActivator` returns a no-op delegate to avoid redundant injection.

3. **Reflection Fallback**: For unregistered components (not in `_serviceResolvers`), or when only `IComponentPropertyActivator` is implemented without `IComponentActivator`, the generator produces a reflection-based fallback that walks the type hierarchy for `[InjectAttribute]` properties and injects them at runtime. This mirrors `DefaultComponentPropertyActivator` from ASP.NET Core.

4. **Important Note**: When implementing only `IComponentPropertyActivator` without `IComponentActivator`, the no-op optimization is NOT applied because there's no guarantee the component's constructor injection went through the container. All components use the reflection fallback in this case.

## Explicit Interface Implementation Notes

1. Generated activator members are explicit interface implementations (`IControllerActivator.Create`, `IControllerActivator.Release`, `IComponentActivator.CreateInstance`).
2. Explicit implementation avoids naming collisions with user-defined members on the same partial container class.
3. Consumers call these members through the interface contract (for example, via ASP.NET Core host infrastructure), not as public container methods.

## Hot Reload Support

When `IComponentActivator` or `IComponentPropertyActivator` is implemented, the generator also emits:

1. **`[assembly: MetadataUpdateHandler]` attribute**: Points to an internal nested `__HotReloadHandler` static class on the container, registering it as a handler for hot reload events.

2. **`__HotReloadHandler` nested class**: Contains a static `ClearCache(Type[]?)` method that the .NET runtime calls when a hot reload occurs. The method clears:
   - `_componentFactoryCache` (if `IComponentActivator` is implemented)
   - `_propertyActivatorCache` (if `IComponentPropertyActivator` is implemented)
   - Both caches if both interfaces are implemented

### When Generated

- Hot reload handler is **only generated** when at least one of `IComponentActivator` or `IComponentPropertyActivator` is implemented.
- The `ClearCache` method only clears the caches that exist based on which interfaces are implemented.
- If neither interface is implemented, no hot reload handler is generated.

## See Also

- [Basic Container Generation](Container.Basic.md)
- [IServiceProviderFactory](Container.Options.md)
