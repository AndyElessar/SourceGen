namespace SourceGen.Ioc.SourceGenerator.Register;

/// <summary>
/// Generates code to register types marked with SourceGen.Ioc.IoCRegisterAttribute/SourceGen.Ioc.IoCRegisterForAttribute
/// in Microsoft.Extensions.DependencyInjection container.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class RegisterSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ========== IoCRegisterAttribute providers ==========
        // IoCRegisterAttribute (non-generic)
        var registerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegister(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IoCRegisterAttribute<T>
        var registerProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IoCRegisterAttribute<T1, T2>
        var registerProvider_T2 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterAttributeFullName_T2,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IoCRegisterAttribute<T1, T2, T3>
        var registerProvider_T3 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterAttributeFullName_T3,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IoCRegisterAttribute<T1, T2, T3, T4>
        var registerProvider_T4 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterAttributeFullName_T4,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // ========== IoCRegisterForAttribute providers ==========
        // IoCRegisterForAttribute (non-generic)
        var registerForProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterForAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterFor(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IoCRegisterForAttribute<T>
        var registerForProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterForAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterForGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // ========== IoCRegisterDefaultsAttribute providers ==========
        // IoCRegisterDefaultsAttribute (non-generic)
        var defaultSettingsProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterDefaultsAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettings(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IoCRegisterDefaultsAttribute<T>
        var defaultSettingsProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IoCRegisterDefaultsAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettingsGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // Combine all default settings providers
        var allDefaultSettings = defaultSettingsProvider
            .Collect()
            .Combine(defaultSettingsProvider_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // ========== ImportModuleAttribute providers ==========
        // ImportModuleAttribute (non-generic)
        var importedDefaultSettingsProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.ImportModuleAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformImportModule(ctx, ct))
            .SelectMany(static (m, _) => m);

        // ImportModuleAttribute<T>
        var importedDefaultSettingsProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.ImportModuleAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformImportModuleGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // Combine all imported default settings providers
        var allImportedDefaultSettings = importedDefaultSettingsProvider
            .Collect()
            .Combine(importedDefaultSettingsProvider_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // Combine default settings from current assembly and imported modules
        // Current assembly settings take precedence over imported settings
        var combinedDefaultSettings = allDefaultSettings
            .Combine(allImportedDefaultSettings)
            .Select(static (combined, _) =>
            {
                var (currentAssembly, imported) = combined;
                // Current assembly settings come first (higher priority), then imported settings (lower priority)
                // DefaultSettingsMap uses first-match semantics, so current assembly settings should be added first
                var allSettings = currentAssembly.AddRange(imported);
                return new DefaultSettingsMap(allSettings);
            });

        // Collect GetService, GetRequiredService, GetKeyedService, GetRequiredKeyedService, GetServices invocations
        var invocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => PredicateInvocations(node),
                transform: TransformInvocations)
            .SelectMany(static (candidates, _) => candidates)
            .Collect();

        // ========== DiscoverAttribute providers ==========
        // DiscoverAttribute (non-generic)
        var discoverProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.DiscoverAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDiscover(ctx, ct))
            .SelectMany(static (m, _) => m);

        // DiscoverAttribute<T>
        var discoverProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.DiscoverAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDiscoverGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // Combine all discover providers
        var allDiscoverProviders = discoverProvider
            .Collect()
            .Combine(discoverProvider_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // Get assembly name from compilation
        var assemblyNameProvider = context.CompilationProvider
            .Select(static (compilation, _) => compilation.AssemblyName ?? "Generated");

        // Get MSBuild properties from analyzer config options
        var msbuildPropertiesProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (configOptions, _) =>
            {
                // Try to get RootNamespace from MSBuild property
                string? rootNamespace = null;
                if(configOptions.GlobalOptions.TryGetValue(Constants.RootNamespaceProperty, out var ns)
                    && !string.IsNullOrWhiteSpace(ns))
                {
                    rootNamespace = ns;
                }

                // Try to get custom IoC name from MSBuild property
                string? customIocName = null;
                if(configOptions.GlobalOptions.TryGetValue(Constants.SourceGenIocNameProperty, out var iocName)
                    && !string.IsNullOrWhiteSpace(iocName))
                {
                    customIocName = iocName;
                }

                return (RootNamespace: rootNamespace, CustomIocName: customIocName);
            });

        // ========== Pipeline 1: Process individual registrations (cacheable per registration) ==========
        // Each registration is processed independently with default settings.

        var basicRegistrationResults1 = registerProvider
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults1_T1 = registerProvider_T1
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults1_T2 = registerProvider_T2
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults1_T3 = registerProvider_T3
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults1_T4 = registerProvider_T4
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults2 = registerForProvider
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults2_T1 = registerForProvider_T1
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        // Collect all basic registration results
        var allBasicResults = basicRegistrationResults1
            .Collect()
            .Combine(basicRegistrationResults1_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right))
            .Combine(basicRegistrationResults1_T2.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right))
            .Combine(basicRegistrationResults1_T3.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right))
            .Combine(basicRegistrationResults1_T4.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right))
            .Combine(basicRegistrationResults2.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right))
            .Combine(basicRegistrationResults2_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // ========== Pipeline 2: Combine results and resolve closed generics ==========

        // Combine invocations with discover attributes
        var combinedClosedGenericDependencies = invocations
            .Combine(allDiscoverProviders)
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        var serviceRegistrations = allBasicResults
            .Combine(combinedClosedGenericDependencies)
            .Select(static (source, ct) => CombineAndResolveClosedGenerics(in source.Left, in source.Right, ct));

        // Combine service registrations with assembly name and MSBuild properties
        var combined = serviceRegistrations
            .Combine(assemblyNameProvider)
            .Combine(msbuildPropertiesProvider);

        // Generate output
        context.RegisterSourceOutput(combined, static (ctx, source) =>
        {
            var ((registrations, assemblyName), msbuildProps) = source;
            // Use RootNamespace from MSBuild if available, otherwise fall back to assembly name
            var rootNamespace = msbuildProps.RootNamespace ?? assemblyName;
            GenerateOutput(in ctx, registrations, rootNamespace, assemblyName, msbuildProps.CustomIocName);
        });
    }
}
