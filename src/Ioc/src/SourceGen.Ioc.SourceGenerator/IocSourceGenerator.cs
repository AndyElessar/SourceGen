namespace SourceGen.Ioc;

/// <summary>
/// Incremental source generator that processes <c>IocRegister*</c>, <c>IocContainer</c>, <c>IocImportModule</c>,
/// <c>IocDiscover</c>, and <c>IocRegisterDefaults</c> attributes (plus <c>IServiceProvider.GetService&lt;T&gt;</c>
/// invocations) and emits two kinds of output:
/// <list type="bullet">
///   <item><b>Register*</b> partial methods — extension methods that register services into <c>IServiceCollection</c>.</item>
///   <item><b>Container partial classes</b> — standalone DI containers that resolve services without <c>IServiceCollection</c>.</item>
/// </list>
/// <para>
/// The pipeline mirrors the stages described in <c>Spec/SPEC.spec.md</c>:
/// </para>
/// <list type="number">
///   <item>Stage 1 — Attribute detection (<c>Transforms/</c>): symbol → data model.</item>
///   <item>Stage 2 — Combine MSBuild / compilation / default-settings inputs.</item>
///   <item>Stage 3 — Per-registration processing (<c>Processing/ProcessSingleRegistration</c>, cacheable).</item>
///   <item>Stage 4 — Closed-generic resolution + grouping (<c>Processing/</c> + <c>Grouping/</c>).</item>
///   <item>Stage 5 — Emit Register output and Container output (<c>Emit/Register/</c>, <c>Emit/Container/</c>).</item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed partial class IocSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ===== Stage 1: Attribute providers (symbol -> data model) =====

        // [IocRegister] / [IocRegister<T>]
        var registerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegister(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var registerProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterGeneric(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // [IocRegisterFor] / [IocRegisterFor<T>]
        var registerForProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterForAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterFor(ctx, ct))
            .SelectMany(static (m, _) => m);

        var registerForProvider_T1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterForAttributeFullName_T1,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformRegisterForGeneric(ctx, ct))
            .SelectMany(static (m, _) => m);

        // [IocRegisterDefaults] / [IocRegisterDefaults<T>] -> (settings, impl-type registrations, factory open generics)
        var allDefaultSettingsResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocRegisterDefaultsAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDefaultSettings(ctx, ct))
            .SelectMany(static (m, _) => m)
            .Collect()
            .Combine(context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Constants.IocRegisterDefaultsAttributeFullName_T1,
                    predicate: static (_, _) => true,
                    transform: static (ctx, ct) => TransformDefaultSettingsGeneric(ctx, ct))
                .SelectMany(static (m, _) => m)
                .Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        var allDefaultSettings = allDefaultSettingsResults
            .SelectMany(static (results, _) => results
                .Where(static r => r.DefaultSettings is not null)
                .Select(static r => r.DefaultSettings!))
            .Collect();

        var defaultSettingsImplTypeRegistrations = allDefaultSettingsResults
            .SelectMany(static (results, _) => results.SelectMany(static r => r.ImplementationTypeRegistrations));

        var factoryBasedOpenGenericEntries = allDefaultSettingsResults
            .SelectMany(static (results, _) => results.SelectMany(static r => r.OpenGenericEntries))
            .Collect();

        // [IocImportModule] / [IocImportModule<T>] -> (imported settings, imported open generics)
        var allImportModuleResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocImportModuleAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformImportModule(ctx, ct))
            .SelectMany(static (m, _) => m)
            .Collect()
            .Combine(context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Constants.IocImportModuleAttributeFullName_T1,
                    predicate: static (_, _) => true,
                    transform: static (ctx, ct) => TransformImportModuleGeneric(ctx, ct))
                .SelectMany(static (m, _) => m)
                .Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        var allImportedDefaultSettings = allImportModuleResults
            .SelectMany(static (results, _) => results.SelectMany(static r => r.DefaultSettings))
            .Collect();

        var allImportedOpenGenerics = allImportModuleResults
            .SelectMany(static (results, _) => results.SelectMany(static r => r.OpenGenericEntries))
            .Collect();

        // [IocDiscover] / [IocDiscover<T>] -> closed-generic dependencies discovered at compile time
        var allDiscoverProviders = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocDiscoverAttributeFullName,
                predicate: static (_, _) => true,
                transform: static (ctx, ct) => TransformDiscover(ctx, ct))
            .SelectMany(static (m, _) => m)
            .Collect()
            .Combine(context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    Constants.IocDiscoverAttributeFullName_T1,
                    predicate: static (_, _) => true,
                    transform: static (ctx, ct) => TransformDiscoverGeneric(ctx, ct))
                .SelectMany(static (m, _) => m)
                .Collect())
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        // IServiceProvider.GetService / GetRequiredService / GetKeyedService / GetServices invocations
        var invocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => PredicateInvocations(node),
                transform: static (ctx, ct) => TransformInvocations(ctx, ct))
            .SelectMany(static (candidates, _) => candidates)
            .Collect();

        // [IocContainer]
        var containerProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                Constants.IocContainerAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => TransformContainer(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // ===== Stage 2: Compilation / MSBuild / default-settings inputs =====

        var defaultLifetimeProvider = BuildDefaultLifetimeProvider(context);
        var msbuildPropertiesProvider = BuildMsBuildPropertiesProvider(context);
        var compilationInfoProvider = BuildCompilationInfoProvider(context);

        // Current-assembly settings take precedence over imported settings (DefaultSettingsMap uses first-match semantics).
        var combinedDefaultSettings = allDefaultSettings
            .Combine(allImportedDefaultSettings)
            .Combine(defaultLifetimeProvider)
            .Select(static (combined, _) =>
            {
                var ((currentAssembly, imported), defaultLifetime) = combined;
                var allSettings = currentAssembly.AddRange(imported);
                return new DefaultSettingsMap(allSettings, defaultLifetime ?? ServiceLifetime.Transient);
            });

        // ===== Stage 3: Per-registration processing (cacheable per registration) =====

        var basicRegistrationResults1 = registerProvider
            .Combine(combinedDefaultSettings)
            .Select(static (s, ct) => ProcessSingleRegistration(s.Left, s.Right, ct));

        var basicRegistrationResults1_T1 = registerProvider_T1
            .Combine(combinedDefaultSettings)
            .Select(static (s, ct) => ProcessSingleRegistration(s.Left, s.Right, ct));

        var basicRegistrationResults2 = registerForProvider
            .Combine(combinedDefaultSettings)
            .Select(static (s, ct) => ProcessSingleRegistration(s.Left, s.Right, ct));

        var basicRegistrationResults2_T1 = registerForProvider_T1
            .Combine(combinedDefaultSettings)
            .Select(static (s, ct) => ProcessSingleRegistration(s.Left, s.Right, ct));

        // ImplementationTypes from [IocRegisterDefaults] already have all settings applied; just convert.
        var basicRegistrationResults3 = defaultSettingsImplTypeRegistrations
            .Select(static (registrations, ct) => ProcessSingleRegistrationFromDefaults(registrations, ct));

        var allBasicResults = CollectAndConcat(
            basicRegistrationResults1,
            basicRegistrationResults1_T1,
            basicRegistrationResults2,
            basicRegistrationResults2_T1,
            basicRegistrationResults3);

        // ===== Stage 4: Closed-generic resolution + grouping =====

        var combinedClosedGenericDependencies = invocations
            .Combine(allDiscoverProviders)
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        var allOpenGenericEntries = factoryBasedOpenGenericEntries
            .Combine(allImportedOpenGenerics)
            .Select(static (combined, _) => combined.Left.AddRange(combined.Right));

        var serviceRegistrations = allBasicResults
            .Combine(combinedClosedGenericDependencies)
            .Combine(allOpenGenericEntries)
            .Select(static (s, ct) => CombineAndResolveClosedGenerics(in s.Left.Left, in s.Left.Right, in s.Right, ct));

        // ===== Stage 5a: Emit Register output =====

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

        // ===== Stage 5b: Emit Container output =====
        // ExplicitOnly containers do not depend on serviceRegistrations (independent caching branch).

        var explicitOnlyContainerWithGroups = containerProvider
            .Where(static c => c.ExplicitOnly)
            .Combine(msbuildPropertiesProvider)
            .Select(static (source, _) =>
            {
                var (container, msbuildProps) = source;
                return GroupExplicitOnlyRegistrations(container, msbuildProps.Features);
            });

        var normalContainerWithGroups = containerProvider
            .Where(static c => !c.ExplicitOnly)
            .Combine(serviceRegistrations)
            .Select(static (source, _) =>
            {
                var (container, registrations) = source;
                var filtered = FilterRegistrationsForContainer(container, registrations);
                return (container, filtered);
            })
            .Combine(msbuildPropertiesProvider)
            .Select(static (source, _) =>
            {
                var ((container, filtered), msbuildProps) = source;
                return GroupRegistrationsForContainer(container, filtered, msbuildProps.Features);
            });

        EmitContainerOutput(explicitOnlyContainerWithGroups);
        EmitContainerOutput(normalContainerWithGroups);

        void EmitContainerOutput(IncrementalValuesProvider<ContainerWithGroups> groups)
        {
            var withInfo = groups.Combine(compilationInfoProvider).Combine(msbuildPropertiesProvider);
            context.RegisterSourceOutput(withInfo, static (ctx, source) =>
            {
                var ((containerWithGroups, compilationInfo), msbuildProps) = source;
                GenerateContainerOutput(in ctx, containerWithGroups, compilationInfo.AssemblyName, msbuildProps, compilationInfo.HasDIPackage);
            });
        }
    }

    /// <summary>
    /// Concatenates five <see cref="IncrementalValuesProvider{T}"/> streams into a single
    /// <see cref="IncrementalValueProvider{T}"/> of <see cref="ImmutableArray{T}"/> via Collect+Combine.
    /// Used to merge the four <c>IocRegister*</c> attribute pipelines plus the <c>[IocRegisterDefaults]</c>
    /// implementation-type pipeline into one collection before closed-generic resolution.
    /// </summary>
    private static IncrementalValueProvider<ImmutableArray<T>> CollectAndConcat<T>(
        IncrementalValuesProvider<T> a,
        IncrementalValuesProvider<T> b,
        IncrementalValuesProvider<T> c,
        IncrementalValuesProvider<T> d,
        IncrementalValuesProvider<T> e)
        => a.Collect()
            .Combine(b.Collect())
            .Combine(c.Collect())
            .Combine(d.Collect())
            .Combine(e.Collect())
            .Select(static (combined, _) =>
            {
                var ((((p1, p2), p3), p4), p5) = combined;
                var builder = ImmutableArray.CreateBuilder<T>(p1.Length + p2.Length + p3.Length + p4.Length + p5.Length);
                builder.AddRange(p1);
                builder.AddRange(p2);
                builder.AddRange(p3);
                builder.AddRange(p4);
                builder.AddRange(p5);
                return builder.MoveToImmutable();
            });
}
