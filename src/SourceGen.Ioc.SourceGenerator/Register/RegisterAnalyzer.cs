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

        if(iocRegisterAttribute is null && iocRegisterForAttribute is null)
            return;

        // Use ConcurrentDictionary for thread-safe collection during parallel symbol analysis
        var registeredServices = new ConcurrentDictionary<INamedTypeSymbol, ServiceInfo>(SymbolEqualityComparer.Default);

        var analyzerContext = new AnalyzerContext(iocRegisterAttribute, iocRegisterForAttribute, registeredServices);

        // Collect assembly-level IoCRegisterFor attributes first
        var assemblyAttributeSyntaxTrees = CollectAssemblyLevelRegistrations(context.Compilation, analyzerContext);

        // First pass: collect all registered services from type-level attributes
        context.RegisterSymbolAction(ctx => CollectRegisteredService(ctx, analyzerContext), SymbolKind.NamedType);

        // Analyze assembly-level IoCRegisterFor attributes using SemanticModelAction for IDE squiggles
        context.RegisterSemanticModelAction(ctx => AnalyzeAssemblyLevelRegistrations(ctx, analyzerContext, assemblyAttributeSyntaxTrees));

        // Second pass: analyze dependencies (runs after first pass due to symbol action ordering)
        context.RegisterSymbolAction(ctx => AnalyzeNamedType(ctx, analyzerContext), SymbolKind.NamedType);
    }

    private static ImmutableHashSet<SyntaxTree> CollectAssemblyLevelRegistrations(Compilation compilation, AnalyzerContext analyzerContext)
    {
        var syntaxTreesBuilder = ImmutableHashSet.CreateBuilder<SyntaxTree>();

        if(analyzerContext.IoCRegisterForAttribute is null)
            return syntaxTreesBuilder.ToImmutable();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
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

            var location = syntaxReference?.GetSyntax().GetLocation();

            var (_, lifetime) = attribute.TryGetLifetime();

            analyzerContext.RegisteredServices.TryAdd(targetType, new ServiceInfo(targetType, lifetime, location));
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

            // Skip further analysis if type is invalid
            if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if(targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            // Get lifetime of current service
            var (_, currentLifetime) = attribute.TryGetLifetime();

            // SGIOC002, SGIOC003 & SGIOC101: Analyze dependencies
            AnalyzeDependencies(context, analyzerContext, targetType, currentLifetime, location);
        }
    }

    private static void CollectRegisteredService(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            bool isIoCRegister = SymbolEqualityComparer.Default.Equals(attributeClass, analyzerContext.IoCRegisterAttribute);
            bool isIoCRegisterFor = SymbolEqualityComparer.Default.Equals(attributeClass, analyzerContext.IoCRegisterForAttribute);

            if(!isIoCRegister && !isIoCRegisterFor)
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

            // Skip invalid types
            if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if(targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                ?? typeSymbol.Locations.FirstOrDefault();

            var (_, lifetime) = attribute.TryGetLifetime();

            analyzerContext.RegisteredServices.TryAdd(targetType, new ServiceInfo(targetType, lifetime, location));
        }
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, AnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach(var attribute in typeSymbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            bool isIoCRegister = SymbolEqualityComparer.Default.Equals(attributeClass, analyzerContext.IoCRegisterAttribute);
            bool isIoCRegisterFor = SymbolEqualityComparer.Default.Equals(attributeClass, analyzerContext.IoCRegisterForAttribute);

            if(!isIoCRegister && !isIoCRegisterFor)
                continue;

            INamedTypeSymbol targetType;

            if(isIoCRegisterFor)
            {
                // For IoCRegisterForAttribute, check the target type
                if(attribute.ConstructorArguments.Length == 0 ||
                   attribute.ConstructorArguments[0].Value is not INamedTypeSymbol target)
                    continue;

                targetType = target;
            }
            else
            {
                // For IoCRegisterAttribute, the type itself is the target
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

            // Skip further analysis if type is invalid
            if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if(targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            // Get lifetime of current service
            var (_, currentLifetime) = attribute.TryGetLifetime();

            // SGIOC002, SGIOC003 & SGIOC101: Analyze dependencies
            AnalyzeDependencies(context, analyzerContext, targetType, currentLifetime, location);
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
        SymbolAnalysisContext context,
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime currentLifetime,
        Location? location)
        => AnalyzeDependencies(context.ReportDiagnostic, analyzerContext, targetType, currentLifetime, location, context.CancellationToken);

    private static void AnalyzeDependencies(
        SemanticModelAnalysisContext context,
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime currentLifetime,
        Location? location)
        => AnalyzeDependencies(context.ReportDiagnostic, analyzerContext, targetType, currentLifetime, location, context.CancellationToken);

    private static void AnalyzeDependencies(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime currentLifetime,
        Location? location,
        CancellationToken cancellationToken)
    {
        // Get constructor dependencies
        var constructors = targetType.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToArray();

        if(constructors.Length == 0)
            return;

        // Use the constructor with most parameters (DI typically uses this)
        var constructor = constructors.OrderByDescending(c => c.Parameters.Length).First();

        foreach(var parameter in constructor.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(parameter.Type is not INamedTypeSymbol parameterType)
                continue;

            // Find the dependency's implementation type and lifetime
            var dependencyInfo = FindRegisteredDependency(analyzerContext, parameterType);
            if(dependencyInfo is null)
                continue;

            // SGIOC002: Check for circular dependency
            var cyclePath = DetectCircularDependency(analyzerContext, targetType, dependencyInfo.Type, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default));
            if(cyclePath is not null)
            {
                var cycleString = string.Join(" -> ", cyclePath.Select(t => t.Name));
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

    private static ServiceInfo? FindRegisteredDependency(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol parameterType)
    {
        // Direct match
        if(analyzerContext.RegisteredServices.TryGetValue(parameterType, out var directMatch))
            return directMatch;

        // Check if any registered service implements this interface or inherits from this base class
        foreach(var kvp in analyzerContext.RegisteredServices)
        {
            var implType = kvp.Key;
            var serviceInfo = kvp.Value;

            // Check interfaces
            if(implType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, parameterType)))
                return serviceInfo;

            // Check base classes
            var baseType = implType.BaseType;
            while(baseType is not null)
            {
                if(SymbolEqualityComparer.Default.Equals(baseType, parameterType))
                    return serviceInfo;
                baseType = baseType.BaseType;
            }
        }

        return null;
    }

    private static List<INamedTypeSymbol>? DetectCircularDependency(
        AnalyzerContext analyzerContext,
        INamedTypeSymbol startType,
        INamedTypeSymbol currentType,
        HashSet<INamedTypeSymbol> visited)
    {
        if(SymbolEqualityComparer.Default.Equals(startType, currentType))
            return [startType];

        if(!visited.Add(currentType))
            return null;

        // Check if current type is registered
        if(!analyzerContext.RegisteredServices.ContainsKey(currentType))
            return null;

        var constructors = currentType.Constructors
            .Where(c => !c.IsStatic && c.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            .ToArray();

        if(constructors.Length == 0)
            return null;

        var constructor = constructors.OrderByDescending(c => c.Parameters.Length).First();

        foreach(var parameter in constructor.Parameters)
        {
            if(parameter.Type is not INamedTypeSymbol parameterType)
                continue;

            var dependency = FindRegisteredDependency(analyzerContext, parameterType);
            if(dependency is null)
                continue;

            var cyclePath = DetectCircularDependency(analyzerContext, startType, dependency.Type, visited);
            if(cyclePath is not null)
            {
                cyclePath.Insert(0, currentType);
                return cyclePath;
            }
        }

        return null;
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

    private sealed class AnalyzerContext(
        INamedTypeSymbol? iocRegisterAttribute,
        INamedTypeSymbol? iocRegisterForAttribute,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices)
    {
        public INamedTypeSymbol? IoCRegisterAttribute { get; } = iocRegisterAttribute;
        public INamedTypeSymbol? IoCRegisterForAttribute { get; } = iocRegisterForAttribute;
        public ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> RegisteredServices { get; } = registeredServices;
    }

    private sealed record ServiceInfo(INamedTypeSymbol Type, ServiceLifetime Lifetime, Location? Location);
}
