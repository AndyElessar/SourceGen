namespace SourceGen.Ioc.SourceGenerator.Register;

/// <summary>
/// Generates code to register types marked with SourceGen.Ioc.IocRegisterAttribute/SourceGen.Ioc.IocRegisterForAttribute
/// in Microsoft.Extensions.DependencyInjection container.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class RegisterSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ========== IocRegisterAttribute providers ==========
        // IocRegisterAttribute (non-generic)
        var registerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegister(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IocRegisterAttribute<T>
        var registerProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IocRegisterAttribute<T1, T2>
        var registerProvider_T2 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName_T2,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IocRegisterAttribute<T1, T2, T3>
        var registerProvider_T3 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName_T3,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // IocRegisterAttribute<T1, T2, T3, T4>
        var registerProvider_T4 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName_T4,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // ========== IocRegisterForAttribute providers ==========
        // IocRegisterForAttribute (non-generic)
        var registerForProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterForAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterFor(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IocRegisterForAttribute<T>
        var registerForProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterForAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterForGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // ========== IocRegisterDefaultsAttribute providers ==========
        // IocRegisterDefaultsAttribute (non-generic)
        var defaultSettingsProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterDefaultsAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettings(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IocRegisterDefaultsAttribute<T>
        var defaultSettingsProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterDefaultsAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettingsGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // Combine all default settings providers
        var allDefaultSettings = defaultSettingsProvider
            .Collect()
            .Combine(defaultSettingsProvider_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // ========== IocImportModuleAttribute providers ==========
        // IocImportModuleAttribute (non-generic)
        var importedDefaultSettingsProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocImportModuleAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformImportModule(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IocImportModuleAttribute<T>
        var importedDefaultSettingsProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocImportModuleAttributeFullName_T1,
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

        // ========== IocDiscoverAttribute providers ==========
        // IocDiscoverAttribute (non-generic)
        var discoverProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocDiscoverAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDiscover(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IocDiscoverAttribute<T>
        var discoverProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocDiscoverAttributeFullName_T1,
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
