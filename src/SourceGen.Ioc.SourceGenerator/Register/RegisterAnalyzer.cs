using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.SourceGenerator.Register;

/// <summary>
/// Analyzer for IoC registration attributes.
/// Reports diagnostics for invalid attribute usage, circular dependencies, and service lifetime conflicts.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegisterAnalyzer : DiagnosticAnalyzer
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
    /// SGIOC011: Duplicated Registration Detected - Same implementation type and key are registered multiple times.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedRegistration = new(
        id: "SGIOC011",
        title: "Duplicated Registration Detected",
        messageFormat: "Duplicated registration: type '{0}'{1} is already registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same implementation type with the same key is registered multiple times. Only the last registration will be effective.");

    /// <summary>
    /// SGIOC012: Duplicated IoCRegisterDefaults Detected - Same target type has multiple default settings.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicatedDefaultSettings = new(
        id: "SGIOC012",
        title: "Duplicated Registration Detected",
        messageFormat: "Duplicated IoCRegisterDefaults: target type '{0}' already has default settings defined",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same target type has multiple IoCRegisterDefaultsAttribute definitions. Only the first definition will be used.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

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
        DuplicatedDefaultSettings
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
        // Get attribute type symbols for faster lookup (including generic variants)
        var iocRegisterAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterAttributeFullName);
        var iocRegisterAttribute_T1 = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterAttributeFullName_T1);
        var iocRegisterAttribute_T2 = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterAttributeFullName_T2);
        var iocRegisterAttribute_T3 = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterAttributeFullName_T3);
        var iocRegisterAttribute_T4 = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterAttributeFullName_T4);
        var iocRegisterForAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterForAttributeFullName);
        var iocRegisterForAttribute_T1 = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterForAttributeFullName_T1);
        var iocRegisterDefaultsAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterDefaultsAttributeFullName);
        var iocRegisterDefaultsAttribute_T1 = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterDefaultsAttributeFullName_T1);

        // Check if any IoC attribute is available
        var hasAnyIoCRegisterAttribute = iocRegisterAttribute is not null
            || iocRegisterAttribute_T1 is not null
            || iocRegisterAttribute_T2 is not null
            || iocRegisterAttribute_T3 is not null
            || iocRegisterAttribute_T4 is not null;
        var hasAnyIoCRegisterForAttribute = iocRegisterForAttribute is not null
            || iocRegisterForAttribute_T1 is not null;

        if(!hasAnyIoCRegisterAttribute && !hasAnyIoCRegisterForAttribute)
            return;

        // Use ConcurrentDictionary for thread-safe collection during parallel symbol analysis
        var registeredServices = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Index for service type -> implementation lookup (interfaces/base classes)
        var serviceTypeIndex = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Collect default settings from IoCRegisterDefaultSettingsAttribute using shared method
        // Also collect duplicated default settings for SGIOC012 reporting
        var duplicatedDefaults = new ConcurrentBag<(string TargetTypeName, Location? Location)>();
        // Track seen target types for SGIOC012 (shared between assembly and type-level)
        var seenDefaultTargetTypes = new ConcurrentDictionary<string, Location?>();
        var defaultSettings = CollectDefaults(context.Compilation, iocRegisterDefaultsAttribute, iocRegisterDefaultsAttribute_T1, duplicatedDefaults, seenDefaultTargetTypes, context.CancellationToken);

        var analyzerContext = new AnalyzerContext(
            iocRegisterAttribute,
            iocRegisterAttribute_T1,
            iocRegisterAttribute_T2,
            iocRegisterAttribute_T3,
            iocRegisterAttribute_T4,
            iocRegisterForAttribute,
            iocRegisterForAttribute_T1,
            iocRegisterDefaultsAttribute,
            iocRegisterDefaultsAttribute_T1,
            registeredServices,
            serviceTypeIndex,
            defaultSettings,
            duplicatedDefaults,
            seenDefaultTargetTypes);

        // Collect assembly-level IoCRegisterFor attributes first (synchronously during compilation start)
        var assemblyAttributeSyntaxTrees = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);

        // First pass: collect services and do immediate validation (SGIOC001)
        context.RegisterSymbolAction(ctx => CollectAndValidateNamedType(ctx, analyzerContext), SymbolKind.NamedType);

        // SGIOC012: Analyze IoCRegisterDefaultsAttribute on types (class, struct, interface)
        context.RegisterSymbolAction(ctx => AnalyzeTypeLevelDefaultsAttribute(ctx, analyzerContext), SymbolKind.NamedType);

        // SGIOC007: Analyze InjectAttribute on members
        context.RegisterSymbolAction(AnalyzeInjectAttribute, SymbolKind.Property, SymbolKind.Field, SymbolKind.Method);

        // SGIOC006: Analyze duplicated keyed service attributes on parameters
        context.RegisterSymbolAction(AnalyzeDuplicatedKeyedServiceAttributes, SymbolKind.Method);

        // SGIOC008: Analyze Factory and Instance members specified via nameof()
        // Using RegisterSyntaxNodeAction to avoid RS1030 warning (do not use Compilation.GetSemanticModel)
        context.RegisterSyntaxNodeAction(ctx => AnalyzeFactoryAndInstanceOnAttribute(ctx, analyzerContext), SyntaxKind.Attribute);

        // Analyze assembly-level IoCRegisterFor attributes using SemanticModelAction for IDE squiggles
        context.RegisterSemanticModelAction(ctx => AnalyzeAssemblyLevelRegistrations(ctx, analyzerContext, assemblyAttributeSyntaxTrees));

        // Second pass: analyze dependencies after all services are collected (SGIOC002, SGIOC003-005)
        context.RegisterCompilationEndAction(ctx => AnalyzeAllDependencies(ctx, analyzerContext));
    }

    /// <summary>
    /// SGIOC012: Analyzes IoCRegisterDefaultsAttribute on types (class, struct, interface).
    /// Reports warning when the same target type has multiple default settings.
    /// </summary>
    private static void AnalyzeTypeLevelDefaultsAttribute(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Skip if no IoCRegisterDefaultsAttribute is available
        if(analyzerContext.IoCRegisterDefaultsAttribute is null && analyzerContext.IoCRegisterDefaultsAttribute_T1 is null)
            return;

        var comparer = SymbolEqualityComparer.Default;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            // For generic types, get the original unbound definition for comparison
            var typeToCompare = attributeClass.IsGenericType ? attributeClass.OriginalDefinition : attributeClass;

            // Check if this is an IoCRegisterDefaultsAttribute (non-generic or generic)
            if(!comparer.Equals(typeToCompare, analyzerContext.IoCRegisterDefaultsAttribute)
                && !comparer.Equals(typeToCompare, analyzerContext.IoCRegisterDefaultsAttribute_T1))
            {
                continue;
            }

            // Extract default settings to get target type name
            var settings = attributeClass.IsGenericType
                ? attribute.ExtractDefaultSettingsFromGenericAttribute()
                : attribute.ExtractDefaultSettings();

            if(settings is null)
                continue;

            var targetTypeName = settings.TargetServiceType.Name;
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();

            // SGIOC012: Check for duplicated target type (shared with assembly-level)
            if(!analyzerContext.SeenDefaultTargetTypes.TryAdd(targetTypeName, location))
            {
                // Report immediately for type-level attributes
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicatedDefaultSettings,
                    location,
                    targetTypeName));
            }
        }
    }

    /// <summary>
    /// SGIOC006: Analyzes parameters for duplicated keyed service attributes.
    /// Reports warning when both [FromKeyedServices] and [Inject] attributes are marked on the same parameter.
    /// </summary>
    private static void AnalyzeDuplicatedKeyedServiceAttributes(SymbolAnalysisContext context)
    {
        if(context.Symbol is not IMethodSymbol method)
            return;

        foreach(var parameter in method.Parameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var hasFromKeyedServices = false;
            var hasInject = false;
            AttributeData? injectAttribute = null;

            foreach(var attr in parameter.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if(attrClass is null)
                    continue;

                if(attrClass.Name == "FromKeyedServicesAttribute"
                    && attrClass.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection")
                {
                    hasFromKeyedServices = true;
                }
                else if(attrClass.Name == "InjectAttribute")
                {
                    hasInject = true;
                    injectAttribute = attr;
                }
            }

            if(hasFromKeyedServices && hasInject)
            {
                var location = injectAttribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? parameter.Locations.FirstOrDefault();

                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicatedKeyedServiceAttribute,
                    location,
                    parameter.Name));
            }
        }
    }

    /// <summary>
    /// SGIOC007: Analyzes InjectAttribute usage on members.
    /// Reports error when InjectAttribute is marked on static member, inaccessible member, or method that does not return void.
    /// </summary>
    private static void AnalyzeInjectAttribute(SymbolAnalysisContext context)
    {
        var member = context.Symbol;

        // Check if the member has InjectAttribute (by name only, matching TransformRegister behavior)
        var injectAttribute = member.GetAttributes()
            .FirstOrDefault(static attr => attr.AttributeClass?.Name == "InjectAttribute");

        if(injectAttribute is null)
            return;

        var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? member.Locations.FirstOrDefault();

        // Check if member is static
        if(member.IsStatic)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidInjectAttributeUsage,
                location,
                member.Name,
                "it is static"));
            return;
        }

        switch(member)
        {
            case IPropertySymbol property:
                // Check if property has no setter or setter is private
                if(property.SetMethod is null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "property has no setter"));
                }
                else if(property.SetMethod.DeclaredAccessibility is Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "property setter is private"));
                }
                break;

            case IFieldSymbol field:
                // Check if field is readonly
                if(field.IsReadOnly)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "field is readonly"));
                }
                // Check if field is private
                else if(field.DeclaredAccessibility is Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "field is private"));
                }
                break;

            case IMethodSymbol method:
                // Check if method is private
                if(method.DeclaredAccessibility is Accessibility.Private)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "method is private"));
                }
                // Check if method does not return void
                else if(!method.ReturnsVoid)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidInjectAttributeUsage,
                        location,
                        member.Name,
                        "method must return void"));
                }
                break;
        }
    }

    /// <summary>
    /// Analyzes all dependencies after all services have been collected.
    /// This ensures we have a complete picture of all registered services before checking dependencies.
    /// Also reports SGIOC012 for duplicated IoCRegisterDefaults.
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

        foreach(var kvp in analyzerContext.RegisteredServices)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var serviceInfo = kvp.Value;
            AnalyzeDependencies(
                context.ReportDiagnostic,
                analyzerContext,
                serviceInfo.Type,
                serviceInfo.Lifetime,
                serviceInfo.Location,
                visited,
                pathStack,
                context.CancellationToken);
        }
    }

    private static ImmutableHashSet<SyntaxTree> CollectAssemblyLevelRegistrations(
        Compilation compilation,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var syntaxTreesBuilder = ImmutableHashSet.CreateBuilder<SyntaxTree>();

        // Check if any IoCRegisterForAttribute variant is available
        if(analyzerContext.IoCRegisterForAttribute is null && analyzerContext.IoCRegisterForAttribute_T1 is null)
            return syntaxTreesBuilder.ToImmutable();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            // Check if this is an IoCRegisterForAttribute (non-generic or generic)
            if(!IsIoCRegisterForAttribute(attributeClass, analyzerContext))
                continue;

            // Track which syntax tree contains this attribute
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference?.SyntaxTree is { } syntaxTree)
            {
                syntaxTreesBuilder.Add(syntaxTree);
            }

            // Get target type from attribute (constructor arg for non-generic, type parameter for generic)
            var targetType = GetTargetTypeFromAttribute(attribute);
            if(targetType is null)
                continue;

            // Skip invalid types
            if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if(targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            var location = syntaxReference?.GetSyntax(cancellationToken).GetLocation();

            var (hasExplicitLifetime, explicitLifetime) = attribute.TryGetLifetime();
            var lifetime = GetEffectiveLifetime(analyzerContext, targetType, hasExplicitLifetime, explicitLifetime);

            RegisterServiceWithIndex(analyzerContext, targetType, lifetime, location);
        }

        return syntaxTreesBuilder.ToImmutable();
    }

    private static void AnalyzeAssemblyLevelRegistrations(
        SemanticModelAnalysisContext context,
        AnalyzerContext analyzerContext,
        ImmutableHashSet<SyntaxTree> assemblyAttributeSyntaxTrees)
    {
        // Check if any IoCRegisterForAttribute variant is available
        if(analyzerContext.IoCRegisterForAttribute is null && analyzerContext.IoCRegisterForAttribute_T1 is null)
            return;

        // Only analyze if this syntax tree contains assembly-level attributes
        if(!assemblyAttributeSyntaxTrees.Contains(context.SemanticModel.SyntaxTree))
            return;

        foreach(var attribute in context.SemanticModel.Compilation.Assembly.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Only process attributes from the current syntax tree
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference?.SyntaxTree != context.SemanticModel.SyntaxTree)
                continue;

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            // Check if this is an IoCRegisterForAttribute (non-generic or generic)
            if(!IsIoCRegisterForAttribute(attributeClass, analyzerContext))
                continue;

            // Get target type from attribute (constructor arg for non-generic, type parameter for generic)
            var targetType = GetTargetTypeFromAttribute(attribute);
            if(targetType is null)
                continue;

            var location = syntaxReference.GetSyntax(context.CancellationToken).GetLocation();

            // Pre-compute fully qualified type name for SGIOC011 check
            var fullyQualifiedTypeName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // SGIOC001: Check if target type is private or abstract
            AnalyzeInvalidAttributeUsage(context, targetType, location);

            // SGIOC009: Check Instance requires Singleton lifetime
            AnalyzeInstanceLifetime(context.ReportDiagnostic, attribute, location);

            // SGIOC011: Check for duplicated registrations (same implementation type and key)
            AnalyzeDuplicatedRegistration(context.ReportDiagnostic, analyzerContext, attribute, targetType, fullyQualifiedTypeName, location);
        }
    }

    /// <summary>
    /// First pass: collect services and do immediate validation (SGIOC001).
    /// Dependency analysis (SGIOC002, SGIOC003-005) is deferred to CompilationEnd.
    /// </summary>
    private static void CollectAndValidateNamedType(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if(!TryGetIoCAttribute(attribute, analyzerContext, out var isIoCRegisterFor))
                continue;

            INamedTypeSymbol targetType;

            if(isIoCRegisterFor)
            {
                // Use helper method to get target type (supports both generic and non-generic variants)
                var target = GetTargetTypeFromAttribute(attribute);
                if(target is null)
                    continue;

                targetType = target;
            }
            else
            {
                targetType = typeSymbol;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();

            // Pre-compute fully qualified type name for SGIOC011 check
            var fullyQualifiedTypeName = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // SGIOC001: Check if target type is private or abstract
            AnalyzeInvalidAttributeUsage(context, targetType, location);

            // SGIOC009: Check Instance requires Singleton lifetime
            AnalyzeInstanceLifetime(context.ReportDiagnostic, attribute, location);

            // SGIOC011: Check for duplicated registrations (same implementation type and key)
            AnalyzeDuplicatedRegistration(context.ReportDiagnostic, analyzerContext, attribute, targetType, fullyQualifiedTypeName, location);

            // Skip registration if type is invalid
            if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if(targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            // Get lifetime of current service (considering default settings)
            var (hasExplicitLifetime, explicitLifetime) = attribute.TryGetLifetime();
            var currentLifetime = GetEffectiveLifetime(analyzerContext, targetType, hasExplicitLifetime, explicitLifetime);

            // Register service with index for faster lookup
            // Dependency analysis will be done in CompilationEnd after all services are collected
            RegisterServiceWithIndex(analyzerContext, targetType, currentLifetime, location);
        }
    }

    /// <summary>
    /// Checks if the attribute is an IoC registration attribute and returns which type.
    /// Supports both non-generic and generic variants.
    /// </summary>
    private static bool TryGetIoCAttribute(AttributeData attribute, AnalyzerContext analyzerContext, out bool isIoCRegisterFor)
    {
        isIoCRegisterFor = false;
        var attributeClass = attribute.AttributeClass;
        if(attributeClass is null)
            return false;

        // For generic types, get the original unbound definition for comparison
        var typeToCompare = attributeClass.IsGenericType ? attributeClass.OriginalDefinition : attributeClass;

        var comparer = SymbolEqualityComparer.Default;

        // Check IoCRegisterAttribute variants (non-generic and generic)
        if(comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T1)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T2)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T3)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T4))
        {
            return true;
        }

        // Check IoCRegisterForAttribute variants (non-generic and generic)
        if(comparer.Equals(typeToCompare, analyzerContext.IoCRegisterForAttribute)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterForAttribute_T1))
        {
            isIoCRegisterFor = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the attribute class is any IoCRegisterForAttribute variant (non-generic or generic).
    /// </summary>
    private static bool IsIoCRegisterForAttribute(INamedTypeSymbol attributeClass, AnalyzerContext analyzerContext)
    {
        // For generic types, get the original unbound definition for comparison
        var typeToCompare = attributeClass.IsGenericType ? attributeClass.OriginalDefinition : attributeClass;
        var comparer = SymbolEqualityComparer.Default;

        return comparer.Equals(typeToCompare, analyzerContext.IoCRegisterForAttribute)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterForAttribute_T1);
    }

    /// <summary>
    /// Checks if the attribute class is any IoC registration attribute variant (IoCRegisterAttribute or IoCRegisterForAttribute).
    /// </summary>
    private static bool IsIoCRegistrationAttribute(INamedTypeSymbol attributeClass, AnalyzerContext analyzerContext)
    {
        // For generic types, get the original unbound definition for comparison
        var typeToCompare = attributeClass.IsGenericType ? attributeClass.OriginalDefinition : attributeClass;
        var comparer = SymbolEqualityComparer.Default;

        // Check IoCRegisterAttribute variants
        if(comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T1)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T2)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T3)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterAttribute_T4))
        {
            return true;
        }

        // Check IoCRegisterForAttribute variants
        return comparer.Equals(typeToCompare, analyzerContext.IoCRegisterForAttribute)
            || comparer.Equals(typeToCompare, analyzerContext.IoCRegisterForAttribute_T1);
    }

    /// <summary>
    /// Gets the target type from an IoCRegisterForAttribute.
    /// For non-generic variant, extracts from constructor argument.
    /// For generic variant (IoCRegisterForAttribute&lt;T&gt;), extracts from type parameter.
    /// </summary>
    private static INamedTypeSymbol? GetTargetTypeFromAttribute(AttributeData attribute)
    {
        var attributeClass = attribute.AttributeClass;
        if(attributeClass is null)
            return null;

        // For generic IoCRegisterForAttribute<T>, get T from type arguments
        if(attributeClass.IsGenericType && attributeClass.TypeArguments.Length > 0)
        {
            return attributeClass.TypeArguments[0] as INamedTypeSymbol;
        }

        // For non-generic IoCRegisterForAttribute, get from constructor argument
        if(attribute.ConstructorArguments.Length > 0 &&
           attribute.ConstructorArguments[0].Value is INamedTypeSymbol targetType)
        {
            return targetType;
        }

        return null;
    }

    /// <summary>
    /// Registers a service and builds the service type index for fast lookups.
    /// </summary>
    private static void RegisterServiceWithIndex(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime lifetime,
        Location? location)
    {
        var serviceInfo = new ServiceInfo(targetType, lifetime, location);

        if(!analyzerContext.RegisteredServices.TryAdd(targetType, serviceInfo))
            return; // Already registered

        // Build index for interfaces
        foreach(var iface in targetType.AllInterfaces)
        {
            analyzerContext.ServiceTypeIndex.TryAdd(iface, serviceInfo);
        }

        // Build index for base classes
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            analyzerContext.ServiceTypeIndex.TryAdd(baseType, serviceInfo);
            baseType = baseType.BaseType;
        }
    }

    private static void AnalyzeInvalidAttributeUsage(SymbolAnalysisContext context, INamedTypeSymbol targetType, Location? location)
        => AnalyzeInvalidAttributeUsage(context.ReportDiagnostic, targetType, location);

    private static void AnalyzeInvalidAttributeUsage(SemanticModelAnalysisContext context, INamedTypeSymbol targetType, Location? location)
        => AnalyzeInvalidAttributeUsage(context.ReportDiagnostic, targetType, location);

    private static void AnalyzeInvalidAttributeUsage(Action<Diagnostic> reportDiagnostic, INamedTypeSymbol targetType, Location? location)
    {
        // Check if target type is private
        if(targetType.DeclaredAccessibility is Accessibility.Private)
        {
            var diagnostic = Diagnostic.Create(
                InvalidAttributeUsage,
                location,
                targetType.Name,
                "private");
            reportDiagnostic(diagnostic);
        }

        // Check if target type is abstract
        if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
        {
            var diagnostic = Diagnostic.Create(
                InvalidAttributeUsage,
                location,
                targetType.Name,
                "abstract");
            reportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// SGIOC011: Analyzes for duplicated registrations (same implementation type and key).
    /// Reports warning when the same (ImplementationType, Key) combination is registered multiple times.
    /// </summary>
    private static void AnalyzeDuplicatedRegistration(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        string fullyQualifiedTypeName,
        Location? location)
    {
        // Get the registration key from the attribute
        var (key, _) = attribute.GetKey();

        // Create a unique key for this registration: fully qualified type name + key
        var registrationKey = (fullyQualifiedTypeName, key);

        // Try to add; if already exists, report duplicate
        if(!analyzerContext.RegistrationKeys.TryAdd(registrationKey, location))
        {
            var keyPart = key is not null ? $" with key '{key}'" : "";
            reportDiagnostic(Diagnostic.Create(
                DuplicatedRegistration,
                location,
                targetType.Name,
                keyPart));
        }
    }

    /// <summary>
    /// SGIOC008: Analyzes Factory and Instance members specified via nameof() on AttributeSyntax.
    /// This uses RegisterSyntaxNodeAction to get the SemanticModel directly, avoiding RS1030.
    /// </summary>
    private static void AnalyzeFactoryAndInstanceOnAttribute(
        SyntaxNodeAnalysisContext context,
        AnalyzerContext analyzerContext)
    {
        if(context.Node is not AttributeSyntax attributeSyntax)
            return;

        // Get the attribute symbol to check if it's an IoC registration attribute
        var attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol;
        if(attributeSymbol is not IMethodSymbol attributeConstructor)
            return;

        var attributeClass = attributeConstructor.ContainingType;
        if(attributeClass is null)
            return;

        // Check if this is an IoC registration attribute (including generic variants)
        if(!IsIoCRegistrationAttribute(attributeClass, analyzerContext))
            return;

        var argumentList = attributeSyntax.ArgumentList;
        if(argumentList is null)
            return;

        var location = attributeSyntax.GetLocation();

        // SGIOC010: Check if both Factory and Instance are specified
        AnalyzeFactoryAndInstanceConflict(context, argumentList, location);

        // Check Factory member
        AnalyzeNameofMemberOnSyntax(context, argumentList, location, "Factory");

        // Check Instance member
        AnalyzeNameofMemberOnSyntax(context, argumentList, location, "Instance");
    }

    /// <summary>
    /// SGIOC010: Analyzes if both Factory and Instance are specified on the same attribute.
    /// Reports error when both are present, as Factory takes precedence and Instance will be ignored.
    /// </summary>
    private static void AnalyzeFactoryAndInstanceConflict(
        SyntaxNodeAnalysisContext context,
        AttributeArgumentListSyntax argumentList,
        Location location)
    {
        var hasFactory = false;
        var hasInstance = false;

        foreach(var argument in argumentList.Arguments)
        {
            var name = argument.NameEquals?.Name.Identifier.Text;
            if(name == "Factory")
                hasFactory = true;
            else if(name == "Instance")
                hasInstance = true;

            // Early exit if both found
            if(hasFactory && hasInstance)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FactoryAndInstanceConflict,
                    location));
                return;
            }
        }
    }

    /// <summary>
    /// Analyzes a specific member (Factory or Instance) specified via nameof() using SyntaxNodeAnalysisContext.
    /// </summary>
    private static void AnalyzeNameofMemberOnSyntax(
        SyntaxNodeAnalysisContext context,
        AttributeArgumentListSyntax argumentList,
        Location location,
        string memberKind)
    {
        foreach(var argument in argumentList.Arguments)
        {
            // Check if this is the named argument we're looking for
            if(argument.NameEquals?.Name.Identifier.Text != memberKind)
                continue;

            // Check if the expression is a nameof() invocation
            if(argument.Expression is not InvocationExpressionSyntax invocation ||
               invocation.Expression is not IdentifierNameSyntax identifierName ||
               identifierName.Identifier.Text != "nameof")
            {
                // Not a nameof() expression, skip validation
                return;
            }

            // Extract the argument inside nameof()
            if(invocation.ArgumentList.Arguments.Count != 1)
                return;

            var nameofArgument = invocation.ArgumentList.Arguments[0].Expression;

            // Resolve the symbol using the SemanticModel from context
            var symbolInfo = context.SemanticModel.GetSymbolInfo(nameofArgument, context.CancellationToken);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            if(symbol is null)
                return;

            // Validate the symbol
            var (isValid, errorReason) = ValidateFactoryOrInstanceSymbol(symbol);
            if(!isValid)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidFactoryOrInstanceMember,
                    location,
                    memberKind,
                    symbol.Name,
                    errorReason));
            }

            return;
        }
    }

    /// <summary>
    /// Validates that a symbol referenced by Factory or Instance via nameof() is valid.
    /// </summary>
    /// <returns>A tuple indicating whether the symbol is valid and the error reason if not.</returns>
    private static (bool IsValid, string? ErrorReason) ValidateFactoryOrInstanceSymbol(ISymbol symbol)
    {
        // Check if the symbol is static
        if(!symbol.IsStatic)
        {
            return (false, "not static");
        }

        // Check accessibility - must be at least internal to be accessible
        // Private members cannot be accessed from the generated code
        switch(symbol.DeclaredAccessibility)
        {
            case Accessibility.Private:
                return (false, "private");
            case Accessibility.ProtectedAndInternal:
            case Accessibility.Protected:
                // Protected members are only accessible in derived classes
                // Since generated code is not a derived class, treat as inaccessible
                return (false, "protected and not accessible from generated code");
        }

        // Also check containing type accessibility
        var containingType = symbol.ContainingType;
        while(containingType is not null)
        {
            if(containingType.DeclaredAccessibility is Accessibility.Private)
            {
                return (false, "declared in a private type");
            }
            containingType = containingType.ContainingType;
        }

        return (true, null);
    }

    /// <summary>
    /// SGIOC009: Analyzes Instance registration to ensure Lifetime is Singleton.
    /// Reports error when Instance is specified but Lifetime is not Singleton.
    /// </summary>
    /// <param name="reportDiagnostic">The action to report diagnostics.</param>
    /// <param name="attribute">The IoC registration attribute.</param>
    /// <param name="location">The location for the diagnostic.</param>
    private static void AnalyzeInstanceLifetime(
        Action<Diagnostic> reportDiagnostic,
        AttributeData attribute,
        Location? location)
    {
        // Check if Instance is specified
        string? instance = null;
        foreach(var namedArg in attribute.NamedArguments)
        {
            if(namedArg.Key == "Instance" && !namedArg.Value.IsNull)
            {
                instance = namedArg.Value.Value?.ToString();
                break;
            }
        }

        if(instance is null)
            return;

        // Get the lifetime
        var (hasLifetime, lifetime) = attribute.TryGetLifetime();

        // If lifetime is not explicitly set, it defaults to Singleton (0), which is valid
        if(!hasLifetime)
            return;

        // If lifetime is Singleton, it's valid
        if(lifetime is ServiceLifetime.Singleton)
            return;

        // Report error: Instance requires Singleton lifetime
        reportDiagnostic(Diagnostic.Create(
            InstanceRequiresSingleton,
            location,
            instance,
            lifetime.Name));
    }

    private static void AnalyzeDependencies(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime currentLifetime,
        Location? location,
        HashSet<INamedTypeSymbol> visited,
        Stack<INamedTypeSymbol> pathStack,
        CancellationToken cancellationToken)
    {
        var constructor = targetType.PrimaryOrMostParametersConstructor;
        if(constructor is null)
            return;

        foreach(var parameter in constructor.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(parameter.Type is not INamedTypeSymbol parameterType)
                continue;

            // Find the dependency's implementation type and lifetime using index
            var dependencyInfo = FindRegisteredDependency(analyzerContext, parameterType);
            if(dependencyInfo is null)
                continue;

            // SGIOC002: Check for circular dependency (reuse visited set and path stack)
            visited.Clear();
            pathStack.Clear();
            if(DetectCircularDependency(analyzerContext, targetType, dependencyInfo.Type, visited, pathStack))
            {
                // Build cycle string from stack (already in correct order: current -> ... -> start)
                var cycleString = BuildCycleString(pathStack);
                reportDiagnostic(Diagnostic.Create(
                    CircularDependency,
                    location,
                    cycleString));
            }

            // SGIOC003-005: Check for lifetime conflict
            var lifetimeConflictDescriptor = GetLifetimeConflictDescriptor(currentLifetime, dependencyInfo.Lifetime);
            if(lifetimeConflictDescriptor is not null)
            {
                reportDiagnostic(Diagnostic.Create(
                    lifetimeConflictDescriptor,
                    location,
                    targetType.Name,
                    dependencyInfo.Type.Name));
            }
        }
    }

    /// <summary>
    /// Builds the cycle string from the path stack without allocating intermediate collections.
    /// </summary>
    private static string BuildCycleString(Stack<INamedTypeSymbol> pathStack)
    {
        // Stack is LIFO, so we need to reverse for correct order
        StringBuilder sb = new(pathStack.Count * 2 - 1);
        foreach(var (i, item) in pathStack.Reverse().Index())
        {
            if(i > 0)
                sb.Append(" -> ");
            sb.Append(item.Name);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Finds a registered dependency by parameter type using O(1) index lookups.
    /// This method is called during CompilationEnd when all services are fully indexed.
    /// </summary>
    private static ServiceInfo? FindRegisteredDependency(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol parameterType)
    {
        // Direct match - O(1)
        if(analyzerContext.RegisteredServices.TryGetValue(parameterType, out var directMatch))
            return directMatch;

        // Try open generic match for direct type (e.g., TestOpenGeneric2<int> -> TestOpenGeneric2<T>)
        if(parameterType.IsGenericType && !parameterType.IsUnboundGenericType)
        {
            var originalDefinition = parameterType.OriginalDefinition;
            if(analyzerContext.RegisteredServices.TryGetValue(originalDefinition, out var genericMatch))
                return genericMatch;
        }

        // Use index for interface/base class lookup - O(1)
        if(analyzerContext.ServiceTypeIndex.TryGetValue(parameterType, out var indexedMatch))
            return indexedMatch;

        // Try open generic match for indexed lookup
        if(parameterType.IsGenericType && !parameterType.IsUnboundGenericType)
        {
            var originalDefinition = parameterType.OriginalDefinition;
            if(analyzerContext.ServiceTypeIndex.TryGetValue(originalDefinition, out var genericIndexMatch))
                return genericIndexMatch;
        }

        // No fallback needed - this runs during CompilationEnd when index is complete
        return null;
    }

    /// <summary>
    /// Detects circular dependencies using a stack-based approach to avoid List.Insert(0,...) allocations.
    /// Returns true if a cycle is detected, with the path stored in pathStack.
    /// </summary>
    private static bool DetectCircularDependency(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol startType,
        INamedTypeSymbol currentType,
        HashSet<INamedTypeSymbol> visited,
        Stack<INamedTypeSymbol> pathStack)
    {
        if(SymbolEqualityComparer.Default.Equals(startType, currentType))
        {
            pathStack.Push(startType);
            return true;
        }

        if(!visited.Add(currentType))
            return false;

        // Check if current type is registered
        if(!analyzerContext.RegisteredServices.ContainsKey(currentType))
            return false;

        var constructor = currentType.PrimaryOrMostParametersConstructor;
        if(constructor is null)
            return false;

        foreach(var parameter in constructor.Parameters)
        {
            if(parameter.Type is not INamedTypeSymbol parameterType)
                continue;

            var dependency = FindRegisteredDependency(analyzerContext, parameterType);
            if(dependency is null)
                continue;

            if(DetectCircularDependency(analyzerContext, startType, dependency.Type, visited, pathStack))
            {
                pathStack.Push(currentType);
                return true;
            }
        }

        return false;
    }

    private static DiagnosticDescriptor? GetLifetimeConflictDescriptor(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        // SGIOC003: Singleton depending on Scoped is a conflict (captive dependency)
        if(consumerLifetime is ServiceLifetime.Singleton && dependencyLifetime is ServiceLifetime.Scoped)
            return SingletonDependsOnScoped;

        // SGIOC004: Singleton depending on Transient is a captive dependency issue
        // The Transient instance will be captured and live for the application lifetime
        if(consumerLifetime is ServiceLifetime.Singleton && dependencyLifetime is ServiceLifetime.Transient)
            return SingletonDependsOnTransient;

        // SGIOC005: Scoped depending on Transient is a captive dependency issue
        // The Transient instance will be captured for the scope lifetime
        if(consumerLifetime is ServiceLifetime.Scoped && dependencyLifetime is ServiceLifetime.Transient)
            return ScopedDependsOnTransient;

        return null;
    }

    /// <summary>
    /// Collects default settings from IoCRegisterDefaultSettingsAttribute on the assembly.
    /// Uses the shared ExtractDefaultSettings method from Constants.
    /// Also tracks duplicated target types for SGIOC012 reporting.
    /// </summary>
    private static DefaultSettingsMap CollectDefaults(
        Compilation compilation,
        INamedTypeSymbol? iocRegisterDefaultSettingsAttribute,
        INamedTypeSymbol? iocRegisterDefaultSettingsAttribute_T1,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<string, Location?> seenTargetTypes,
        CancellationToken cancellationToken)
    {
        if(iocRegisterDefaultSettingsAttribute is null && iocRegisterDefaultSettingsAttribute_T1 is null)
            return new DefaultSettingsMap([]);

        var settingsBuilder = ImmutableArray.CreateBuilder<DefaultSettingsModel>();
        var comparer = SymbolEqualityComparer.Default;

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            // For generic types, get the original unbound definition for comparison
            var typeToCompare = attributeClass.IsGenericType ? attributeClass.OriginalDefinition : attributeClass;

            // Check if this is an IoCRegisterDefaultsAttribute (non-generic or generic)
            if(!comparer.Equals(typeToCompare, iocRegisterDefaultSettingsAttribute)
                && !comparer.Equals(typeToCompare, iocRegisterDefaultSettingsAttribute_T1))
            {
                continue;
            }

            // Use shared method to extract default settings
            // Use different extraction method for generic vs non-generic attributes
            var settings = attributeClass.IsGenericType
                ? attribute.ExtractDefaultSettingsFromGenericAttribute()
                : attribute.ExtractDefaultSettings();
            if(settings is not null)
            {
                var targetTypeName = settings.TargetServiceType.Name;

                // SGIOC012: Check for duplicated target type
                if(!seenTargetTypes.TryAdd(targetTypeName, attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()))
                {
                    var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                    duplicatedDefaults.Add((targetTypeName, location));
                }
                else
                {
                    settingsBuilder.Add(settings);
                }
            }
        }

        return new DefaultSettingsMap(settingsBuilder.ToImmutable());
    }

    /// <summary>
    /// Gets the effective lifetime for a type, considering default settings.
    /// Uses DefaultSettingsMap for efficient lookups, consistent with RegisterSourceGenerator.
    /// </summary>
    private static ServiceLifetime GetEffectiveLifetime(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        bool hasExplicitLifetime,
        ServiceLifetime explicitLifetime)
    {
        // If lifetime is explicitly set, use it
        if(hasExplicitLifetime)
            return explicitLifetime;

        var defaultSettings = analyzerContext.DefaultSettings;
        if(defaultSettings.IsEmpty)
            return explicitLifetime;

        // Check default settings for matching interfaces
        foreach(var iface in targetType.AllInterfaces)
        {
            var ifaceTypeData = iface.GetTypeData();

            // Try exact match first
            if(defaultSettings.TryGetExactMatches(ifaceTypeData.Name, out var exactIndex))
                return defaultSettings[exactIndex].Lifetime;

            // Try generic match (e.g., IGenericTest<> matches IGenericTest<T>)
            if(iface.IsGenericType && defaultSettings.TryGetGenericMatches(ifaceTypeData.NameWithoutGeneric, ifaceTypeData.GenericArity, out var genericIndex))
                return defaultSettings[genericIndex].Lifetime;
        }

        // Check default settings for matching base classes
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            var baseTypeData = baseType.GetTypeData();

            // Try exact match first
            if(defaultSettings.TryGetExactMatches(baseTypeData.Name, out var exactIndex))
                return defaultSettings[exactIndex].Lifetime;

            // Try generic match for base classes
            if(baseType.IsGenericType && defaultSettings.TryGetGenericMatches(baseTypeData.NameWithoutGeneric, baseTypeData.GenericArity, out var genericIndex))
                return defaultSettings[genericIndex].Lifetime;

            baseType = baseType.BaseType;
        }

        // Default lifetime is Singleton (as defined in TryGetLifetime)
        return explicitLifetime;
    }

    private sealed class AnalyzerContext(
        INamedTypeSymbol? iocRegisterAttribute,
        INamedTypeSymbol? iocRegisterAttribute_T1,
        INamedTypeSymbol? iocRegisterAttribute_T2,
        INamedTypeSymbol? iocRegisterAttribute_T3,
        INamedTypeSymbol? iocRegisterAttribute_T4,
        INamedTypeSymbol? iocRegisterForAttribute,
        INamedTypeSymbol? iocRegisterForAttribute_T1,
        INamedTypeSymbol? iocRegisterDefaultsAttribute,
        INamedTypeSymbol? iocRegisterDefaultsAttribute_T1,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> serviceTypeIndex,
        DefaultSettingsMap defaultSettings,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<string, Location?> seenDefaultTargetTypes)
    {
        public INamedTypeSymbol? IoCRegisterAttribute { get; } = iocRegisterAttribute;
        public INamedTypeSymbol? IoCRegisterAttribute_T1 { get; } = iocRegisterAttribute_T1;
        public INamedTypeSymbol? IoCRegisterAttribute_T2 { get; } = iocRegisterAttribute_T2;
        public INamedTypeSymbol? IoCRegisterAttribute_T3 { get; } = iocRegisterAttribute_T3;
        public INamedTypeSymbol? IoCRegisterAttribute_T4 { get; } = iocRegisterAttribute_T4;
        public INamedTypeSymbol? IoCRegisterForAttribute { get; } = iocRegisterForAttribute;
        public INamedTypeSymbol? IoCRegisterForAttribute_T1 { get; } = iocRegisterForAttribute_T1;
        public INamedTypeSymbol? IoCRegisterDefaultsAttribute { get; } = iocRegisterDefaultsAttribute;
        public INamedTypeSymbol? IoCRegisterDefaultsAttribute_T1 { get; } = iocRegisterDefaultsAttribute_T1;
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> RegisteredServices { get; } = registeredServices;
        /// <summary>
        /// Index mapping service types (interfaces/base classes) to their implementations.
        /// Enables O(1) lookup instead of O(n) linear search.
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> ServiceTypeIndex { get; } = serviceTypeIndex;
        /// <summary>
        /// Default settings from IoCRegisterDefaultSettingsAttribute, using DefaultSettingsMap for efficient lookups.
        /// </summary>
        public DefaultSettingsMap DefaultSettings { get; } = defaultSettings;
        /// <summary>
        /// List of duplicated IoCRegisterDefaults for SGIOC012 reporting.
        /// </summary>
        public ConcurrentBag<(string TargetTypeName, Location? Location)> DuplicatedDefaults { get; } = duplicatedDefaults;
        /// <summary>
        /// Tracks seen IoCRegisterDefaults target types across assembly and type-level attributes (SGIOC012).
        /// </summary>
        public ConcurrentDictionary<string, Location?> SeenDefaultTargetTypes { get; } = seenDefaultTargetTypes;

        /// <summary>
        /// Tracks registered (ImplementationType, Key) pairs to detect duplicates (SGIOC011).
        /// Key is the fully qualified type name combined with the registration key.
        /// Value is the first registration location for diagnostic reporting.
        /// </summary>
        public ConcurrentDictionary<(string TypeName, string? Key), Location?> RegistrationKeys { get; } = [];
    }

    private sealed record ServiceInfo
    {
        public INamedTypeSymbol Type { get; }
        public ServiceLifetime Lifetime { get; }
        public Location? Location { get; }
        /// <summary>
        /// Pre-computed fully qualified type name to avoid repeated ToDisplayString calls.
        /// </summary>
        public string FullyQualifiedName { get; }

        public ServiceInfo(INamedTypeSymbol type, ServiceLifetime lifetime, Location? location)
        {
            Type = type;
            Lifetime = lifetime;
            Location = location;
            FullyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
    }
}
