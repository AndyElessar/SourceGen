using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Analyzer for IoC container attributes.
/// Reports diagnostics for container-level issues such as missing partial modifier and unresolvable dependencies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class ContainerAnalyzer : DiagnosticAnalyzer
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
    /// SGIOC019: Container class must be partial and cannot be static - The class marked with [IocContainer] is missing the partial modifier, is static, or is a record.
    /// </summary>
    public static readonly DiagnosticDescriptor ContainerMustBePartialAndNotStatic = new(
        id: "SGIOC019",
        title: "Container class must be partial and cannot be static",
        messageFormat: "Container class '{0}' must be declared as partial and cannot be static or a record",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A class marked with [IocContainer] must be declared as partial to allow source generation, cannot be static, and cannot be a record.");

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

    /// <summary>
    /// SGIOC031: Container is not generated because the Container feature is disabled.
    /// </summary>
    public static readonly DiagnosticDescriptor ContainerFeatureDisabled = new(
        id: "SGIOC031",
        title: "Container ignored due to disabled feature",
        messageFormat: "Container '{0}' will not be generated because the Container feature is not enabled. Add 'Container' to <SourceGenIocFeatures> in your project file.",
        category: Constants.Category_Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When SourceGenIocFeatures does not include Container, classes marked with [IocContainer] are ignored during generation.");

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
        UnsupportedAsyncPartialAccessorType,
        ContainerFeatureDisabled
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

        var analyzerContext = new ContainerAnalyzerContext(attributeSymbols, features);

        // Collect assembly-level [IocRegisterFor] registrations and [IocRegisterDefaults] settings
        CollectAssemblyLevelAttributes(context.Compilation, analyzerContext, context.CancellationToken);

        // SGIOC019/020/031 on container classes; collect containers, import edges, registrations,
        // default settings and module imports for the compilation end analysis
        context.RegisterSymbolAction(ctx =>
        {
            AnalyzeContainerClass(ctx, analyzerContext);
            CollectRegistrationAttributes(ctx, analyzerContext);
        }, SymbolKind.NamedType);

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
        AttributeData? containerAttribute = null;
        foreach(var attr in typeSymbol.GetAttributes())
        {
            if(AnalyzerHelpers.IsAttributeMatch(attr.AttributeClass, analyzerContext.AttributeSymbols.IocContainerAttribute))
            {
                containerAttribute = attr;
                break;
            }
        }

        if(containerAttribute is null)
            return;

        // SGIOC031: The generator skips every container when the Container feature is disabled
        if((analyzerContext.Features & IocFeatures.Container) == 0)
        {
            var location = containerAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();

            context.ReportDiagnostic(Diagnostic.Create(
                ContainerFeatureDisabled,
                location,
                typeSymbol.Name));
        }

        // SGIOC019: Check for partial modifier and static modifier.
        // Records are reported too because the generator only handles class declarations.
        foreach(var syntaxRef in typeSymbol.DeclaringSyntaxReferences)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            if(syntax is not (ClassDeclarationSyntax or RecordDeclarationSyntax))
                continue;

            var typeDeclaration = (TypeDeclarationSyntax)syntax;
            var hasPartial = typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);
            var hasStatic = typeDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);
            var isRecord = typeDeclaration is RecordDeclarationSyntax;

            if(!hasPartial || hasStatic || isRecord)
            {
                var location = containerAttribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? typeDeclaration.Identifier.GetLocation();

                context.ReportDiagnostic(Diagnostic.Create(
                    ContainerMustBePartialAndNotStatic,
                    location,
                    typeSymbol.Name));
            }
        }

        // Read the options that select the container's registrations (SGIOC018/021)
        var integrateServiceProvider = true;
        var useSwitchStatement = false;
        var explicitOnly = false;
        ImmutableArray<string> includeTags = [];
        foreach(var namedArg in containerAttribute.NamedArguments)
        {
            switch(namedArg.Key)
            {
                case "IntegrateServiceProvider" when namedArg.Value.Value is bool integrateValue:
                    integrateServiceProvider = integrateValue;
                    break;
                case "UseSwitchStatement" when namedArg.Value.Value is bool switchValue:
                    useSwitchStatement = switchValue;
                    break;
                case "ExplicitOnly" when namedArg.Value.Value is bool explicitValue:
                    explicitOnly = explicitValue;
                    break;
                case "IncludeTags" when !namedArg.Value.IsNull && namedArg.Value.Kind == TypedConstantKind.Array:
                    includeTags = [.. namedArg.Value.Values.Select(static v => v.Value).OfType<string>()];
                    break;
            }
        }

        // Collect import edges for SGIOC025 circular import detection
        var importedModules = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach(var attr in typeSymbol.GetAttributes())
        {
            var importedModuleType = GetImportedModuleType(attr, analyzerContext.AttributeSymbols);
            if(importedModuleType is not null)
            {
                importedModules.Add(importedModuleType);
                analyzerContext.ImportEdges.Add((typeSymbol, importedModuleType));
            }
        }

        analyzerContext.Containers.Add(new ContainerInfo(
            typeSymbol,
            integrateServiceProvider,
            explicitOnly,
            includeTags,
            importedModules.ToImmutable()));

        // SGIOC020: Check for UseSwitchStatement = true with imported modules
        if(useSwitchStatement)
        {
            if(importedModules.Count > 0)
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
    }

    /// <summary>
    /// Analyzes container dependencies for SGIOC018, SGIOC021, SGIOC027, and SGIOC029.
    /// </summary>
    private static void AnalyzeContainerDependencies(CompilationAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        if(analyzerContext.Containers.IsEmpty)
            return;

        var cancellationToken = context.CancellationToken;
        var registrations = ResolveRegistrations(context.Compilation, analyzerContext, cancellationToken);
        var asyncInitServiceKeys = BuildAsyncInitServiceKeys(analyzerContext.Features, registrations, cancellationToken);

        // SGIOC027/029: async-init semantic validation runs on ALL containers
        foreach(var container in analyzerContext.Containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyzeAsyncPartialAccessors(context, asyncInitServiceKeys, container.Symbol);
        }

        // SGIOC018/021: registration resolution checks only when IntegrateServiceProvider = false
        if(!analyzerContext.Containers.Any(static c => !c.IntegrateServiceProvider))
            return;

        var containers = new Dictionary<INamedTypeSymbol, ContainerInfo>(SymbolEqualityComparer.Default);
        foreach(var container in analyzerContext.Containers)
            containers[container.Symbol] = container;

        var scopeBuilder = new ContainerScopeBuilder(registrations, containers, context.Compilation.Assembly);
        var dependencyCache = new DependencyCache();

        // Containers that use every registration of the compilation share the same unresolved dependencies
        List<(Location? Location, string ServiceType)>? sharedUnresolvedDependencies = null;

        foreach(var container in analyzerContext.Containers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(container.IntegrateServiceProvider)
                continue;

            var scope = scopeBuilder.GetScope(container);

            // Registrations of a module from another assembly cannot be enumerated, so its services are unknown
            if(!scope.HasExternalModule)
            {
                var unresolvedDependencies = ContainerScopeBuilder.UsesAllRegistrations(container)
                    ? sharedUnresolvedDependencies ??= CollectUnresolvedDependencies(scope, dependencyCache, cancellationToken)
                    : CollectUnresolvedDependencies(scope, dependencyCache, cancellationToken);

                foreach(var (location, serviceType) in unresolvedDependencies)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UnableToResolveService,
                        location,
                        serviceType,
                        container.Symbol.Name));
                }
            }

            AnalyzePartialAccessorDependencies(context, scope.Own, asyncInitServiceKeys, container.Symbol);
        }
    }

    /// <summary>
    /// Builds the (service type, key) pairs that have at least one async-init implementation (SGIOC021/027/029).
    /// </summary>
    private static HashSet<ServiceSymbolKey> BuildAsyncInitServiceKeys(
        IocFeatures features,
        List<ResolvedRegistration> registrations,
        CancellationToken cancellationToken)
    {
        var asyncInitServiceKeys = new HashSet<ServiceSymbolKey>();
        if((features & IocFeatures.AsyncMethodInject) == 0)
            return asyncInitServiceKeys;

        var isAsyncInitByImplementation = new Dictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
        foreach(var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var implementationType = registration.ImplementationType;
            if(!isAsyncInitByImplementation.TryGetValue(implementationType, out var isAsyncInit))
            {
                isAsyncInit = AnalyzerHelpers.IsAsyncInitImplementation(implementationType, features);
                isAsyncInitByImplementation[implementationType] = isAsyncInit;
            }

            if(isAsyncInit)
            {
                foreach(var serviceType in registration.ServiceTypes)
                    asyncInitServiceKeys.Add(new(serviceType, registration.Key));
            }
        }

        return asyncInitServiceKeys;
    }

    /// <summary>
    /// SGIOC027/029: Validates async-init partial accessor return types for ALL containers.
    /// Fires regardless of IntegrateServiceProvider because async-init access requires the correct return type.
    /// </summary>
    private static void AnalyzeAsyncPartialAccessors(
        CompilationAnalysisContext context,
        HashSet<ServiceSymbolKey> asyncInitServiceKeys,
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

            if(!asyncInitServiceKeys.Contains(new(serviceType, serviceKey)))
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
        RegistrationSet registrations,
        HashSet<ServiceSymbolKey> asyncInitServiceKeys,
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
            if(innermostType is not null && asyncInitServiceKeys.Contains(new(innermostType, serviceKey)))
                continue;

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
                && !IsPartialAccessorServiceRegistered(serviceType, serviceKey, registrations))
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
                && IsPartialAccessorServiceRegistered(serviceType, serviceKey, registrations)
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
        // Mirrors TransformExtensions.TypeData.cs downgrade rules (nested Task shapes, collection-at-top).
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

    private static IocFeatures ParseIocFeatures(AnalyzerOptions options)
    {
        options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(Constants.SourceGenIocFeaturesProperty, out var rawFeatures);
        return IocFeaturesHelper.Parse(rawFeatures);
    }

    private static bool IsPartialAccessorServiceRegistered(
        INamedTypeSymbol serviceType,
        string? serviceKey,
        RegistrationSet registrations)
    {
        if(registrations.ServiceKeys.Contains(new(serviceType, serviceKey)))
            return true;

        if(AnalyzerHelpers.IsAlwaysResolvable(serviceType))
            return true;

        if(serviceKey is null && AnalyzerHelpers.IsIEnumerableOfT(serviceType))
        {
            var elementType = AnalyzerHelpers.GetEnumerableElementType(serviceType);
            if(elementType is not null)
                return registrations.Services.Contains(elementType, key: null);
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
}
