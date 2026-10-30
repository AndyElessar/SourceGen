using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc;

/// <summary>
/// Analyzer for IoC registration attributes.
/// Reports diagnostics for invalid attribute usage, circular dependencies, and service lifetime conflicts.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RegisterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// SGIOC001: Invalid Attribute Usage - IoCRegisterAttribute or IoCRegisterForAttribute is marked on private or abstract class.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidAttributeUsage = new(
        id: "SGIOC001",
        title: "Invalid Attribute Usage",
        messageFormat: "The type '{0}' cannot be registered because it is {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "IoCRegisterAttribute and IoCRegisterForAttribute cannot be applied to private or abstract classes.");

    /// <summary>
    /// SGIOC002: Circular Dependency Detected - Circular dependencies are detected among registered services.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularDependency = new(
        id: "SGIOC002",
        title: "Circular Dependency Detected",
        messageFormat: "Circular dependency detected: {0}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Circular dependencies among registered services will cause runtime errors.");

    /// <summary>
    /// SGIOC003: Service Lifetime Conflict Detected - Singleton service depending on Scoped service.
    /// </summary>
    public static readonly DiagnosticDescriptor SingletonDependsOnScoped = new(
        id: "SGIOC003",
        title: "Service Lifetime Conflict Detected",
        messageFormat: "Lifetime conflict: Singleton service '{0}' depends on Scoped service '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Singleton service should not depend on a Scoped service.");

    /// <summary>
    /// SGIOC004: Dangerous Service Lifetime Dependency - Singleton service depending on Transient service.
    /// </summary>
    public static readonly DiagnosticDescriptor SingletonDependsOnTransient = new(
        id: "SGIOC004",
        title: "Dangerous Service Lifetime Dependency Detected",
        messageFormat: "Dangerous lifetime dependency: Singleton service '{0}' depends on Transient service '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Singleton service should not depend on a Transient service. The Transient instance will be captured and live for the application lifetime.");

    /// <summary>
    /// SGIOC005: Dangerous Service Lifetime Dependency - Scoped service depending on Transient service.
    /// </summary>
    public static readonly DiagnosticDescriptor ScopedDependsOnTransient = new(
        id: "SGIOC005",
        title: "Dangerous Service Lifetime Dependency Detected",
        messageFormat: "Dangerous lifetime dependency: Scoped service '{0}' depends on Transient service '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A Scoped service should not depend on a Transient service. The Transient instance will be captured for the scope lifetime.");

    /// <summary>
    /// SGIOC007: Invalid Attribute Usage - InjectAttribute is marked on static member, inaccessible member, or method that does not return void.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidInjectAttributeUsage = new(
        id: "SGIOC007",
        title: "Invalid Attribute Usage",
        messageFormat: "InjectAttribute cannot be applied to '{0}' because {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "InjectAttribute cannot be applied to static members, members that cannot be assigned/invoked, or methods that do not return void (or non-generic Task when AsyncMethodInject is enabled).");

    /// <summary>
    /// SGIOC008: Invalid Attribute Usage - Factory or Instance uses nameof() but the referenced member is not static or is inaccessible.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidFactoryOrInstanceMember = new(
        id: "SGIOC008",
        title: "Invalid Attribute Usage",
        messageFormat: "The {0} member '{1}' specified via nameof() is {2}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using nameof() to specify a Factory or Instance, the referenced field or property must be static and accessible.");

    /// <summary>
    /// SGIOC009: Invalid Attribute Usage - Instance is specified but Lifetime is not Singleton.
    /// </summary>
    public static readonly DiagnosticDescriptor InstanceRequiresSingleton = new(
        id: "SGIOC009",
        title: "Invalid Attribute Usage",
        messageFormat: "Instance registration '{0}' requires Singleton lifetime, but '{1}' was specified",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using Instance to provide a pre-created object, the Lifetime must be Singleton because the same instance will be returned for every resolution.");

    /// <summary>
    /// SGIOC010: Invalid Attribute Usage - Both Factory and Instance are specified on the same attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor FactoryAndInstanceConflict = new(
        id: "SGIOC010",
        title: "Invalid Attribute Usage",
        messageFormat: "Both Factory and Instance are specified on the same attribute; Factory takes precedence and Instance will be ignored",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When both Factory and Instance are specified on the same IoCRegisterAttribute or IoCRegisterForAttribute, Factory takes precedence and Instance will be ignored. Remove one of them to avoid confusion.");

    /// <summary>
    /// SGIOC006: Duplicated Attribute Usage - Both FromKeyedServicesAttribute and InjectAttribute are marked on the same parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedKeyedServiceAttribute = new(
        id: "SGIOC006",
        title: "Duplicated Attribute Usage",
        messageFormat: "Parameter '{0}' has both [FromKeyedServices] and [Inject] attributes; [FromKeyedServices] will take precedence",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When both [FromKeyedServices] and [Inject] attributes are applied to the same parameter, [FromKeyedServices] takes precedence and [Inject] is ignored.");

    /// <summary>
    /// SGIOC011: Duplicated Registration Detected - Same implementation type, key, and at least one matching tag are registered multiple times.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedRegistration = new(
        id: "SGIOC011",
        title: "Duplicated Registration Detected",
        messageFormat: "Duplicated registration: type '{0}'{1} is already registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same implementation type with the same key and at least one overlapping tag is registered multiple times. Only the last registration will be effective.");

    /// <summary>
    /// SGIOC012: Duplicated IoCRegisterDefaults Detected - Same target type and at least one matching tag has multiple default settings.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedDefaultSettings = new(
        id: "SGIOC012",
        title: "Duplicated Registration Detected",
        messageFormat: "Duplicated IoCRegisterDefaults: target type '{0}' already has default settings defined",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same target type with at least one overlapping tag has multiple IoCRegisterDefaultsAttribute definitions. Only the first definition will be used.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC013: Key type is unmatched - ServiceKeyAttribute parameter type does not match the registered key type.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceKeyTypeMismatch = new(
        id: "SGIOC013",
        title: "Key type is unmatched",
        messageFormat: "[ServiceKey] parameter '{0}' has type '{1}' but the registered key type is '{2}'",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The parameter marked with [ServiceKey] attribute must have a type that matches or is assignable from the registered key type in [IoCRegister] or [IoCRegisterFor] attribute.");

    /// <summary>
    /// SGIOC014: Key does not exist - ServiceKeyAttribute is marked on parameter but no Key is registered.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceKeyNotRegistered = new(
        id: "SGIOC014",
        title: "Key does not exist",
        messageFormat: "[ServiceKey] parameter '{0}' is used but no Key is specified in [IoCRegister] or [IoCRegisterFor] attribute",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The parameter marked with [ServiceKey] attribute requires a Key to be specified in [IoCRegister] or [IoCRegisterFor] attribute.");

    /// <summary>
    /// SGIOC015: KeyValuePair's Key type is unmatched - Injected KeyValuePair/Dictionary key type does not match any registered keyed service's key type.
    /// </summary>
    public static readonly DiagnosticDescriptor KeyValuePairKeyTypeMismatch = new(
        id: "SGIOC015",
        title: "KeyValuePair's Key type is unmatched",
        messageFormat: "KeyValuePair parameter '{0}' has key type '{1}' but no keyed service for '{2}' has a matching key type",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When injecting KeyValuePair<K, V>, IDictionary<K, V>, or collection of KeyValuePair<K, V>, the key type K must match the registered key type of at least one keyed service for V.");

    /// <summary>
    /// SGIOC016: Factory Method is unmatched - Generic factory method has no valid type parameter mapping.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericFactoryMissingAttribute = new(
        id: "SGIOC016",
        title: "Factory Method is unmatched",
        messageFormat: "Generic factory method '{0}' has no valid type parameter mapping: {1}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using a generic factory method, you must mark it with [IocGenericFactory] attribute (or specify GenericFactoryTypeMapping) to specify how service type placeholders map to factory method type parameters. The mapping must provide one placeholder per factory method type parameter, and each placeholder must appear in the service type template.");

    /// <summary>
    /// SGIOC017: Generic Factory Method's type parameters are duplicated - Placeholder types in [IocGenericFactory] must be unique.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedGenericFactoryPlaceholders = new(
        id: "SGIOC017",
        title: "Generic Factory Method's type parameters are duplicated",
        messageFormat: "[IocGenericFactory] or GenericFactoryTypeMapping has duplicated placeholder type '{0}'; each placeholder type must be unique",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The placeholder types in [IocGenericFactory] or GenericFactoryTypeMapping (from second to last) must be unique. Duplicated types make it impossible to distinguish which type argument maps to which factory method type parameter.");

    /// <summary>
    /// SGIOC022: Inject attribute ignored due to disabled feature.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectFeatureDisabled = new(
        id: "SGIOC022",
        title: "Inject attribute ignored due to disabled feature",
        messageFormat: "'{0}' has [IocInject] but {1} feature is not enabled. Add '{1}' to <SourceGenIocFeatures> in your project file.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When SourceGenIocFeatures disables a member injection feature, [IocInject] on that member is ignored during generation.");

    /// <summary>
    /// SGIOC023: Invalid InjectMembers element format.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectMembersInvalidFormat = new(
        id: "SGIOC023",
        title: "Invalid InjectMembers element format",
        messageFormat: "InjectMembers element at index {0} is invalid; expected nameof(member) or new object[] {{ nameof(member), key [, KeyType] }}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each element in InjectMembers must be either a nameof() expression or an array literal with member name, key, and optional KeyType. With KeyType.Value (the default), the key must be a non-null constant.");

    /// <summary>
    /// SGIOC024: InjectMembers specifies non-injectable member.
    /// </summary>
    public static readonly DiagnosticDescriptor InjectMembersNonInjectableMember = new(
        id: "SGIOC024",
        title: "InjectMembers specifies non-injectable member",
        messageFormat: "Member '{0}' specified in InjectMembers cannot be injected: {1}",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Members specified in InjectMembers must be injectable: instance properties with accessible setters, non-readonly fields, and ordinary non-generic void-returning methods (or non-generic Task-returning when AsyncMethodInject is enabled), all of which must be public, internal, or protected internal.");

    /// <summary>
    /// SGIOC026: AsyncMethodInject feature requires MethodInject to be enabled.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncMethodInjectRequiresMethodInject = new(
        id: "SGIOC026",
        title: "Invalid feature combination",
        messageFormat: "'AsyncMethodInject' feature requires 'MethodInject' to be enabled. Add 'MethodInject' to <SourceGenIocFeatures>.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "AsyncMethodInject delegates async method injection to the source generator, which requires MethodInject to already be enabled. Add MethodInject alongside AsyncMethodInject in SourceGenIocFeatures.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC030: Synchronous dependency requested for async-init-only service.
    /// </summary>
    public static readonly DiagnosticDescriptor SyncDependencyOnAsyncInitService = new(
        id: "SGIOC030",
        title: "Synchronous dependency requested for async-init service",
        messageFormat: "'{0}' requires '{1}' but this service has async inject methods and no synchronous registration exists. Use 'Task<{1}>'.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a service is registered with async inject methods, consumers must request Task<T> instead of T because no synchronous resolution path exists.");

    /// <summary>
    /// SGIOC028: [IocInject] method is declared as async void, which cannot be awaited.
    /// </summary>
    public static readonly DiagnosticDescriptor AsyncVoidInjectMethod = new(
        id: "SGIOC028",
        title: "async void injection method cannot be awaited",
        messageFormat: "[IocInject] method '{0}' is 'async void' which cannot be awaited. Change return type to 'Task' for async initialization, or remove the 'async' modifier for synchronous injection.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Methods marked with [IocInject] that are declared as async void cannot be awaited by the source generator. Change the return type to Task to enable async injection, or remove the async modifier if the method is synchronous.");

    /// <summary>
    /// SGIOC032: Explicit service type is not compatible with the implementation and is skipped.
    /// </summary>
    public static readonly DiagnosticDescriptor IncompatibleServiceType = new(
        id: "SGIOC032",
        title: "Service type is not compatible with implementation",
        messageFormat: "Service type '{0}' cannot be registered for implementation '{1}' and will be skipped: {2}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An open generic implementation can only be registered as open generic service types it implements, and a closed implementation cannot be registered as an open generic service type. Incompatible service types are skipped during generation.");

    /// <summary>
    /// SGIOC033: A Func factory with arguments creates a new instance per call, ignoring the service's Singleton or Scoped lifetime.
    /// </summary>
    public static readonly DiagnosticDescriptor FuncWithArgumentsIgnoresLifetime = new(
        id: "SGIOC033",
        title: "Func with arguments ignores the service lifetime",
        messageFormat: "'{0}' depends on '{1}', which creates a new '{2}' on every call although '{2}' is registered as {3}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Func<T1, ..., TReturn> with input parameters constructs a new instance from its arguments on every call, so a Singleton or Scoped lifetime of the service does not apply. Register the service as Transient, or depend on the parameterless Func<T> or the service itself to follow its lifetime.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        InvalidAttributeUsage,
        CircularDependency,
        SingletonDependsOnScoped,
        SingletonDependsOnTransient,
        ScopedDependsOnTransient,
        InvalidInjectAttributeUsage,
        InvalidFactoryOrInstanceMember,
        InstanceRequiresSingleton,
        FactoryAndInstanceConflict,
        DuplicatedKeyedServiceAttribute,
        DuplicatedRegistration,
        DuplicatedDefaultSettings,
        ServiceKeyTypeMismatch,
        ServiceKeyNotRegistered,
        KeyValuePairKeyTypeMismatch,
        GenericFactoryMissingAttribute,
        DuplicatedGenericFactoryPlaceholders,
        InjectFeatureDisabled,
        InjectMembersInvalidFormat,
        InjectMembersNonInjectableMember,
        AsyncMethodInjectRequiresMethodInject,
        SyncDependencyOnAsyncInitService,
        AsyncVoidInjectMethod,
        IncompatibleServiceType,
        FuncWithArgumentsIgnoresLifetime
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for compilation start to cache attribute symbols and collect services
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var features = ParseIocFeatures(context.Options);

        // SGIOC026: AsyncMethodInject requires MethodInject — report once per compilation
        if((features & IocFeatures.AsyncMethodInject) != 0 && (features & IocFeatures.MethodInject) == 0)
        {
            context.RegisterCompilationEndAction(static ctx =>
                ctx.ReportDiagnostic(Diagnostic.Create(AsyncMethodInjectRequiresMethodInject, Location.None)));
        }

        // Get attribute type symbols for faster lookup (including generic variants)
        var attributeSymbols = new IocAttributeSymbols(context.Compilation);

        // Check if any IoC attribute is available
        if(!attributeSymbols.HasAnyRegistrationAttribute)
            return;

        // Use ConcurrentDictionary for thread-safe collection during parallel symbol analysis
        var registeredServices = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Index for service type -> implementation lookup (interfaces/base classes)
        var serviceTypeIndex = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Collect default settings from IoCRegisterDefaultSettingsAttribute using shared method
        // Also collect duplicated default settings for SGIOC012 reporting
        var duplicatedDefaults = new ConcurrentBag<(string TargetTypeName, Location? Location)>();
        // Track seen (target type, single tag) pairs for SGIOC012 (shared between assembly and type-level)
        var seenDefaultTargetTypes = new ConcurrentDictionary<(string TargetTypeName, string Tag), Location?>();
        var defaultSettings = CollectDefaults(context.Compilation, attributeSymbols, duplicatedDefaults, seenDefaultTargetTypes, context.CancellationToken);

        var analyzerContext = new AnalyzerContext(
            attributeSymbols,
            registeredServices,
            serviceTypeIndex,
            defaultSettings,
            duplicatedDefaults,
            seenDefaultTargetTypes,
            features);

        // Collect assembly-level IoCRegisterFor attributes first (synchronously during compilation start)
        var assemblyRegistrations = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);
        var attributeNameFilter = new IocAttributeNameFilter(
            context.Compilation,
            attributeSymbols.IocRegisterAttribute,
            attributeSymbols.IocRegisterAttribute_T1,
            attributeSymbols.IocRegisterForAttribute,
            attributeSymbols.IocRegisterForAttribute_T1,
            attributeSymbols.IocRegisterDefaultsAttribute,
            attributeSymbols.IocRegisterDefaultsAttribute_T1);

        // First pass: collect services and do immediate validation (SGIOC001, 009, 011, 032),
        // and SGIOC012 for IoCRegisterDefaultsAttribute on types (class, struct, interface)
        context.RegisterSymbolAction(ctx =>
        {
            CollectAndValidateNamedType(ctx, analyzerContext);
            AnalyzeTypeLevelDefaultsAttribute(ctx, analyzerContext);
        }, SymbolKind.NamedType);

        // SGIOC007 + SGIOC022 + SGIOC028: Analyze InjectAttribute usage and feature gating on members
        context.RegisterSymbolAction(ctx => AnalyzeInjectAttribute(ctx, analyzerContext), SymbolKind.Property, SymbolKind.Field);

        // Methods: SGIOC007/022/028 InjectAttribute usage, SGIOC006 duplicated keyed service attributes on parameters,
        // and SGIOC016/017 [IocGenericFactory] mappings
        context.RegisterSymbolAction(ctx =>
        {
            AnalyzeInjectAttribute(ctx, analyzerContext);
            AnalyzeDuplicatedKeyedServiceAttributes(ctx);
            AnalyzeIocGenericFactoryAttribute(ctx);
        }, SymbolKind.Method);

        // SGIOC008/010/023/024 + nameof() key type resolution for KeyType.Csharp registrations,
        // and SGIOC001/009/011/032 for assembly-level IoCRegisterFor attributes.
        // Using RegisterSyntaxNodeAction to avoid RS1030 warning (do not use Compilation.GetSemanticModel)
        context.RegisterSyntaxNodeAction(ctx => AnalyzeIocAttributeSyntax(ctx, analyzerContext, assemblyRegistrations, attributeNameFilter), SyntaxKind.Attribute);

        // Second pass: analyze dependencies after all services are collected (SGIOC002, SGIOC003-005)
        context.RegisterCompilationEndAction(ctx => AnalyzeAllDependencies(ctx, analyzerContext));
    }

    /// <summary>
    /// Binds each attribute that can be an IoC attribute once and dispatches to the IoC attribute syntax analyses.
    /// Assembly-level IoCRegisterFor attributes were bound at compilation start, so they are not bound again.
    /// </summary>
    private static void AnalyzeIocAttributeSyntax(
        SyntaxNodeAnalysisContext context,
        AnalyzerContext analyzerContext,
        Dictionary<(SyntaxTree Tree, TextSpan Span), AssemblyRegistration> assemblyRegistrations,
        IocAttributeNameFilter attributeNameFilter)
    {
        if(context.Node is not AttributeSyntax attributeSyntax)
            return;

        INamedTypeSymbol attributeClass;
        AttributeData? attributeData = null;
        if(assemblyRegistrations.TryGetValue((attributeSyntax.SyntaxTree, attributeSyntax.Span), out var assemblyRegistration))
        {
            AnalyzeAssemblyLevelRegistration(context.ReportDiagnostic, analyzerContext, assemblyRegistration, attributeSyntax.GetLocation());
            attributeData = assemblyRegistration.Attribute;
            attributeClass = attributeData.AttributeClass!;
        }
        else
        {
            // Binding every attribute in the compilation dominates this action, so skip names that cannot be IoC attributes
            if(!attributeNameFilter.MayBeIocAttribute(attributeSyntax, context.CancellationToken))
                return;

            var attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol;
            if(attributeSymbol is not IMethodSymbol { ContainingType: { } boundAttributeClass })
                return;

            if(!AnalyzerHelpers.IsAnyIoCAttribute(boundAttributeClass, analyzerContext.AttributeSymbols))
                return;

            attributeClass = boundAttributeClass;
        }

        AnalyzeFactoryAndInstanceOnAttribute(context, attributeSyntax, attributeClass, analyzerContext);
        ResolveCsharpKeyTypes(context, attributeSyntax, attributeClass, attributeData, analyzerContext);
    }

    /// <summary>
    /// Analyzes all dependencies after all services have been collected.
    /// This ensures we have a complete picture of all registered services before checking dependencies.
    /// Also reports SGIOC012 for duplicated IoCRegisterDefaults, SGIOC013-014 for ServiceKey type mismatches,
    /// and SGIOC015 for KeyValuePair key type mismatches.
    /// </summary>
    private static void AnalyzeAllDependencies(CompilationAnalysisContext context, AnalyzerContext analyzerContext)
    {
        // SGIOC012: Report duplicated IoCRegisterDefaults
        foreach(var (targetTypeName, location) in analyzerContext.DuplicatedDefaults)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicatedDefaultSettings,
                location,
                targetTypeName));
        }

        // Reuse these collections across all services to reduce allocations
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pathStack = new Stack<INamedTypeSymbol>();
        var graph = new DependencyGraph(analyzerContext);
        var keyedServiceIndex = new KeyedServiceIndex(analyzerContext);

        foreach(var kvp in analyzerContext.RegisteredServices)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var serviceInfo = kvp.Value;

            // SGIOC013/SGIOC014: Analyze ServiceKey parameter type mismatches
            AnalyzeServiceKeyTypeMismatch(context.ReportDiagnostic, serviceInfo, context.CancellationToken);

            // SGIOC015: Analyze KeyValuePair/Dictionary key type mismatches
            AnalyzeKeyValuePairKeyTypeMismatch(context.ReportDiagnostic, serviceInfo, analyzerContext, keyedServiceIndex, context.CancellationToken);

            // SGIOC033: Func factories with arguments ignore Singleton/Scoped lifetimes
            AnalyzeFuncWithArgumentsLifetime(context.ReportDiagnostic, serviceInfo, analyzerContext, context.CancellationToken);

            AnalyzeDependencies(
                context.ReportDiagnostic,
                graph,
                serviceInfo,
                visited,
                pathStack,
                context.CancellationToken);
        }

        var asyncInitOnlyServiceTypes = BuildAsyncInitOnlyServiceTypes(analyzerContext);
        if(asyncInitOnlyServiceTypes.Count == 0)
            return;

        foreach(var serviceInfo in analyzerContext.RegisteredServices.Values)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeSyncDependenciesOnAsyncInitServices(
                context.ReportDiagnostic,
                serviceInfo,
                asyncInitOnlyServiceTypes,
                context.CancellationToken);
        }
    }

    private static ImmutableHashSet<ServiceSymbolKey> BuildAsyncInitOnlyServiceTypes(AnalyzerContext analyzerContext)
    {
        var serviceTypeStates = new Dictionary<ServiceSymbolKey, (bool HasAsync, bool HasSync)>();

        foreach(var serviceInfo in analyzerContext.RegisteredServices.Values)
        {
            var isAsyncInit = AnalyzerHelpers.IsAsyncInitImplementation(serviceInfo.Type, analyzerContext.Features);

            // Use pre-computed AllRegistrations (populated at collection time from attribute data) when
            // available. This correctly handles assembly-level registrations where multiple IocRegisterFor
            // attributes target the same impl type with different keys — the second and subsequent
            // registrations are not stored in RegisteredServices but are recorded in AllRegistrations.
            IEnumerable<ServiceSymbolKey> serviceTypes =
                serviceInfo.AllRegistrations.IsEmpty
                    ? CollectRegisteredServiceTypesForAnalysis(serviceInfo, analyzerContext.AttributeSymbols)
                    : serviceInfo.AllRegistrations;

            foreach(var serviceType in serviceTypes)
            {
                serviceTypeStates.TryGetValue(serviceType, out var state);
                serviceTypeStates[serviceType] = isAsyncInit
                    ? (HasAsync: true, HasSync: state.HasSync)
                    : (HasAsync: state.HasAsync, HasSync: true);
            }
        }

        var asyncOnlyServiceTypes = ImmutableHashSet.CreateBuilder<ServiceSymbolKey>();
        foreach(var kvp in serviceTypeStates)
        {
            var serviceType = kvp.Key;
            var state = kvp.Value;
            if(state is { HasAsync: true, HasSync: false })
                asyncOnlyServiceTypes.Add(serviceType);
        }

        return asyncOnlyServiceTypes.ToImmutable();
    }

    private static HashSet<ServiceSymbolKey> CollectRegisteredServiceTypesForAnalysis(
        ServiceInfo serviceInfo,
        IocAttributeSymbols attributeSymbols)
    {
        var serviceTypes = new HashSet<ServiceSymbolKey>();
        var hasRegistrationAttribute = false;

        var implementationType = serviceInfo.Type;

        foreach(var attribute in implementationType.GetAttributes())
        {
            var attrClass = attribute.AttributeClass;
            if(attrClass is null || !AnalyzerHelpers.IsIoCRegistrationAttribute(attrClass, attributeSymbols))
                continue;

            hasRegistrationAttribute = true;
            var (serviceKey, _, _) = attribute.GetKeyInfo();
            foreach(var serviceType in AnalyzerHelpers.EnumerateRegisteredServiceTypes(implementationType, attribute))
            {
                serviceTypes.Add(new(serviceType, serviceKey));
            }
        }

        if(!hasRegistrationAttribute)
        {
            foreach(var serviceType in AnalyzerHelpers.EnumerateImplicitServiceTypes(implementationType))
            {
                serviceTypes.Add(new(serviceType, serviceInfo.ServiceKey));
            }
        }

        return serviceTypes;
    }

    private static void AnalyzeSyncDependenciesOnAsyncInitServices(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        ImmutableHashSet<ServiceSymbolKey> asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken)
    {
        if(serviceInfo.Constructor is not null)
        {
            AnalyzeParameterDependenciesOnAsyncInitServices(
                reportDiagnostic,
                serviceInfo.Constructor.Parameters,
                asyncInitOnlyServiceTypes,
                cancellationToken);
        }

        foreach(var (member, injectAttribute) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch(member)
            {
                case IPropertySymbol property:
                    AnalyzeDependencyOnAsyncInitService(
                        reportDiagnostic,
                        property.Name,
                        property.Type,
                        injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                            ?? property.Locations.FirstOrDefault(),
                        injectAttribute.GetKeyInfo().Key,
                        asyncInitOnlyServiceTypes);
                    break;

                case IFieldSymbol field:
                    AnalyzeDependencyOnAsyncInitService(
                        reportDiagnostic,
                        field.Name,
                        field.Type,
                        injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                            ?? field.Locations.FirstOrDefault(),
                        injectAttribute.GetKeyInfo().Key,
                        asyncInitOnlyServiceTypes);
                    break;

                case IMethodSymbol method:
                    AnalyzeParameterDependenciesOnAsyncInitServices(
                        reportDiagnostic,
                        method.Parameters,
                        asyncInitOnlyServiceTypes,
                        cancellationToken);
                    break;
            }
        }
    }

    private static void AnalyzeParameterDependenciesOnAsyncInitServices(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        ImmutableHashSet<ServiceSymbolKey> asyncInitOnlyServiceTypes,
        CancellationToken cancellationToken)
    {
        foreach(var parameter in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(ShouldSkipAsyncInitDependencyCheck(parameter))
                continue;

            AnalyzeDependencyOnAsyncInitService(
                reportDiagnostic,
                parameter.Name,
                parameter.Type,
                parameter.Locations.FirstOrDefault(),
                parameter.GetServiceKeyAndAttributeInfo().ServiceKey,
                asyncInitOnlyServiceTypes);
        }
    }

    private static void AnalyzeDependencyOnAsyncInitService(
        Action<Diagnostic> reportDiagnostic,
        string memberName,
        ITypeSymbol dependencyType,
        Location? location,
        string? serviceKey,
        ImmutableHashSet<ServiceSymbolKey> asyncInitOnlyServiceTypes)
    {
        if(AnalyzerHelpers.TryGetAsyncWrapperElementType(dependencyType) is not null)
            return;

        if(dependencyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is not INamedTypeSymbol namedDependencyType)
            return;

        if(!asyncInitOnlyServiceTypes.Contains(new(namedDependencyType, serviceKey)))
            return;

        reportDiagnostic(Diagnostic.Create(
            SyncDependencyOnAsyncInitService,
            location,
            memberName,
            namedDependencyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private static bool ShouldSkipAsyncInitDependencyCheck(IParameterSymbol parameter)
    {
        if(parameter.HasExplicitDefaultValue)
            return true;

        if(AnalyzerHelpers.IsWellKnownServiceType(parameter.Type))
            return true;

        return parameter.GetAttributes().Any(static attr => AnalyzerHelpers.IsServiceKeyAttribute(attr.AttributeClass));
    }

    private static IocFeatures ParseIocFeatures(AnalyzerOptions options)
    {
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var rawFeatures);
        return IocFeaturesHelper.Parse(rawFeatures);
    }
}

