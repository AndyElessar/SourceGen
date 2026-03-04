# Tag-Based Registration

## Overview

Tag-based registration enables conditional registration of services based on tags. Services can be registered with specific tags, and the registration method can accept a `params IEnumerable<string> tags` parameter to select which tagged services to register.

## Feature: Tag-Based Registration

When Tags is not empty, generate a single extension method with `params IEnumerable<string> tags` parameter to handle tag-based registration:

### Registration Logic (Mutually Exclusive Model)

- Services **without** Tags: Only registered when no tags are passed (`!tags.Any()`)
- Services **with** Tags: Only registered when passed tags match (at least one tag matches)
- Tags act as mutually exclusive group selectors - passing tags switches from default to tagged services

### Generated Code Pattern

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
```

### Usage

```csharp
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

## See Also

- [Basic Registration](Register.Basic.md)
- [Container IncludeTags](Container.Options.md)
