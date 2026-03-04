# MSBuild Properties

## Overview

MSBuild properties provide compile-time customization of the generated registration extension methods. Control the namespace, method naming, lifetime defaults, and feature flags.

## Feature: MSBuild Properties

MSBuild properties for customizing generated code:

### RootNamespace

Controls the namespace of generated extension class. If not set, falls back to assembly name.

### SourceGenIocName

Controls the method name suffix. If not set, uses assembly name.

### SourceGenIocDefaultLifetime

Controls the default lifetime for registrations when not explicitly specified. If not set, uses `Transient`.

### SourceGenIocFeatures

Controls which outputs and injection member kinds are generated. See [Feature Flags](SPEC.md#7-feature-flags) for details.

### Configuration Example

.csproj:

```xml
<PropertyGroup>
    <RootNamespace>MyCompany.MyProject</RootNamespace>
    <SourceGenIocName>CustomName</SourceGenIocName>
    <SourceGenIocDefaultLifetime>Scoped</SourceGenIocDefaultLifetime>
    <SourceGenIocFeatures>Register,Container,PropertyInject,MethodInject</SourceGenIocFeatures>
</PropertyGroup>
<ItemGroup>
    <CompilerVisibleProperty Include="RootNamespace" />
    <CompilerVisibleProperty Include="SourceGenIocName" />
    <CompilerVisibleProperty Include="SourceGenIocDefaultLifetime" />
    <CompilerVisibleProperty Include="SourceGenIocFeatures" />
</ItemGroup>
```

### Generated Output

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

## See Also

- [Feature Flags](SPEC.md#7-feature-flags)
- [Compilation Info](SPEC.md#6-compilation-info)
