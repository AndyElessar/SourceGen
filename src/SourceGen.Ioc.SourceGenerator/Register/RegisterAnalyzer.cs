using System.Collections.Concurrent;
using System.Text;
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
    /// SGIOC006: Nested Open Generic Detected - Service is implementing interface/class with generic type containing unbound type parameters.
    /// </summary>
    public static readonly DiagnosticDescriptor NestedOpenGeneric = new(
        id: "SGIOC006",
        title: "Nested Open Generic Detected",
        messageFormat: "The type '{0}' implements '{1}' which has a type argument that is itself a generic type with unbound type parameters (e.g., Wrapper<T>). The DI container cannot resolve such nested open generics.",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "When a service implements an interface or inherits from a class where a type argument is itself a generic type containing unbound type parameters (e.g., IHandler<Wrapper<T>>), the DI container cannot resolve it. Use IHandler<T> directly, or register closed generic types like IHandler<Wrapper<string>> instead.");

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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        InvalidAttributeUsage,
        CircularDependency,
        SingletonDependsOnScoped,
        SingletonDependsOnTransient,
        ScopedDependsOnTransient,
        NestedOpenGeneric,
        InvalidInjectAttributeUsage
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
        // Get attribute type symbols for faster lookup
        var iocRegisterAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterAttributeFullName);
        var iocRegisterForAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterForAttributeFullName);
        var iocRegisterDefaultSettingsAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterDefaultsAttributeFullName);

        if(iocRegisterAttribute is null && iocRegisterForAttribute is null)
            return;

        // Use ConcurrentDictionary for thread-safe collection during parallel symbol analysis
        var registeredServices = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Index for service type -> implementation lookup (interfaces/base classes)
        var serviceTypeIndex = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Collect default settings from IoCRegisterDefaultSettingsAttribute using shared method
        var defaultSettings = CollectDefaultSettings(context.Compilation, iocRegisterDefaultSettingsAttribute, context.CancellationToken);

        var analyzerContext = new AnalyzerContext(iocRegisterAttribute, iocRegisterForAttribute, registeredServices, serviceTypeIndex, defaultSettings);

        // Collect assembly-level IoCRegisterFor attributes first (synchronously during compilation start)
        var assemblyAttributeSyntaxTrees = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);

        // First pass: collect services and do immediate validation (SGIOC001, SGIOC006)
        context.RegisterSymbolAction(ctx => CollectAndValidateNamedType(ctx, analyzerContext), SymbolKind.NamedType);

        // SGIOC007: Analyze InjectAttribute on members
        context.RegisterSymbolAction(AnalyzeInjectAttribute, SymbolKind.Property, SymbolKind.Field, SymbolKind.Method);

        // Analyze assembly-level IoCRegisterFor attributes using SemanticModelAction for IDE squiggles
        context.RegisterSemanticModelAction(ctx => AnalyzeAssemblyLevelRegistrations(ctx, analyzerContext, assemblyAttributeSyntaxTrees));

        // Second pass: analyze dependencies after all services are collected (SGIOC002, SGIOC003-005)
        context.RegisterCompilationEndAction(ctx => AnalyzeAllDependencies(ctx, analyzerContext));
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
    /// </summary>
    private static void AnalyzeAllDependencies(CompilationAnalysisContext context, AnalyzerContext analyzerContext)
    {
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

        if(analyzerContext.IoCRegisterForAttribute is null)
            return syntaxTreesBuilder.ToImmutable();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            if(!SymbolEqualityComparer.Default.Equals(attributeClass, analyzerContext.IoCRegisterForAttribute))
                continue;

            // Track which syntax tree contains this attribute
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference?.SyntaxTree is { } syntaxTree)
            {
                syntaxTreesBuilder.Add(syntaxTree);
            }

            if(attribute.ConstructorArguments.Length == 0 ||
               attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetType)
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
        if(analyzerContext.IoCRegisterForAttribute is null)
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

            if(!SymbolEqualityComparer.Default.Equals(attributeClass, analyzerContext.IoCRegisterForAttribute))
                continue;

            if(attribute.ConstructorArguments.Length == 0 ||
               attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetType)
                continue;

            var location = syntaxReference.GetSyntax(context.CancellationToken).GetLocation();

            // SGIOC001: Check if target type is private or abstract
            AnalyzeInvalidAttributeUsage(context, targetType, location);

            // SGIOC006: Check for nested open generic (only when registering interfaces/base classes)
            if(attribute.WillRegisterInterfacesOrBaseClasses())
            {
                AnalyzeNestedOpenGeneric(context, targetType, location);
            }
        }
    }

    /// <summary>
    /// First pass: collect services and do immediate validation (SGIOC001, SGIOC006).
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
                if(attribute.ConstructorArguments.Length == 0 ||
                   attribute.ConstructorArguments[0].Value is not INamedTypeSymbol target)
                    continue;

                targetType = target;
            }
            else
            {
                targetType = typeSymbol;
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();

            // SGIOC001: Check if target type is private or abstract
            AnalyzeInvalidAttributeUsage(context, targetType, location);

            // SGIOC006: Check for nested open generic (only when registering interfaces/base classes)
            if(attribute.WillRegisterInterfacesOrBaseClasses())
            {
                AnalyzeNestedOpenGeneric(context, targetType, location);
            }

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
    /// </summary>
    private static bool TryGetIoCAttribute(AttributeData attribute, AnalyzerContext analyzerContext, out bool isIoCRegisterFor)
    {
        isIoCRegisterFor = false;
        var attributeClass = attribute.AttributeClass;
        if(attributeClass is null)
            return false;

        var comparer = SymbolEqualityComparer.Default;
        if(comparer.Equals(attributeClass, analyzerContext.IoCRegisterAttribute))
            return true;

        if(comparer.Equals(attributeClass, analyzerContext.IoCRegisterForAttribute))
        {
            isIoCRegisterFor = true;
            return true;
        }

        return false;
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

    private static void AnalyzeNestedOpenGeneric(SymbolAnalysisContext context, INamedTypeSymbol targetType, Location? location)
        => AnalyzeNestedOpenGeneric(context.ReportDiagnostic, targetType, location);

    private static void AnalyzeNestedOpenGeneric(SemanticModelAnalysisContext context, INamedTypeSymbol targetType, Location? location)
        => AnalyzeNestedOpenGeneric(context.ReportDiagnostic, targetType, location);

    private static void AnalyzeNestedOpenGeneric(Action<Diagnostic> reportDiagnostic, INamedTypeSymbol targetType, Location? location)
    {
        // Only check open generic types
        if(!targetType.IsGenericType)
            return;

        // For unbound generic types (e.g., from typeof(MyClass<>)), we need to use OriginalDefinition
        // to properly check interfaces and base classes
        var typeToCheck = targetType.IsUnboundGenericType ? targetType.OriginalDefinition : targetType;

        // Check all interfaces for nested open generics
        foreach(var iface in typeToCheck.AllInterfaces)
        {
            if(iface.IsNestedOpenGeneric)
            {
                var diagnostic = Diagnostic.Create(
                    NestedOpenGeneric,
                    location,
                    targetType.Name,
                    iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                reportDiagnostic(diagnostic);
            }
        }

        // Check base classes for nested open generics
        var baseType = typeToCheck.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            if(baseType.IsNestedOpenGeneric)
            {
                var diagnostic = Diagnostic.Create(
                    NestedOpenGeneric,
                    location,
                    targetType.Name,
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                reportDiagnostic(diagnostic);
            }
            baseType = baseType.BaseType;
        }
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
    /// </summary>
    private static DefaultSettingsMap CollectDefaultSettings(
        Compilation compilation,
        INamedTypeSymbol? iocRegisterDefaultSettingsAttribute,
        CancellationToken cancellationToken)
    {
        if(iocRegisterDefaultSettingsAttribute is null)
            return new DefaultSettingsMap([]);

        var settingsBuilder = ImmutableArray.CreateBuilder<DefaultSettingsModel>();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            if(!SymbolEqualityComparer.Default.Equals(attributeClass, iocRegisterDefaultSettingsAttribute))
                continue;

            // Use shared method to extract default settings
            var settings = attribute.ExtractDefaultSettings();
            if(settings is not null)
            {
                settingsBuilder.Add(settings);
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
        INamedTypeSymbol? iocRegisterForAttribute,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> serviceTypeIndex,
        DefaultSettingsMap defaultSettings)
    {
        public INamedTypeSymbol? IoCRegisterAttribute { get; } = iocRegisterAttribute;
        public INamedTypeSymbol? IoCRegisterForAttribute { get; } = iocRegisterForAttribute;
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
    }

    private sealed record ServiceInfo(INamedTypeSymbol Type, ServiceLifetime Lifetime, Location? Location);
}
