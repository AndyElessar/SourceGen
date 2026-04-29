namespace SourceGen.Ioc;

// Helpers that read MSBuild / AnalyzerConfig / Compilation inputs into pipeline-friendly providers.
// Extracted from Initialize() to keep the orchestrator focused on pipeline wiring.
partial class IocSourceGenerator
{
    /// <summary>
    /// Reads the SourceGenIocDefaultLifetime MSBuild property and parses it into a <see cref="ServiceLifetime"/>.
    /// Returns <c>null</c> when the property is missing or unrecognised.
    /// </summary>
    private static IncrementalValueProvider<ServiceLifetime?> BuildDefaultLifetimeProvider(
        IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider
            .Select(static (configOptions, _) =>
            {
                if(!configOptions.GlobalOptions.TryGetValue(Constants.SourceGenIocDefaultLifetimeProperty, out var lifetimeStr)
                    || string.IsNullOrWhiteSpace(lifetimeStr))
                {
                    return (ServiceLifetime?)null;
                }

                var trimmed = lifetimeStr.Trim();
                return (ServiceLifetime?)(trimmed switch
                {
                    _ when trimmed.Equals("singleton", StringComparison.OrdinalIgnoreCase) => ServiceLifetime.Singleton,
                    _ when trimmed.Equals("scoped", StringComparison.OrdinalIgnoreCase) => ServiceLifetime.Scoped,
                    _ when trimmed.Equals("transient", StringComparison.OrdinalIgnoreCase) => ServiceLifetime.Transient,
                    _ => null,
                });
            });

    /// <summary>
    /// Reads MSBuild properties (RootNamespace, SourceGenIocName, SourceGenIocFeatures) into a <see cref="MsBuildProperties"/> record.
    /// </summary>
    private static IncrementalValueProvider<MsBuildProperties> BuildMsBuildPropertiesProvider(
        IncrementalGeneratorInitializationContext context)
        => context.AnalyzerConfigOptionsProvider
            .Select(static (configOptions, _) =>
            {
                var rootNamespace = TryReadNonEmptyProperty(configOptions, Constants.RootNamespaceProperty);
                var customIocName = TryReadNonEmptyProperty(configOptions, Constants.SourceGenIocNameProperty);

                configOptions.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var featuresStr);
                var features = IocFeaturesHelper.Parse(featuresStr);

                return new MsBuildProperties(rootNamespace, customIocName, features);
            });

    private static string? TryReadNonEmptyProperty(
        Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider configOptions,
        string propertyName)
    {
        if(configOptions.GlobalOptions.TryGetValue(propertyName, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// Reads compilation-level inputs: assembly name (defaults to <c>"Generated"</c>) and whether
    /// Microsoft.Extensions.DependencyInjection is referenced (detected via <c>ServiceCollectionContainerBuilderExtensions</c>).
    /// </summary>
    private static IncrementalValueProvider<(string AssemblyName, bool HasDIPackage)> BuildCompilationInfoProvider(
        IncrementalGeneratorInitializationContext context)
        => context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var assemblyName = compilation.AssemblyName ?? "Generated";
                var hasDIPackage = compilation.GetTypeByMetadataName(
                    "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions") is not null;
                return (AssemblyName: assemblyName, HasDIPackage: hasDIPackage);
            });

}
