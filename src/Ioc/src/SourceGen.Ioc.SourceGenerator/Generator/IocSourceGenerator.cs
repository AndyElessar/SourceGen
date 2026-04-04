namespace SourceGen.Ioc;

/// <summary>
/// Generates code to register types marked with SourceGen.Ioc.IocRegisterAttribute/SourceGen.Ioc.IocRegisterForAttribute
/// in Microsoft.Extensions.DependencyInjection container.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class IocSourceGenerator : IIncrementalGenerator
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
        // Transform IocRegisterDefaultsAttribute to get both DefaultSettings and ImplementationType registrations
        // IocRegisterDefaultsAttribute (non-generic)
        var defaultSettingsResultProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterDefaultsAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettings(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IocRegisterDefaultsAttribute<T>
        var defaultSettingsResultProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterDefaultsAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettingsGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // Combine all default settings result providers
        var allDefaultSettingsResults = defaultSettingsResultProvider
            .Collect()
            .Combine(defaultSettingsResultProvider_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // Pipeline 1: Extract DefaultSettingsModel from results (for default settings map)
        var allDefaultSettings = allDefaultSettingsResults
            .SelectMany(static (results, _) => results
                .Where(static r => r.DefaultSettings is not null)
                .Select(static r => r.DefaultSettings!))
            .Collect();

        // Pipeline 2: Extract RegistrationData from results (for implementation type registrations)
        var defaultSettingsImplTypeRegistrations = allDefaultSettingsResults
            .SelectMany(static (results, _) => results
                .SelectMany(static r => r.ImplementationTypeRegistrations));

        // Pipeline 3: Extract OpenGenericEntries from results (for factory-based open generic registrations)
        var factoryBasedOpenGenericEntries = allDefaultSettingsResults
            .SelectMany(static (results, _) => results
                .SelectMany(static r => r.OpenGenericEntries))
            .Collect();

        // ========== IocImportModuleAttribute providers ==========
        // Transform IocImportModuleAttribute to get both DefaultSettings and OpenGenericEntries in a single pass
        // IocImportModuleAttribute (non-generic)
        var importModuleResultProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocImportModuleAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformImportModule(ctx, ct))
            .SelectMany(static (m, _) => m);

        // IocImportModuleAttribute<T>
        var importModuleResultProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocImportModuleAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformImportModuleGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // Combine all import module result providers
        var allImportModuleResults = importModuleResultProvider
            .Collect()
            .Combine(importModuleResultProvider_T1.Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // Pipeline 1: Extract DefaultSettingsModel from ImportModuleResult (for imported default settings)
        var allImportedDefaultSettings = allImportModuleResults
            .SelectMany(static (results, _) => results
                .SelectMany(static r => r.DefaultSettings))
            .Collect();

        // Pipeline 2: Extract OpenGenericEntries from ImportModuleResult (for cross-assembly open generic discovery)
        var allImportedOpenGenerics = allImportModuleResults
            .SelectMany(static (results, _) => results
                .SelectMany(static r => r.OpenGenericEntries))
            .Collect();

        // Get MSBuild properties from analyzer config options
        var msbuildPropertiesProvider = context.AnalyzerConfigOptionsProvider
            .Select(static (configOptions, _) =>
            {
                // Try to get RootNamespace from MSBuild property
                string? rootNamespace = null;
                if(configOptions.GlobalOptions.TryGetValue(Constants.RootNamespaceProperty, out var ns)
                    && ns is { Length: > 0 } rawRootNamespace
                    && !string.IsNullOrWhiteSpace(rawRootNamespace))
                {
                    rootNamespace = rawRootNamespace;
                }

                // Try to get custom IoC name from MSBuild property
                string? customIocName = null;
                if(configOptions.GlobalOptions.TryGetValue(Constants.SourceGenIocNameProperty, out var iocName)
                    && iocName is { Length: > 0 } rawCustomIocName
                    && !string.IsNullOrWhiteSpace(rawCustomIocName))
                {
                    customIocName = rawCustomIocName;
                }

                // Try to get default lifetime from MSBuild property
                ServiceLifetime? defaultLifetime = null;
                if(configOptions.GlobalOptions.TryGetValue(Constants.SourceGenIocDefaultLifetimeProperty, out var lifetimeStr)
                    && lifetimeStr is { Length: > 0 } rawLifetime
                    && !string.IsNullOrWhiteSpace(rawLifetime))
                {
                    var trimmed = rawLifetime.Trim();
                    defaultLifetime = trimmed switch
                    {
                        _ when trimmed.Equals("singleton", StringComparison.OrdinalIgnoreCase) => ServiceLifetime.Singleton,
                        _ when trimmed.Equals("scoped", StringComparison.OrdinalIgnoreCase) => ServiceLifetime.Scoped,
                        _ when trimmed.Equals("transient", StringComparison.OrdinalIgnoreCase) => ServiceLifetime.Transient,
                        _ => null
                    };
                }

                // Try to get enabled feature flags from MSBuild property
                configOptions.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var featuresStr);
                var features = IocFeaturesHelper.Parse(featuresStr);

                return new MsBuildProperties(rootNamespace, customIocName, defaultLifetime, features);
            });

        // Get compilation info (assembly name and DI package reference)
        var compilationInfoProvider = context.CompilationProvider
            .Select(static (compilation, _) =>
            {
                var assemblyName = compilation.AssemblyName ?? "Generated";
                // Detect if Microsoft.Extensions.DependencyInjection package is referenced
                // by checking for ServiceCollectionContainerBuilderExtensions type
                var hasDIPackage = compilation.GetTypeByMetadataName(
                    "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions") is not null;
                return (AssemblyName: assemblyName, HasDIPackage: hasDIPackage);
            });

        // Combine default settings from current assembly and imported modules
        // Current assembly settings take precedence over imported settings
        var combinedDefaultSettings = allDefaultSettings
            .Combine(allImportedDefaultSettings)
            .Combine(msbuildPropertiesProvider)
            .Select(static (combined, _) =>
            {
                var ((currentAssembly, imported), msbuildProps) = combined;
                // Current assembly settings come first (higher priority), then imported settings (lower priority)
                // DefaultSettingsMap uses first-match semantics, so current assembly settings should be added first
                var allSettings = currentAssembly.AddRange(imported);
                return new DefaultSettingsMap(allSettings, msbuildProps.DefaultLifetime ?? ServiceLifetime.Transient);
            });

        // Collect GetService, GetRequiredService, GetKeyedService, GetRequiredKeyedService, GetServices invocations
        var invocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => PredicateInvocations(node),
                transform: static (ctx, ct) => TransformInvocations(ctx, ct))
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

        // ========== Pipeline 1: Process individual registrations (cacheable per registration) ==========
        // Each registration is processed independently with default settings.

        var basicRegistrationResults1 = registerProvider
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults1_T1 = registerProvider_T1
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults2 = registerForProvider
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        var basicRegistrationResults2_T1 = registerForProvider_T1
            .Combine(combinedDefaultSettings)
            .Select(static (source, _) => ProcessSingleRegistration(source.Left, source.Right));

        // Process ImplementationTypes from IocRegisterDefaultsAttribute
        // These registrations already have all settings applied from the defaults attribute
        // Transform each RegistrationData to BasicRegistrationResult
        var basicRegistrationResults3 = defaultSettingsImplTypeRegistrations
            .Select(static (registrations, _) => ProcessSingleRegistrationFromDefaults(registrations));

        // Collect all basic registration results
        var allBasicResults = basicRegistrationResults1.Collect()
            .Combine(basicRegistrationResults1_T1.Collect())
            .Combine(basicRegistrationResults2.Collect())
            .Combine(basicRegistrationResults2_T1.Collect())
            .Combine(basicRegistrationResults3.Collect())
            .Select(static (combined, _) =>
            {
                var part1 = combined.Left.Left.Left.Left;
                var part2 = combined.Left.Left.Left.Right;
                var part3 = combined.Left.Left.Right;
                var part4 = combined.Left.Right;
                var part5 = combined.Right;

                var builder = ImmutableArray.CreateBuilder<BasicRegistrationResult>(
                    part1.Length + part2.Length + part3.Length + part4.Length + part5.Length);

                builder.AddRange(part1);
                builder.AddRange(part2);
                builder.AddRange(part3);
                builder.AddRange(part4);
                builder.AddRange(part5);

                return builder.MoveToImmutable();
            });

        // ========== Pipeline 2: Combine results and resolve closed generics ==========

        // Combine invocations with discover attributes
        var combinedClosedGenericDependencies = invocations
            .Combine(allDiscoverProviders)
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // Combine factory-based open generics with imported open generics from other assemblies
        var allOpenGenericEntries = factoryBasedOpenGenericEntries
            .Combine(allImportedOpenGenerics)
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        var serviceRegistrations = allBasicResults
            .Combine(combinedClosedGenericDependencies)
            .Combine(allOpenGenericEntries)
            .Select(static (source, ct) => CombineAndResolveClosedGenerics(in source.Left.Left, in source.Left.Right, in source.Right, ct));

        var registerOutputModel = serviceRegistrations
            .Combine(compilationInfoProvider)
            .Combine(msbuildPropertiesProvider)
            .Select(static (source, _) =>
            {
                var ((registrations, compilationInfo), msbuildProps) = source;
                var rootNamespace = msbuildProps.RootNamespace ?? compilationInfo.AssemblyName;
                return GroupRegistrationsForRegister(
                    registrations, rootNamespace, compilationInfo.AssemblyName,
                    msbuildProps.CustomIocName, msbuildProps.Features);
            });

        context.RegisterSourceOutput(registerOutputModel, static (ctx, model) =>
        {
            if(model is null)
                return;

            GenerateRegisterOutput(in ctx, model);
        });

        // ========== Container Pipeline ==========
        // IocContainerAttribute provider
        var containerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocContainerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => TransformContainer(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Combine container with existing serviceRegistrations and group them
        var containerWithGroups = containerProvider
            .Combine(serviceRegistrations)
            .Combine(msbuildPropertiesProvider)
            .Select(static (source, _) =>
            {
                var ((container, registrations), msbuildProps) = source;
                return GroupRegistrationsForContainer(container, registrations, msbuildProps.Features);
            });

        // Combine with compilation info and MSBuild properties
        var containerWithCompilationInfo = containerWithGroups
            .Combine(compilationInfoProvider)
            .Combine(msbuildPropertiesProvider);

        // Generate Container output (separate from Registration output)
        context.RegisterSourceOutput(containerWithCompilationInfo, static (ctx, source) =>
        {
            var ((containerWithGroups, compilationInfo), msbuildProps) = source;
            GenerateContainerOutput(in ctx, containerWithGroups, compilationInfo.AssemblyName, msbuildProps, compilationInfo.HasDIPackage);
        });
    }

}
