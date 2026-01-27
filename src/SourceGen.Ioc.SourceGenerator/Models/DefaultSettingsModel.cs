namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents default settings for service registration.
/// </summary>
/// <param name="TargetServiceType">The target service type that this default applies to.</param>
/// <param name="Lifetime">The default service lifetime.</param>
/// <param name="RegisterAllInterfaces">Whether to register all interfaces by default.</param>
/// <param name="RegisterAllBaseClasses">Whether to register all base classes by default.</param>
/// <param name="ServiceTypes">Additional service types to register by default.</param>
/// <param name="Decorators">The decorator types to apply by default.</param>
/// <param name="Tags">The collection of tags associated with this default setting.</param>
/// <param name="Factory">The factory method data to use for creating instances, or null if not specified.</param>
/// <param name="ImplementationTypes">Implementation types to directly register with these default settings.</param>
internal sealed record class DefaultSettingsModel(
    TypeData TargetServiceType,
    ServiceLifetime Lifetime,
    bool RegisterAllInterfaces,
    bool RegisterAllBaseClasses,
    ImmutableEquatableArray<TypeData> ServiceTypes,
    ImmutableEquatableArray<TypeData> Decorators,
    ImmutableEquatableArray<string> Tags,
    FactoryMethodData? Factory,
    ImmutableEquatableArray<TypeData> ImplementationTypes);
