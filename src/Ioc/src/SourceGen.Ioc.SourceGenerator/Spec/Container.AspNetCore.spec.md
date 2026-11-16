# MVC and Blazor Activator Interfaces

## Overview

The container can generate host-specific activator implementations for ASP.NET Core MVC and Blazor when the container partial class explicitly declares these interfaces.

## Interface Support Matrix

|Interface|Generation Trigger|Generated Behavior|
|:---|:---|:---|
|`IControllerActivator`|Container partial class declares `IControllerActivator` **AND** `Microsoft.AspNetCore.Mvc.Core` is referenced|Generates controller activation using `ActivatorUtilities.CreateFactory` and caches factories in `ConcurrentDictionary<Type, ObjectFactory>`|
|`IComponentActivator`|Container partial class declares `IComponentActivator` **AND** `Microsoft.AspNetCore.Components` is referenced|Generates component activation using `ActivatorUtilities.CreateFactory` and caches factories in `ConcurrentDictionary<Type, ObjectFactory>`; includes hot reload cache invalidation handler|
|`IComponentPropertyActivator`|Container partial class declares `IComponentPropertyActivator` **AND** `Microsoft.AspNetCore.Components` ≥ 11 (which exposes the type)|For registered components (when also implementing `IComponentActivator`): returns no-op delegate. For unregistered/unknown components: uses reflection to inject properties marked with `Microsoft.AspNetCore.Components.InjectAttribute` or `SourceGen.Ioc.IocInjectAttribute` (matched by type; see Key Design Notes), caching activators in `ConcurrentDictionary<Type, Action<IServiceProvider, IComponent>>`; includes hot reload cache invalidation handler|

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
    private static ObjectFactory CreateControllerFactory(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type t)
        => ActivatorUtilities.CreateFactory(t, Type.EmptyTypes);

    object IControllerActivator.Create(ControllerContext controllerContext)
    {
        ArgumentNullException.ThrowIfNull(controllerContext);
        var controllerType = controllerContext.ActionDescriptor.ControllerTypeInfo.AsType();
        var instance = GetService(controllerType);
        if (instance is not null) return instance;

        if (!_controllerFactoryCache.TryGetValue(controllerType, out var factory))
        {
            factory = CreateControllerFactory(controllerType);
            _controllerFactoryCache.TryAdd(controllerType, factory);
        }

        return factory(this, []);
    }

    void IControllerActivator.Release(ControllerContext context, object controller)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(controller);
        if (controller is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    ValueTask IControllerActivator.ReleaseAsync(ControllerContext context, object controller)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(controller);
        if (controller is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        ((IControllerActivator)this).Release(context, controller);
        return default;
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

namespace MyApp;

[IocContainer]
public partial class AppContainer : IComponentActivator;
#endregion

#region Generate:
[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::MyApp.AppContainer.__HotReloadHandler))]

partial class AppContainer : IComponentActivator, /* ... other interfaces */
{
    private static readonly ConcurrentDictionary<Type, ObjectFactory> _componentFactoryCache = new();
    private static ObjectFactory CreateComponentFactory(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type t)
        => ActivatorUtilities.CreateFactory(t, Type.EmptyTypes);

    IComponent IComponentActivator.CreateInstance([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type componentType)
    {
        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException($"The type {componentType.FullName} does not implement {nameof(IComponent)}.", nameof(componentType));
        }

        var instance = GetService(componentType);
        if (instance is IComponent component) return component;

        if (!_componentFactoryCache.TryGetValue(componentType, out var factory))
        {
            factory = CreateComponentFactory(componentType);
            _componentFactoryCache.TryAdd(componentType, factory);
        }

        return (IComponent)factory(this, []);
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [GeneratedCode("SourceGen.Ioc", "{Version}")]
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

namespace MyApp;

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
            activator = _serviceResolvers.ContainsKey(new ServiceIdentifier(componentType, ServiceIdentifier.NonKeyed))
                ? static (_, _) => { }
                : CreateComponentPropertyInjector(componentType);
            _propertyActivatorCache.TryAdd(componentType, activator);
        }
        return activator;
    }

    private static Action<IServiceProvider, IComponent> CreateComponentPropertyInjector(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type componentType)
    {
        // Walks the type hierarchy; for each property declared on the visited type:
        object? key;
        if (property.GetCustomAttribute<InjectAttribute>(false) is { } injectAttr)
        {
            key = injectAttr.Key;
        }
        else if (property.GetCustomAttribute<IocInjectAttribute>(false) is { } iocInjectAttr)
        {
            if (iocInjectAttr.KeyType is KeyType.Csharp) continue; // cannot evaluate a C# expression at runtime
            key = iocInjectAttr.Key;
        }
        else
        {
            continue;
        }
        // ... collects (property, key) and returns a delegate that resolves keyed (Key not null)
        // or non-keyed services and sets each property
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [GeneratedCode("SourceGen.Ioc", "{Version}")]
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

3. **Reflection Fallback**: For unregistered components (not in `_serviceResolvers`), or when only `IComponentPropertyActivator` is implemented without `IComponentActivator`, the generator produces a reflection-based fallback that walks the type hierarchy for properties with an inject attribute (see note 4) and injects them at runtime. This mirrors `DefaultComponentPropertyActivator` from ASP.NET Core.

4. **Inject Attribute Matching**: The reflection fallback MUST match inject attributes by type, never by name, and MUST read `Key` directly from the typed attribute (no reflection on the attribute type), so the generated code raises no trim or Native AOT warnings and keyed injection survives trimming:

   |Attribute on the property|Behavior|
   |:---|:---|
   |`Microsoft.AspNetCore.Components.InjectAttribute`|Injected with `InjectAttribute.Key` (keyed when not `null`); takes precedence when both attributes are present|
   |`SourceGen.Ioc.IocInjectAttribute` with `KeyType.Value`|Injected with `IocInjectAttribute.Key` (keyed when not `null`)|
   |`SourceGen.Ioc.IocInjectAttribute` with `KeyType.Csharp`|Skipped: `Key` holds a C# expression that the runtime cannot evaluate|
   |Any other attribute, including one named `InjectAttribute` in another namespace|Ignored|

   Attributes are read with `inherit: false`; the hierarchy walk visits each declaring type, so a property is matched by the attributes of its own declaration. `[IocInject]` is visible at runtime only when the `SOURCEGEN` constant is defined (its `[Conditional("SOURCEGEN")]` is satisfied), which the `SourceGen.Ioc` package does by default. `InjectAttribute.Key` exists in every `Microsoft.AspNetCore.Components` version that exposes `IComponentPropertyActivator` (v11+).

5. **Important Note**: When implementing only `IComponentPropertyActivator` without `IComponentActivator`, the no-op optimization is NOT applied because there's no guarantee the component's constructor injection went through the container. All components use the reflection fallback in this case.

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

- [Basic Container Generation](Container.Basic.spec.md)
- [IServiceProviderFactory](Container.Options.spec.md)
