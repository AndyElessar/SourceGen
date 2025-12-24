namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents default settings for service registration.
/// </summary>
/// <param name="TargetServiceType">The target service type that this default applies to.</param>
/// <param name="Lifetime">The default service lifetime.</param>
/// <param name="RegisterAllInterfaces">Whether to register all interfaces by default.</param>
/// <param name="RegisterAllBaseClasses">Whether to register all base classes by default.</param>
/// <param name="ServiceTypes">Additional service types to register by default.</param>
internal sealed record class DefaultSettingsModel(
    TypeData TargetServiceType,
    ServiceLifetime Lifetime,
    bool RegisterAllInterfaces,
    bool RegisterAllBaseClasses,
    ImmutableEquatableArray<TypeData> ServiceTypes);
