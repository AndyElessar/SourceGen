using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;

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
        description: "InjectAttribute cannot be applied to static members, members that cannot be assigned/invoked, or methods that do not return void.");

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
    /// SGIOC016: Factory Method is unmatched - Generic factory method does not have [IocGenericFactory] attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor GenericFactoryMissingAttribute = new(
        id: "SGIOC016",
        title: "Factory Method is unmatched",
        messageFormat: "Generic factory method '{0}' must be marked with [IocGenericFactory] attribute to specify type parameter mapping",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When using a generic factory method, you must mark it with [IocGenericFactory] attribute to specify how service type placeholders map to factory method type parameters.");

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
        description: "Each element in InjectMembers must be either a nameof() expression or an array literal with member name, optional key, and optional KeyType.");

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
        description: "Members specified in InjectMembers must be injectable: instance properties with accessible setters, non-readonly fields, and ordinary non-generic void-returning methods, all of which must be public, internal, or protected internal.");

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
        InjectMembersNonInjectableMember
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

        // Get attribute type symbols for faster lookup (including generic variants)
        var attributeSymbols = new IoCAttributeSymbols(context.Compilation);

        // Check if any IoC attribute is available
        if (!attributeSymbols.HasAnyRegistrationAttribute)
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
        var assemblyAttributeSyntaxTrees = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);

        // First pass: collect services and do immediate validation (SGIOC001)
        context.RegisterSymbolAction(ctx => CollectAndValidateNamedType(ctx, analyzerContext), SymbolKind.NamedType);

        // SGIOC012: Analyze IoCRegisterDefaultsAttribute on types (class, struct, interface)
        context.RegisterSymbolAction(ctx => AnalyzeTypeLevelDefaultsAttribute(ctx, analyzerContext), SymbolKind.NamedType);

        // SGIOC007 + SGIOC022: Analyze InjectAttribute usage and feature gating on members
        context.RegisterSymbolAction(ctx => AnalyzeInjectAttribute(ctx, analyzerContext), SymbolKind.Property, SymbolKind.Field, SymbolKind.Method);

        // SGIOC006: Analyze duplicated keyed service attributes on parameters
        context.RegisterSymbolAction(AnalyzeDuplicatedKeyedServiceAttributes, SymbolKind.Method);

        // SGIOC008: Analyze Factory and Instance members specified via nameof()
        // Using RegisterSyntaxNodeAction to avoid RS1030 warning (do not use Compilation.GetSemanticModel)
        context.RegisterSyntaxNodeAction(ctx => AnalyzeFactoryAndInstanceOnAttribute(ctx, analyzerContext), SyntaxKind.Attribute);

        // Resolve nameof() key types for KeyType.Csharp registrations (avoids RS1030)
        context.RegisterSyntaxNodeAction(ctx => ResolveCsharpKeyTypes(ctx, analyzerContext), SyntaxKind.Attribute);

        // SGIOC017: Analyze [IocGenericFactory] attribute for duplicated placeholder types
        context.RegisterSymbolAction(AnalyzeIocGenericFactoryAttribute, SymbolKind.Method);

        // Analyze assembly-level IoCRegisterFor attributes using SemanticModelAction for IDE squiggles
        context.RegisterSemanticModelAction(ctx => AnalyzeAssemblyLevelRegistrations(ctx, analyzerContext, assemblyAttributeSyntaxTrees));

        // Second pass: analyze dependencies after all services are collected (SGIOC002, SGIOC003-005)
        context.RegisterCompilationEndAction(ctx => AnalyzeAllDependencies(ctx, analyzerContext));
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
        foreach (var (targetTypeName, location) in analyzerContext.DuplicatedDefaults)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DuplicatedDefaultSettings,
                location,
                targetTypeName));
        }

        // Reuse these collections across all services to reduce allocations
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pathStack = new Stack<INamedTypeSymbol>();

        foreach (var kvp in analyzerContext.RegisteredServices)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var serviceInfo = kvp.Value;

            // SGIOC013/SGIOC014: Analyze ServiceKey parameter type mismatches
            AnalyzeServiceKeyTypeMismatch(context.ReportDiagnostic, serviceInfo, context.CancellationToken);

            // SGIOC015: Analyze KeyValuePair/Dictionary key type mismatches
            AnalyzeKeyValuePairKeyTypeMismatch(context.ReportDiagnostic, serviceInfo, analyzerContext, context.CancellationToken);

            AnalyzeDependencies(
                context.ReportDiagnostic,
                analyzerContext,
                serviceInfo,
                visited,
                pathStack,
                context.CancellationToken);
        }
    }

    private static IocFeatures ParseIocFeatures(AnalyzerOptions options)
    {
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var rawFeatures);
        return IocFeaturesHelper.Parse(rawFeatures);
    }
}

