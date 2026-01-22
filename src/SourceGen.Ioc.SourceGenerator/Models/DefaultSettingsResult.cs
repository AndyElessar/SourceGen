namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents the result of transforming an IoCRegisterDefaultsAttribute.
/// Contains both the default settings model and the registration data for implementation types.
/// </summary>
/// <param name="DefaultSettings">The default settings model extracted from the attribute.</param>
/// <param name="ImplementationTypeRegistrations">Registration data for types specified in ImplementationTypes property.</param>
/// <param name="OpenGenericEntries">Open generic entries for factory-based registrations without explicit implementation types.</param>
internal readonly record struct DefaultSettingsResult(
    DefaultSettingsModel? DefaultSettings,
    ImmutableEquatableArray<RegistrationData> ImplementationTypeRegistrations,
    ImmutableEquatableArray<OpenGenericEntry> OpenGenericEntries);
