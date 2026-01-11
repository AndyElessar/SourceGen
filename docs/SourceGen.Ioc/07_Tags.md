# Tags

Tags allow generating multiple registration methods for different scenarios.

## Basic Tags

```csharp
[IoCRegisterDefaults<IHandler>(ServiceLifetime.Transient, Tags = ["Mediator"])]
public interface IHandler;

[IoCRegister]
internal class MyHandler : IHandler;
```

This generates:

```csharp
// Generated
public static IServiceCollection AddMyProject(this IServiceCollection services)
{
    // Default registrations (includes MyHandler)
    services.AddTransient<global::MyNamespace.MyHandler, global::MyNamespace.MyHandler>();
    services.AddTransient<global::MyNamespace.IHandler>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.MyHandler>());
    return services;
}

public static IServiceCollection AddMyProject_Mediator(this IServiceCollection services)
{
    // Mediator-tagged registrations (also includes MyHandler)
    services.AddTransient<global::MyNamespace.MyHandler, global::MyNamespace.MyHandler>();
    services.AddTransient<global::MyNamespace.IHandler>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.MyHandler>());
    return services;
}
```

## Multiple Tags

```csharp
[IoCRegister<IService>(Tags = ["Feature1", "Feature2"])]
internal class MyService : IService;
```

Generates registration in both `AddMyProject_Feature1` and `AddMyProject_Feature2`, as well as the default `AddMyProject`.

## Exclude From Default

Use `ExcludeFromDefault = true` to exclude a registration from the default extension method. This is useful when you want certain services to only be registered via specific tag methods.

```csharp
[IoCRegister<IService>(Tags = ["Feature1"], ExcludeFromDefault = true)]
internal class FeatureOnlyService : IService;

[IoCRegister<IService>]
internal class DefaultService : IService;
```

This generates:

```csharp
// Generated
public static IServiceCollection AddMyProject(this IServiceCollection services)
{
    // Only DefaultService is registered here
    services.AddSingleton<global::MyNamespace.DefaultService, global::MyNamespace.DefaultService>();
    services.AddSingleton<global::MyNamespace.IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.DefaultService>());
    return services;
}

public static IServiceCollection AddMyProject_Feature1(this IServiceCollection services)
{
    // FeatureOnlyService is only registered in the tag method
    services.AddSingleton<global::MyNamespace.FeatureOnlyService, global::MyNamespace.FeatureOnlyService>();
    services.AddSingleton<global::MyNamespace.IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.FeatureOnlyService>());
    return services;
}
```

> **Note:** `ExcludeFromDefault` defaults to `false`, meaning registrations are included in both the default method and any tag methods.

## Using Tags

```csharp
// Register only core services
services.AddMyProject();

// Register feature-specific services
services.AddMyProject_Mediator();
services.AddMyProject_Feature1();
```

## Tags with Defaults

Apply tags and exclusion settings to all implementations via `IoCRegisterDefaults`:

```csharp
[IoCRegisterDefaults<IMediator>(
    ServiceLifetime.Singleton,
    Tags = ["Mediator"],
    ExcludeFromDefault = true)]  // All IMediator implementations excluded from AddMyProject()
public interface IMediator;

[IoCRegister]
internal class Mediator : IMediator;  // Only in AddMyProject_Mediator()
```

---

[← Back to Overview](01_Overview.md)