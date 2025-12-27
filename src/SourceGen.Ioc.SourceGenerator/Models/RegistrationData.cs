namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents parsed registration data from IoCRegisterAttribute or IoCRegisterForAttribute.
/// </summary>
/// <param name="ImplementationType">The implementation type data including hierarchy information.</param>
/// <param name="Lifetime">The service lifetime (-1 means use default).</param>
/// <param name="RegisterAllInterfaces">Whether to register all interfaces.</param>
/// <param name="RegisterAllBaseClasses">Whether to register all base classes.</param>
/// <param name="ServiceTypes">Explicit service types to register.</param>
/// <param name="Key">The key for keyed registration.</param>
/// <param name="KeyType">How to interpret the key.</param>
/// <param name="HasExplicitLifetime">Whether the lifetime was explicitly set.</param>
/// <param name="HasExplicitRegisterAllInterfaces">Whether RegisterAllInterfaces was explicitly set.</param>
/// <param name="HasExplicitRegisterAllBaseClasses">Whether RegisterAllBaseClasses was explicitly set.</param>
/// <param name="ValidOpenGenericServiceTypes">Set of valid open generic service type names (without generic parameters) that can be properly registered.</param>
/// <param name="Decorators">The decorator types to apply, in order from outermost to innermost.</param>
internal sealed record class RegistrationData(
    TypeData ImplementationType,
    ServiceLifetime Lifetime,
    bool RegisterAllInterfaces,
    bool RegisterAllBaseClasses,
    ImmutableEquatableArray<TypeData> ServiceTypes,
    string? Key,
    int KeyType,
    bool HasExplicitLifetime,
    bool HasExplicitRegisterAllInterfaces,
    bool HasExplicitRegisterAllBaseClasses,
    ImmutableEquatableSet<string> ValidOpenGenericServiceTypes,
    ImmutableEquatableArray<TypeData> Decorators)
{
    /// <summary>
    /// Gets all interfaces implemented by the type. Shortcut for ImplementationType.AllInterfaces.
    /// </summary>
    public ImmutableEquatableArray<TypeData> AllInterfaces => ImplementationType.AllInterfaces ?? [];

    /// <summary>
    /// Gets all base classes of the type. Shortcut for ImplementationType.AllBaseClasses.
    /// </summary>
    public ImmutableEquatableArray<TypeData> AllBaseClasses => ImplementationType.AllBaseClasses ?? [];
}
