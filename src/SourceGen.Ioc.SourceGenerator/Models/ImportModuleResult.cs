namespace SourceGen.Ioc.SourceGenerator.Models;

/// <summary>
/// Represents the result of transforming an IocImportModuleAttribute.
/// Contains both the default settings from the imported module and open generic entries for cross-assembly discovery.
/// </summary>
/// <param name="DefaultSettings">The default settings models extracted from the imported module.</param>
/// <param name="OpenGenericEntries">Open generic entries for cross-assembly open generic discovery.</param>
internal readonly record struct ImportModuleResult(
    ImmutableEquatableArray<DefaultSettingsModel> DefaultSettings,
    ImmutableEquatableArray<OpenGenericEntry> OpenGenericEntries);
