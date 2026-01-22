namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents a container class marked with [IocContainer].
/// </summary>
/// <param name="ContainerTypeName">Fully qualified type name of the container class.</param>
/// <param name="ContainerNamespace">Namespace of the container class.</param>
/// <param name="ClassName">Simple class name without namespace.</param>
/// <param name="ResolveIServiceCollection">Whether to support external IServiceProvider fallback.</param>
/// <param name="ExplicitOnly">Whether to only include explicitly marked registrations.</param>
/// <param name="IncludeTags">Tags to filter services by. When non-empty, only services with matching tags are included.</param>
/// <param name="UseSwitchStatement">Whether to use switch statement instead of FrozenDictionary.</param>
/// <param name="ImportedModules">Types of imported module containers.</param>
/// <param name="ExplicitRegistrations">Registrations explicitly marked on the container class (for ExplicitOnly mode).</param>
internal sealed record class ContainerModel(
    string ContainerTypeName,
    string ContainerNamespace,
    string ClassName,
    bool ResolveIServiceCollection,
    bool ExplicitOnly,
    ImmutableEquatableArray<string> IncludeTags,
    bool UseSwitchStatement,
    ImmutableEquatableArray<TypeData> ImportedModules,
    ImmutableEquatableArray<RegistrationData> ExplicitRegistrations);
