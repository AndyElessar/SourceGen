using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Analyzer for IoC container attributes.
/// Reports diagnostics for container-level issues such as missing partial modifier and unresolvable dependencies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContainerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// SGIOC018: Unable to resolve service - A dependency cannot be resolved when IntegrateServiceProvider = false.
    /// </summary>
    public static readonly DiagnosticDescriptor UnableToResolveService = new(
        id: "SGIOC018",
        title: "Unable to resolve service",
        messageFormat: "Unable to resolve service '{0}' for container '{1}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When IntegrateServiceProvider is false, all dependencies must be registered in the container. No fallback to external IServiceProvider is available.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC019: Container class must be partial and cannot be static - The class marked with [IocContainer] is missing the partial modifier or is static.
    /// </summary>
    public static readonly DiagnosticDescriptor ContainerMustBePartialAndNotStatic = new(
        id: "SGIOC019",
        title: "Container class must be partial and cannot be static",
        messageFormat: "Container class '{0}' must be declared as partial and cannot be static",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A class marked with [IocContainer] must be declared as partial to allow source generation and cannot be static.");

    /// <summary>
    /// SGIOC020: UseSwitchStatement should not be true when importing modules - The setting is ignored when there are imported modules.
    /// </summary>
    public static readonly DiagnosticDescriptor UseSwitchStatementIgnoredWithImportedModules = new(
        id: "SGIOC020",
        title: "UseSwitchStatement is ignored when importing modules",
        messageFormat: "Container '{0}' specifies UseSwitchStatement = true but has imported modules; the setting will be ignored and FrozenDictionary will be used instead",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When a container has one or more [IocImportModule] attributes, UseSwitchStatement is ignored and FrozenDictionary is always used for service resolution.");

    /// <summary>
    /// SGIOC021: Unable to resolve partial accessor service - A partial method/property's return type cannot be resolved when IntegrateServiceProvider = false.
    /// </summary>
    public static readonly DiagnosticDescriptor UnableToResolvePartialAccessor = new(
        id: "SGIOC021",
        title: "Unable to resolve partial accessor service",
        messageFormat: "Unable to resolve service '{0}' for partial accessor '{1}' in container '{2}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When IntegrateServiceProvider is false, the return type of a partial method or property accessor must be a registered service. No fallback to external IServiceProvider is available.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC025: Circular module import detected - A container has a circular [IocImportModule] dependency.
    /// </summary>
    public static readonly DiagnosticDescriptor CircularModuleImport = new(
        id: "SGIOC025",
        title: "Circular module import detected",
        messageFormat: "Container '{0}' has a circular module import dependency: {1}",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Circular module imports create static initializer deadlocks. Remove the circular dependency.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC027: Partial accessor must return Task&lt;T&gt; for an async-init service.
    /// </summary>
    public static readonly DiagnosticDescriptor PartialAccessorMustReturnTask = new(
        id: "SGIOC027",
        title: "Partial accessor must return Task<T> for async-init service",
        messageFormat: "Partial accessor '{0}' returns '{1}' but the implementation has async inject methods. Use 'Task<{1}>'.",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a registered implementation has async inject methods (methods returning Task decorated with [IocInject]), partial accessors targeting that service must return Task<TService> so the generator can emit an awaitable resolver.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    /// <summary>
    /// SGIOC029: Unsupported async partial accessor type.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedAsyncPartialAccessorType = new(
        id: "SGIOC029",
        title: "Unsupported async partial accessor type",
        messageFormat: "Partial accessor '{0}' returns '{1}' which is not a supported async type. Only 'Task<T>' is supported.",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When an async-init service is targeted by a partial accessor, only Task<TService> is a valid return type. Other wrapper types (Lazy<T>, Func<T>, ValueTask<T>, collections, etc.) and nested wrapper shapes are not supported.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    private static readonly SymbolDisplayFormat s_qualifiedFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        UnableToResolveService,
        ContainerMustBePartialAndNotStatic,
        UseSwitchStatementIgnoredWithImportedModules,
        UnableToResolvePartialAccessor,
        CircularModuleImport,
        PartialAccessorMustReturnTask,
        UnsupportedAsyncPartialAccessorType
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var features = ParseIocFeatures(context.Options);
        var attributeSymbols = new IocAttributeSymbols(context.Compilation);

        if(!attributeSymbols.HasContainerAttribute)
            return;

        // Collect registered services for SGIOC018 analysis
        var registeredServiceTypes = new ConcurrentDictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);

        // Maps (service type, key) -> implementation type for async partial accessor validation.
        var serviceImplementationTypes = new ConcurrentDictionary<(INamedTypeSymbol ServiceType, string? Key), INamedTypeSymbol>(AnalyzerHelpers.ServiceTypeAndKeyComparer);

        // Tracks every registration entry so async-init checks can evaluate all implementations for a (service type, key) pair.
        var registrations = new ConcurrentBag<ServiceRegistration>();

        // Collect containers with IntegrateServiceProvider = false for SGIOC018 analysis
        var containersWithNoFallback = new ConcurrentBag<INamedTypeSymbol>();

        // Collect all [IocContainer] types for SGIOC027/029 analysis
        var allContainers = new ConcurrentBag<INamedTypeSymbol>();

        // Collect import edges for SGIOC025 circular import analysis
        var importEdges = new ConcurrentBag<(INamedTypeSymbol Container, INamedTypeSymbol Module)>();

        var analyzerContext = new ContainerAnalyzerContext(
            attributeSymbols,
            registeredServiceTypes,
            serviceImplementationTypes,
            registrations,
            containersWithNoFallback,
            allContainers,
            importEdges,
            features);

        // Collect assembly-level [IocRegisterFor] / [IocRegisterFor<T>] registrations
        CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);

        // SGIOC019: Check for partial modifier and static modifier on container classes
        // Also collect containers with IntegrateServiceProvider = false for SGIOC018
        context.RegisterSymbolAction(ctx => AnalyzeContainerClass(ctx, analyzerContext), SymbolKind.NamedType);

        // Collect all registered services for SGIOC018 analysis
        context.RegisterSymbolAction(ctx => CollectRegisteredServices(ctx, analyzerContext), SymbolKind.NamedType);

        // SGIOC018: Analyze dependencies at compilation end
        context.RegisterCompilationEndAction(ctx => AnalyzeContainerDependencies(ctx, analyzerContext));

        // SGIOC025: Detect circular module import dependencies at compilation end
        context.RegisterCompilationEndAction(ctx => AnalyzeCircularImports(ctx, analyzerContext));
    }

    /// <summary>
    /// Analyzes container classes for SGIOC019 (partial modifier and static modifier) and collects containers for SGIOC018.
    /// </summary>
    private static void AnalyzeContainerClass(SymbolAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Check if the type has IocContainerAttribute
        var containerAttribute = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => AnalyzerHelpers.IsAttributeMatch(attr.AttributeClass, analyzerContext.AttributeSymbols.IocContainerAttribute));

        if(containerAttribute is null)
            return;

        // SGIOC019: Check for partial modifier and static modifier
        foreach(var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            if(syntax is not ClassDeclarationSyntax classDeclaration)
                continue;

            var hasPartial = classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);
            var hasStatic = classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);

            if(!hasPartial || hasStatic)
            {
                var location = containerAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? classDeclaration.Identifier.GetLocation();

                context.ReportDiagnostic(Diagnostic.Create(
                    ContainerMustBePartialAndNotStatic,
                    location,
                    typeSymbol.Name));
            }
        }

        // Collect containers with IntegrateServiceProvider = false for SGIOC018
        var integrateServiceProvider = true;
        var useSwitchStatement = false;
        foreach(var namedArg in containerAttribute.NamedArguments)
        {
            if(namedArg.Key is "IntegrateServiceProvider" && namedArg.Value.Value is bool integrateValue)
            {
                integrateServiceProvider = integrateValue;
            }
            else if(namedArg.Key is "UseSwitchStatement" && namedArg.Value.Value is bool switchValue)
            {
                useSwitchStatement = switchValue;
            }
        }

        if(!integrateServiceProvider)
        {
            analyzerContext.ContainersWithNoFallback.Add(typeSymbol);
        }

        analyzerContext.AllContainers.Add(typeSymbol);

        // SGIOC020: Check for UseSwitchStatement = true with imported modules
        if(useSwitchStatement)
        {
            var hasImportedModules = typeSymbol.GetAttributes()
                .Any(attr => AnalyzerHelpers.IsIocImportModuleAttribute(attr.AttributeClass, analyzerContext.AttributeSymbols));

            if(hasImportedModules)
            {
                var location = containerAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? typeSymbol.Locations.FirstOrDefault();

                if(location is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UseSwitchStatementIgnoredWithImportedModules,
                        location,
                        typeSymbol.Name));
                }
            }
        }

        // Collect import edges for SGIOC025 circular import detection
        foreach(var attr in typeSymbol.GetAttributes())
        {
            var importedModuleType = GetImportedModuleType(attr, analyzerContext.AttributeSymbols);
            if(importedModuleType is not null)
            {
                analyzerContext.ImportEdges.Add((typeSymbol, importedModuleType));
            }
        }
    }

    /// <summary>
    /// Collects assembly-level [IocRegisterFor] / [IocRegisterFor&lt;T&gt;] registrations into the analyzer context.
    /// </summary>
    private static void CollectAssemblyLevelRegistrations(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        foreach(var (attribute, targetType) in AnalyzerHelpers.EnumerateAssemblyLevelRegisterForAttributes(
            compilation, analyzerContext.AttributeSymbols, cancellationToken))
        {
            RegisterServiceTypes(analyzerContext, targetType, attribute);
        }
    }

    /// <summary>
    /// Collects all registered service types for SGIOC018 analysis.
    /// </summary>
    private static void CollectRegisteredServices(SymbolAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            // Check for IocRegisterAttribute
            if(AnalyzerHelpers.IsIoCRegisterAttribute(attrClass, analyzerContext.AttributeSymbols))
            {
                RegisterServiceTypes(analyzerContext, typeSymbol, attribute);
            }

            // Check for IocRegisterForAttribute
            if(AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, analyzerContext.AttributeSymbols))
            {
                // Get the target implementation type from the attribute
                var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
                if(targetType is not null)
                    RegisterServiceTypes(analyzerContext, targetType, attribute);
            }
        }
    }

    /// <summary>
    /// Analyzes container dependencies for SGIOC018, SGIOC021, SGIOC027, and SGIOC029.
    /// </summary>
    private static void AnalyzeContainerDependencies(CompilationAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        // SGIOC027/029: async-init semantic validation runs on ALL containers
        foreach(var containerSymbol in analyzerContext.AllContainers)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeAsyncPartialAccessors(context, analyzerContext, containerSymbol);
        }

        // SGIOC018/021: registration resolution checks only when IntegrateServiceProvider = false
        foreach(var containerSymbol in analyzerContext.ContainersWithNoFallback)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            AnalyzeContainerServiceDependencies(context, analyzerContext, containerSymbol);
        }
    }

    private static void AnalyzeContainerServiceDependencies(
        CompilationAnalysisContext context,
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol containerSymbol)
    {
        foreach(var kvp in analyzerContext.RegisteredServiceTypes)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var serviceType = kvp.Key;

            // Analyze constructor dependencies
            var constructor = serviceType.SpecifiedOrPrimaryOrMostParametersConstructor;
            if(constructor is not null)
            {
                AnalyzeParameterDependencies(context, analyzerContext, containerSymbol, constructor.Parameters);
            }

            // Analyze injected property dependencies
            foreach(var (member, injectAttribute) in serviceType.GetInjectedMembers())
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                switch(member)
                {
                    case IPropertySymbol property:
                        AnalyzePropertyDependency(context, analyzerContext, containerSymbol, property, injectAttribute);
                        break;

                    case IMethodSymbol method:
                        AnalyzeParameterDependencies(context, analyzerContext, containerSymbol, method.Parameters);
                        break;

                    // IFieldSymbol - analyze field dependencies like properties
                    case IFieldSymbol field:
                        AnalyzeFieldDependency(context, analyzerContext, containerSymbol, field, injectAttribute);
                        break;
                }
            }
        }

        // SGIOC021: Analyze partial accessor return types
        AnalyzePartialAccessorDependencies(context, analyzerContext, containerSymbol);
    }

    /// <summary>
    /// SGIOC027/029: Validates async-init partial accessor return types for ALL containers.
    /// Fires regardless of IntegrateServiceProvider because async-init access requires the correct return type.
    /// </summary>
    private static void AnalyzeAsyncPartialAccessors(
        CompilationAnalysisContext context,
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol containerSymbol)
    {
        foreach(var member in containerSymbol.GetMembers())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if(member.IsStatic)
                continue;

            ITypeSymbol? returnType = null;
            string? memberName = null;

            switch(member)
            {
                case IMethodSymbol method
                    when method.IsPartialDefinition
                         && !method.ReturnsVoid
                         && method.Parameters.Length == 0
                         && !method.IsGenericMethod
                         && method.MethodKind == MethodKind.Ordinary:
                    returnType = method.ReturnType;
                    memberName = method.Name;
                    break;

                case IPropertySymbol property
                    when property.IsPartialDefinition
                         && property.GetMethod is not null:
                    returnType = property.Type;
                    memberName = property.Name;
                    break;
            }

            if(returnType is null || memberName is null)
                continue;

            var serviceKey = AnalyzerHelpers.GetServiceKeyFromMember(member);

            var normalizedReturnType = AnalyzerHelpers.UnwrapNullableValueType(
                returnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

            if(normalizedReturnType is null)
                continue;

            var serviceType = TryGetInnermostServiceType(normalizedReturnType);

            if(serviceType is null)
                continue;

            if(!IsAnyImplementationAsyncInit(analyzerContext, serviceType, serviceKey))
                continue;

            // Async-init service found: validate return type.
            var location = member.Locations.FirstOrDefault();

            // Task<TService> is the only valid return type → no diagnostic
            if(AnalyzerHelpers.IsTaskOfServiceType(normalizedReturnType, serviceType))
                continue;

            // Sync TService return → SGIOC027
            if(normalizedReturnType is INamedTypeSymbol namedReturnType
                && SymbolEqualityComparer.Default.Equals(namedReturnType, serviceType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PartialAccessorMustReturnTask,
                    location,
                    memberName,
                    serviceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                continue;
            }

            // Any other return type (wrappers, ValueTask<T>, collections, nested shapes, etc.) → SGIOC029
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedAsyncPartialAccessorType,
                location,
                memberName,
                normalizedReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
        }
    }

    /// <summary>
    /// SGIOC021: Analyzes partial method/property accessors in a container class to ensure their return types are registered.
    /// Only applies when IntegrateServiceProvider = false.
    /// </summary>
    private static void AnalyzePartialAccessorDependencies(
        CompilationAnalysisContext context,
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol containerSymbol)
    {
        foreach(var member in containerSymbol.GetMembers())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if(member.IsStatic)
                continue;

            ITypeSymbol? returnType = null;
            string? memberName = null;

            switch(member)
            {
                case IMethodSymbol method
                    when method.IsPartialDefinition
                         && !method.ReturnsVoid
                         && method.Parameters.Length == 0
                         && !method.IsGenericMethod
                         && method.MethodKind == MethodKind.Ordinary:
                    returnType = method.ReturnType;
                    memberName = method.Name;
                    break;

                case IPropertySymbol property
                    when property.IsPartialDefinition
                         && property.GetMethod is not null:
                    returnType = property.Type;
                    memberName = property.Name;
                    break;
            }

            if(returnType is null || memberName is null)
                continue;

            var serviceKey = AnalyzerHelpers.GetServiceKeyFromMember(member);

            // Strip nullable annotation for type lookup
            var normalizedReturnType = AnalyzerHelpers.UnwrapNullableValueType(
                returnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
            var isNullable = returnType.NullableAnnotation == NullableAnnotation.Annotated;

            if(normalizedReturnType is null)
                continue;

            var serviceType = GetAccessorServiceType(normalizedReturnType);
            var isGenericValueTaskReturnType = normalizedReturnType.TryGetWrapperInfo(out var returnWrapperInfo)
                && returnWrapperInfo.Kind is WrapperKind.ValueTask;

            // Guard: if the innermost type (ignoring downgrade rules) is an async-init service,
            // all diagnostic reporting is owned by AnalyzeAsyncPartialAccessors (SGIOC027/029).
            // Skip SGIOC021 entirely to prevent double-reporting on downgraded wrapper shapes.
            var innermostType = TryGetInnermostServiceType(normalizedReturnType);
            if(innermostType is not null)
            {
                if(IsAnyImplementationAsyncInit(analyzerContext, innermostType, serviceKey))
                    continue;
            }

            // Report SGIOC021 for unsupported return types (non-generic Task, non-generic ValueTask)
            if(serviceType is null && !isNullable)
            {
                var location = member.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(
                    UnableToResolvePartialAccessor,
                    location,
                    normalizedReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    memberName,
                    containerSymbol.Name));
                continue;
            }

            if(serviceType is not null
                && !isNullable
                && !IsPartialAccessorServiceRegistered(serviceType, serviceKey, analyzerContext))
            {
                var location = member.Locations.FirstOrDefault();
                // For ValueTask<T> (not a generator-supported recursive wrapper), report the full return type
                // because the shape is considered downgraded/unsupported per spec.
                var diagnosticType = isGenericValueTaskReturnType
                    ? normalizedReturnType
                    : serviceType;
                context.ReportDiagnostic(Diagnostic.Create(
                    UnableToResolvePartialAccessor,
                    location,
                    diagnosticType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    memberName,
                    containerSymbol.Name));
            }

            // Report SGIOC021 for ValueTask<T> when the service is registered but not an async-init service.
            // (When it IS async-init, SGIOC029 is reported by AnalyzeAsyncPartialAccessors and we already continued.)
            if(serviceType is not null
                && !isNullable
                && IsPartialAccessorServiceRegistered(serviceType, serviceKey, analyzerContext)
                && isGenericValueTaskReturnType)
            {
                var location = member.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(
                    UnableToResolvePartialAccessor,
                    location,
                    normalizedReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    memberName,
                    containerSymbol.Name));
                continue;
            }
        }
    }

    private static void RegisterServiceTypes(
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol implementationType,
        AttributeData attribute)
    {
        var (serviceKey, _, _) = attribute.GetKeyInfo();

        foreach(var serviceType in AnalyzerHelpers.EnumerateRegisteredServiceTypes(
                     implementationType,
                     attribute))
        {
            analyzerContext.RegisteredServiceTypes.TryAdd(serviceType, true);
            analyzerContext.Registrations.Add(new ServiceRegistration(serviceType, serviceKey, implementationType));
            analyzerContext.ServiceImplementationTypes[(serviceType, serviceKey)] = implementationType;
        }
    }

    private static INamedTypeSymbol? GetAccessorServiceType(ITypeSymbol returnType)
    {
        var normalizedReturnType = AnalyzerHelpers.UnwrapNullableValueType(
            returnType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

        if(normalizedReturnType is null)
            return null;

        // Unsupported types (non-generic Task, non-generic ValueTask) → return null so caller reports SGIOC021
        if(AnalyzerHelpers.IsUnsupportedPartialAccessorReturnType(normalizedReturnType))
            return null;

        // ValueTask<T> is not a generator-supported recursive wrapper; return T directly so callers
        // can check registration and async-init. SGIOC029 / SGIOC021 are reported separately.
        if(normalizedReturnType.TryGetWrapperInfo(out var rootWrapperInfo)
            && rootWrapperInfo.Kind is WrapperKind.ValueTask)
            return rootWrapperInfo.ElementType;

        // Recursively unwrap generator-supported wrappers with downgrade detection.
        // Mirrors TransformExtensions.cs downgrade rules (nested Task shapes, collection-at-top).
        ITypeSymbol current = normalizedReturnType;
        var isAfterCollection = false;

        while(current.TryGetWrapperInfo(out var wrapperInfo)
            && wrapperInfo.ElementType is { } element)
        {
            var elementKind = element.TryGetWrapperInfo(out var elementWrapperInfo)
                ? elementWrapperInfo.Kind
                : WrapperKind.None;

            if(elementKind is not WrapperKind.None
                && IsUnsupportedWrapperNesting(wrapperInfo.Kind, elementKind, isAfterCollection))
                return null;

            if(wrapperInfo.Kind.IsCollectionWrapperKind())
                isAfterCollection = true;

            current = element;
        }

        return current as INamedTypeSymbol;
    }

    /// <summary>
    /// Recursively unwraps all generator-supported wrapper types (without applying downgrade rules) to find
    /// the innermost concrete service type. Used by diagnostic analysis to classify async-init services
    /// regardless of whether the generator would downgrade the shape to IServiceProvider fallback.
    /// </summary>
    private static INamedTypeSymbol? TryGetInnermostServiceType(ITypeSymbol input)
    {
        ITypeSymbol current = input;

        while(current.TryGetWrapperInfo(out var wrapperInfo)
            && wrapperInfo.ElementType is { } element)
        {
            current = element;
        }

        return current as INamedTypeSymbol;
    }

    private static bool IsAnyImplementationAsyncInit(
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol serviceType,
        string? serviceKey)
    {
        foreach(var registration in analyzerContext.Registrations)
        {
            if(!SymbolEqualityComparer.Default.Equals(registration.ServiceType, serviceType))
                continue;

            if(!StringComparer.Ordinal.Equals(registration.Key, serviceKey))
                continue;

            if(AnalyzerHelpers.IsAsyncInitImplementation(registration.ImplementationType, analyzerContext.Features))
                return true;
        }

        return false;
    }

    private static IocFeatures ParseIocFeatures(AnalyzerOptions options)
    {
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var rawFeatures);
        return IocFeaturesHelper.Parse(rawFeatures);
    }

    private static void AnalyzeParameterDependencies(
        CompilationAnalysisContext context,
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol containerSymbol,
        ImmutableArray<IParameterSymbol> parameters)
    {
        foreach(var param in parameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Skip if parameter is always resolvable
            if(AnalyzerHelpers.IsParameterAlwaysResolvable(param))
                continue;

            var paramType = param.Type;

            // Check if the dependency type is registered
            if(paramType is INamedTypeSymbol namedParamType)
            {
                if(!IsServiceRegistered(namedParamType, analyzerContext))
                {
                    var location = param.Locations.FirstOrDefault();
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnableToResolveService,
                        location,
                        paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        containerSymbol.Name));
                }
            }
        }
    }

    private static void AnalyzePropertyDependency(
        CompilationAnalysisContext context,
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol containerSymbol,
        IPropertySymbol property,
        AttributeData injectAttribute)
    {
        // Skip if property is always resolvable
        if(AnalyzerHelpers.IsPropertyAlwaysResolvable(property, injectAttribute))
            return;

        var propertyType = property.Type;

        // Check if the dependency type is registered
        if(propertyType is INamedTypeSymbol namedPropertyType)
        {
            if(!IsServiceRegistered(namedPropertyType, analyzerContext))
            {
                var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? property.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(
                    UnableToResolveService,
                    location,
                    propertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    containerSymbol.Name));
            }
        }
    }

    private static void AnalyzeFieldDependency(
        CompilationAnalysisContext context,
        ContainerAnalyzerContext analyzerContext,
        INamedTypeSymbol containerSymbol,
        IFieldSymbol field,
        AttributeData injectAttribute)
    {
        // Skip if field is always resolvable
        if(AnalyzerHelpers.IsFieldAlwaysResolvable(field, injectAttribute))
            return;

        var fieldType = field.Type;

        // Check if the dependency type is registered
        if(fieldType is INamedTypeSymbol namedFieldType)
        {
            if(!IsServiceRegistered(namedFieldType, analyzerContext))
            {
                var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? field.Locations.FirstOrDefault();
                context.ReportDiagnostic(Diagnostic.Create(
                    UnableToResolveService,
                    location,
                    fieldType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    containerSymbol.Name));
            }
        }
    }

    private static bool IsServiceRegistered(INamedTypeSymbol serviceType, ContainerAnalyzerContext analyzerContext)
    {
        // Direct match
        if(analyzerContext.RegisteredServiceTypes.ContainsKey(serviceType))
            return true;

        // Check if it's always resolvable (well-known types like IServiceProvider)
        if(AnalyzerHelpers.IsAlwaysResolvable(serviceType))
            return true;

        // Handle IEnumerable<T> - check if T is registered
        if(AnalyzerHelpers.IsIEnumerableOfT(serviceType))
        {
            var elementType = AnalyzerHelpers.GetEnumerableElementType(serviceType);
            if(elementType is not null)
            {
                // IEnumerable<T> is resolvable if T is registered
                return analyzerContext.RegisteredServiceTypes.ContainsKey(elementType);
            }
        }

        return false;
    }

    private static bool IsPartialAccessorServiceRegistered(
        INamedTypeSymbol serviceType,
        string? serviceKey,
        ContainerAnalyzerContext analyzerContext)
    {
        if(analyzerContext.ServiceImplementationTypes.ContainsKey((serviceType, serviceKey)))
            return true;

        if(AnalyzerHelpers.IsAlwaysResolvable(serviceType))
            return true;

        if(serviceKey is null && AnalyzerHelpers.IsIEnumerableOfT(serviceType))
        {
            var elementType = AnalyzerHelpers.GetEnumerableElementType(serviceType);
            if(elementType is not null)
                return analyzerContext.RegisteredServiceTypes.ContainsKey(elementType);
        }

        return false;
    }

    /// <summary>
    /// Extracts the imported module type from an [IocImportModule] or [IocImportModule&lt;T&gt;] attribute.
    /// Returns null if the attribute is not an import module attribute or the type cannot be resolved.
    /// </summary>
    private static INamedTypeSymbol? GetImportedModuleType(AttributeData attr, IocAttributeSymbols attributeSymbols)
    {
        var attrClass = attr.AttributeClass;
        if(attrClass is null)
            return null;

        if(!AnalyzerHelpers.IsIocImportModuleAttribute(attrClass, attributeSymbols))
            return null;

        return attr.GetImportedModuleType();
    }

    /// <summary>
    /// SGIOC025: Detects circular [IocImportModule] dependencies using DFS cycle detection.
    /// </summary>
    private static void AnalyzeCircularImports(CompilationAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        if(analyzerContext.ImportEdges.IsEmpty)
            return;

        // Build adjacency list: container → list of imported module types
        var graph = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach(var (container, module) in analyzerContext.ImportEdges)
        {
            if(!graph.TryGetValue(container, out var edges))
            {
                edges = [];
                graph[container] = edges;
            }

            edges.Add(module);
        }

        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var inStack = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reported = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach(var node in graph.Keys)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if(!visited.Contains(node))
            {
                var path = new List<INamedTypeSymbol>();
                DetectCycles(context, graph, node, visited, inStack, reported, path, analyzerContext);
            }
        }
    }

    private static void DetectCycles(
        CompilationAnalysisContext context,
        Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> graph,
        INamedTypeSymbol node,
        HashSet<INamedTypeSymbol> visited,
        HashSet<INamedTypeSymbol> inStack,
        HashSet<INamedTypeSymbol> reported,
        List<INamedTypeSymbol> path,
        ContainerAnalyzerContext analyzerContext)
    {
        visited.Add(node);
        inStack.Add(node);
        path.Add(node);

        if(graph.TryGetValue(node, out var neighbors))
        {
            foreach(var neighbor in neighbors)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if(!visited.Contains(neighbor))
                {
                    DetectCycles(context, graph, neighbor, visited, inStack, reported, path, analyzerContext);
                }
                else if(inStack.Contains(neighbor))
                {
                    // Back-edge found — locate where the cycle starts in the current path
                    var cycleStartIdx = -1;
                    for(var i = 0; i < path.Count; i++)
                    {
                        if(SymbolEqualityComparer.Default.Equals(path[i], neighbor))
                        {
                            cycleStartIdx = i;
                            break;
                        }
                    }

                    if(cycleStartIdx < 0)
                        continue;

                    var cycleStr = string.Join(" → ", path.Skip(cycleStartIdx).Append(neighbor).Select(s => s.ToDisplayString(s_qualifiedFormat)));

                    // Report a diagnostic for every container in the cycle
                    for(var i = cycleStartIdx; i < path.Count; i++)
                    {
                        var containerInCycle = path[i];
                        if(!reported.Add(containerInCycle))
                            continue;

                        var location = GetContainerLocation(containerInCycle);
                        context.ReportDiagnostic(Diagnostic.Create(
                            CircularModuleImport,
                            location,
                            containerInCycle.ToDisplayString(s_qualifiedFormat),
                            cycleStr));
                    }
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
    }

    private static Location? GetContainerLocation(INamedTypeSymbol containerSymbol)
        => containerSymbol.Locations.FirstOrDefault();

    private readonly record struct ServiceRegistration(
        INamedTypeSymbol ServiceType,
        string? Key,
        INamedTypeSymbol ImplementationType);

    private sealed class ContainerAnalyzerContext(
        IocAttributeSymbols attributeSymbols,
        ConcurrentDictionary<INamedTypeSymbol, bool> registeredServiceTypes,
        ConcurrentDictionary<(INamedTypeSymbol ServiceType, string? Key), INamedTypeSymbol> serviceImplementationTypes,
        ConcurrentBag<ServiceRegistration> registrations,
        ConcurrentBag<INamedTypeSymbol> containersWithNoFallback,
        ConcurrentBag<INamedTypeSymbol> allContainers,
        ConcurrentBag<(INamedTypeSymbol Container, INamedTypeSymbol Module)> importEdges,
        IocFeatures features)
    {
        public IocAttributeSymbols AttributeSymbols { get; } = attributeSymbols;
        public ConcurrentDictionary<INamedTypeSymbol, bool> RegisteredServiceTypes { get; } = registeredServiceTypes;
        public ConcurrentDictionary<(INamedTypeSymbol ServiceType, string? Key), INamedTypeSymbol> ServiceImplementationTypes { get; } = serviceImplementationTypes;
        public ConcurrentBag<ServiceRegistration> Registrations { get; } = registrations;
        public ConcurrentBag<INamedTypeSymbol> ContainersWithNoFallback { get; } = containersWithNoFallback;
        public ConcurrentBag<INamedTypeSymbol> AllContainers { get; } = allContainers;
        public ConcurrentBag<(INamedTypeSymbol Container, INamedTypeSymbol Module)> ImportEdges { get; } = importEdges;
        public IocFeatures Features { get; } = features;
    }
}

