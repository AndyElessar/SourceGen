# Tags

Tags allow filtering service registrations at runtime by passing tag parameters to the extension method.

## Basic Tags

```csharp
[IocRegisterDefaults<IHandler>(ServiceLifetime.Transient, Tags = ["Mediator"])]
public interface IHandler;

[IocRegister]
internal class MyHandler : IHandler;
```

This generates a single method with a `params IEnumerable<string> tags` parameter:

```csharp
// Generated
public static IServiceCollection AddMyProject(this IServiceCollection services, params IEnumerable<string> tags)
{
    // Services with tags: only register when matching tags are passed
    if (tags.Contains("Mediator"))
    {
        services.AddTransient<global::MyNamespace.MyHandler, global::MyNamespace.MyHandler>();
        services.AddTransient<global::MyNamespace.IHandler>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.MyHandler>());
    }

    return services;
}
```

## Behavior (Mutually Exclusive Model)

- **Services without tags**: Only registered when **no tags are passed**
- **Services with tags**: Only registered when matching tags are passed

Tags act as mutually exclusive group selectors. This enables scenarios like environment-specific configurations where you switch between different service implementations rather than adding to them.

## Multiple Tags

```csharp
[IocRegister<IService>(Tags = ["Feature1", "Feature2"])]
internal class MyService : IService;
```

The service is registered when any of the specified tags match:

```csharp
if (tags.Contains("Feature1") || tags.Contains("Feature2"))
{
    // Register MyService
}
```

## Mixed Services

```csharp
[IocRegister<IService>(Tags = ["Feature1"])]
internal class FeatureService : IService;

[IocRegister<IService>]
internal class DefaultService : IService;
```

This generates:

```csharp
// Generated
public static IServiceCollection AddMyProject(this IServiceCollection services, params IEnumerable<string> tags)
{
    // Services without tags - only register when no tags passed
    if (!tags.Any())
    {
        services.AddSingleton<global::MyNamespace.DefaultService, global::MyNamespace.DefaultService>();
        services.AddSingleton<global::MyNamespace.IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.DefaultService>());
    }

    // Services with tags - only register when tags match
    if (tags.Contains("Feature1"))
    {
        services.AddSingleton<global::MyNamespace.FeatureService, global::MyNamespace.FeatureService>();
        services.AddSingleton<global::MyNamespace.IService>((global::System.IServiceProvider sp) => sp.GetRequiredService<global::MyNamespace.FeatureService>());
    }

    return services;
}
```

## Using Tags

```csharp
// Register only services without tags (default configuration)
services.AddMyProject();

// Register only services matching "Mediator" tag (NOT services without tags)
services.AddMyProject("Mediator");

// Register only services matching "Feature1" or "Feature2" tags (NOT services without tags)
services.AddMyProject("Feature1", "Feature2");

// Using an array of tags
string[] myTags = ["Mediator", "Feature1"];
services.AddMyProject(myTags);
```

## Tags with Defaults

Apply tags to all implementations via `IocRegisterDefaults`:

```csharp
[IocRegisterDefaults<IMediator>(
    ServiceLifetime.Singleton,
    Tags = ["Mediator"])]  // All IMediator implementations only register when "Mediator" tag is passed
public interface IMediator;

[IoCRegister]
internal class Mediator : IMediator;  // Only registered with AddMyProject("Mediator")
```

---

[← Back to Overview](01_Overview.md)
