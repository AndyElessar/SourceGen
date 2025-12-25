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
    /// SGIOC003: Service Lifetime Conflict Detected - Conflicting service lifetimes among registered services.
    /// </summary>
    public static readonly DiagnosticDescriptor LifetimeConflict = new(
        id: "SGIOC003",
        title: "Service Lifetime Conflict Detected",
        messageFormat: "Lifetime conflict: {1} service '{0}' depends on {3} service '{2}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A service with a longer lifetime (e.g., Singleton) should not depend on a service with a shorter lifetime (e.g., Scoped or Transient).");

    /// <summary>
    /// SGIOC004: Nested Open Generic Detected - Service is implementing nested open generic interfaces/class.
    /// </summary>
    public static readonly DiagnosticDescriptor NestedOpenGeneric = new(
        id: "SGIOC004",
        title: "Nested Open Generic Detected",
        messageFormat: "The type '{0}' implements nested open generic '{1}', which cannot be registered",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Services implementing nested open generic interfaces or classes cannot be registered with the dependency injection container.");

    /// <summary>
    /// SGIOC101: Service Lifetime Conflict Warning - Potential lifetime conflict among registered services.
    /// </summary>
    public static readonly DiagnosticDescriptor LifetimeConflictWarning = new(
        id: "SGIOC101",
        title: "Service Lifetime Conflict Detected",
        messageFormat: "Potential lifetime conflict: {1} service '{0}' depends on {3} service '{2}'",
        category: Constants.Category_Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A Scoped service depending on a Transient service may cause unexpected behavior.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        InvalidAttributeUsage,
        CircularDependency,
        LifetimeConflict,
        NestedOpenGeneric,
        LifetimeConflictWarning
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
        var iocRegisterDefaultSettingsAttribute = context.Compilation.GetTypeByMetadataName(Constants.IoCRegisterDefaultSettingsAttributeFullName);

        if(iocRegisterAttribute is null && iocRegisterForAttribute is null)
            return;

        // Use ConcurrentDictionary for thread-safe collection during parallel symbol analysis
        var registeredServices = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Index for service type -> implementation lookup (interfaces/base classes)
        var serviceTypeIndex = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);
        // Collect default settings from IoCRegisterDefaultSettingsAttribute
        var defaultSettings = CollectDefaultSettings(context.Compilation, iocRegisterDefaultSettingsAttribute, context.CancellationToken);

        var analyzerContext = new AnalyzerContext(iocRegisterAttribute, iocRegisterForAttribute, registeredServices, serviceTypeIndex, defaultSettings);

        // Collect assembly-level IoCRegisterFor attributes first (synchronously during compilation start)
        var assemblyAttributeSyntaxTrees = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext, context.CancellationToken);

        // First pass: collect services and do immediate validation (SGIOC001, SGIOC004)
        context.RegisterSymbolAction(ctx => CollectAndValidateNamedType(ctx, analyzerContext), SymbolKind.NamedType);

        // Analyze assembly-level IoCRegisterFor attributes using SemanticModelAction for IDE squiggles
        context.RegisterSemanticModelAction(ctx => AnalyzeAssemblyLevelRegistrations(ctx, analyzerContext, assemblyAttributeSyntaxTrees));

        // Second pass: analyze dependencies after all services are collected (SGIOC002, SGIOC003, SGIOC101)
        context.RegisterCompilationEndAction(ctx => AnalyzeAllDependencies(ctx, analyzerContext));
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

            // SGIOC004: Check for nested open generic (only when registering interfaces/base classes)
            bool willRegisterInterfacesOrBaseClasses = WillRegisterInterfacesOrBaseClasses(attribute);
            if(willRegisterInterfacesOrBaseClasses)
            {
                AnalyzeNestedOpenGeneric(context, targetType, location);
            }
        }
    }

    /// <summary>
    /// First pass: collect services and do immediate validation (SGIOC001, SGIOC004).
    /// Dependency analysis (SGIOC002, SGIOC003, SGIOC101) is deferred to CompilationEnd.
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

            // SGIOC004: Check for nested open generic (only when registering interfaces/base classes)
            bool willRegisterInterfacesOrBaseClasses = WillRegisterInterfacesOrBaseClasses(attribute);
            if(willRegisterInterfacesOrBaseClasses)
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

    /// <summary>
    /// Determines if the attribute will cause registration of interfaces or base classes.
    /// For open generic types, nested open generics are only a problem when registering interfaces/base classes.
    /// </summary>
    private static bool WillRegisterInterfacesOrBaseClasses(AttributeData attribute)
    {
        // Check if ServiceTypes is specified
        var serviceTypes = attribute.GetTypeArrayArgument("ServiceTypes");
        if(serviceTypes.Length > 0)
            return true;

        // Check if RegisterAllInterfaces is true
        var (hasRegisterAllInterfaces, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        if(hasRegisterAllInterfaces && registerAllInterfaces)
            return true;

        // Check if RegisterAllBaseClasses is true
        var (hasRegisterAllBaseClasses, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();
        if(hasRegisterAllBaseClasses && registerAllBaseClasses)
            return true;

        // Only registering self, no interfaces/base classes
        return false;
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
        // Get primary constructor using loop instead of LINQ to avoid allocations
        var constructor = GetPrimaryConstructor(targetType);
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

            // SGIOC003 & SGIOC101: Check for lifetime conflict
            var conflictLevel = GetLifetimeConflictLevel(currentLifetime, dependencyInfo.Lifetime);
            Diagnostic? diagnostic = conflictLevel switch
            {
                LifetimeConflictLevel.Error => Diagnostic.Create(
                    LifetimeConflict,
                    location,
                    targetType.Name,
                    currentLifetime.Name,
                    dependencyInfo.Type.Name,
                    dependencyInfo.Lifetime.Name),
                LifetimeConflictLevel.Warning => Diagnostic.Create(
                    LifetimeConflictWarning,
                    location,
                    targetType.Name,
                    currentLifetime.Name,
                    dependencyInfo.Type.Name,
                    dependencyInfo.Lifetime.Name),
                _ => null
            };
            if(diagnostic is not null)
                reportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Builds the cycle string from the path stack without allocating intermediate collections.
    /// </summary>
    private static string BuildCycleString(Stack<INamedTypeSymbol> pathStack)
    {
        // Stack is LIFO, so we need to reverse for correct order
        StringBuilder sb = new(pathStack.Count * 2 - 1);
        foreach(var (i, item) in pathStack.Reverse().Select((it, i) => (i, it)))
        {
            if(i > 0)
                sb.Append(" -> ");
            sb.Append(item.Name);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gets the primary constructor (with most parameters).
    /// </summary>
    private static IMethodSymbol? GetPrimaryConstructor(INamedTypeSymbol targetType)
    {
        IMethodSymbol? bestConstructor = null;
        int maxParameters = -1;

        foreach(var constructor in targetType.Constructors)
        {
            if(constructor.IsStatic)
                continue;
            if(constructor.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                continue;

            if(constructor.Parameters.Length > maxParameters)
            {
                maxParameters = constructor.Parameters.Length;
                bestConstructor = constructor;
            }
        }

        return bestConstructor;
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

        // Use GetPrimaryConstructor to avoid LINQ allocations
        var constructor = GetPrimaryConstructor(currentType);
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

    private static LifetimeConflictLevel GetLifetimeConflictLevel(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        // Singleton depending on Scoped is a conflict (captive dependency) - Error
        if(consumerLifetime is ServiceLifetime.Singleton && dependencyLifetime is ServiceLifetime.Scoped)
            return LifetimeConflictLevel.Error;

        // Singleton depending on Transient is a captive dependency issue - Error
        // The Transient instance will be captured and live for the application lifetime
        if(consumerLifetime is ServiceLifetime.Singleton && dependencyLifetime is ServiceLifetime.Transient)
            return LifetimeConflictLevel.Error;

        // Scoped depending on Transient is a potential issue - Warning
        // The Transient instance will be captured for the scope lifetime
        if(consumerLifetime is ServiceLifetime.Scoped && dependencyLifetime is ServiceLifetime.Transient)
            return LifetimeConflictLevel.Warning;

        return LifetimeConflictLevel.None;
    }

    private enum LifetimeConflictLevel
    {
        None,
        Warning,
        Error
    }

    /// <summary>
    /// Collects default settings from IoCRegisterDefaultSettingsAttribute on the assembly.
    /// </summary>
    private static ImmutableDictionary<INamedTypeSymbol, DefaultSettingsInfo> CollectDefaultSettings(
        Compilation compilation,
        INamedTypeSymbol? iocRegisterDefaultSettingsAttribute,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, DefaultSettingsInfo>(SymbolEqualityComparer.Default);

        if(iocRegisterDefaultSettingsAttribute is null)
            return builder.ToImmutable();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            if(!SymbolEqualityComparer.Default.Equals(attributeClass, iocRegisterDefaultSettingsAttribute))
                continue;

            if(attribute.ConstructorArguments.Length < 2)
                continue;

            if(attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetServiceType)
                continue;

            if(attribute.ConstructorArguments[1].Value is not int lifetime)
                continue;

            // Store the type as-is (including unbound generic types like IGenericTest2<>)
            // GetEffectiveLifetime will use ConstructUnboundGenericType() for comparison
            var defaultSettingsInfo = new DefaultSettingsInfo((ServiceLifetime)lifetime);

            // Keep the first definition (in case of duplicates)
            if(!builder.ContainsKey(targetServiceType))
            {
                builder.Add(targetServiceType, defaultSettingsInfo);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Gets the effective lifetime for a type, considering default settings.
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

        // Check default settings for matching interfaces
        foreach(var iface in targetType.AllInterfaces)
        {
            // Try exact match first
            if(analyzerContext.DefaultSettings.TryGetValue(iface, out var exactMatch))
                return exactMatch.Lifetime;

            // Try unbound generic match (e.g., IGenericTest<> matches IGenericTest<T>)
            // DefaultSettings stores unbound generic types like IGenericTest2<>
            // We need to construct the unbound generic from the interface to match
            if(iface.IsGenericType)
            {
                var unboundGeneric = iface.ConstructUnboundGenericType();
                if(analyzerContext.DefaultSettings.TryGetValue(unboundGeneric, out var genericMatch))
                    return genericMatch.Lifetime;
            }
        }

        // Check default settings for matching base classes
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            if(analyzerContext.DefaultSettings.TryGetValue(baseType, out var baseMatch))
                return baseMatch.Lifetime;

            // Try unbound generic match for base classes
            if(baseType.IsGenericType)
            {
                var unboundGeneric = baseType.ConstructUnboundGenericType();
                if(analyzerContext.DefaultSettings.TryGetValue(unboundGeneric, out var genericMatch))
                    return genericMatch.Lifetime;
            }

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
        ImmutableDictionary<INamedTypeSymbol, DefaultSettingsInfo> defaultSettings)
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
        /// Default settings from IoCRegisterDefaultSettingsAttribute, keyed by target service type.
        /// </summary>
        public ImmutableDictionary<INamedTypeSymbol, DefaultSettingsInfo> DefaultSettings { get; } = defaultSettings;
    }

    private sealed record ServiceInfo(INamedTypeSymbol Type, ServiceLifetime Lifetime, Location? Location);

    private sealed record DefaultSettingsInfo(ServiceLifetime Lifetime);
}
