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

        // First pass: collect all registered services
        context.RegisterSymbolAction(ctx => CollectRegisteredService(ctx, analyzerContext), SymbolKind.NamedType);

        // Second pass: analyze dependencies (runs after first pass due to symbol action ordering)
        context.RegisterSymbolAction(ctx => AnalyzeNamedType(ctx, analyzerContext), SymbolKind.NamedType);
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

            // SGIOC004: Check for nested open generic
            AnalyzeNestedOpenGeneric(context, targetType, location);

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

    private static void AnalyzeInvalidAttributeUsage(SymbolAnalysisContext context, INamedTypeSymbol targetType, Location? location)
    {
        // Check if target type is private
        if(targetType.DeclaredAccessibility is Accessibility.Private)
        {
            var diagnostic = Diagnostic.Create(
                InvalidAttributeUsage,
                location,
                targetType.Name,
                "private");
            context.ReportDiagnostic(diagnostic);
        }

        // Check if target type is abstract
        if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
        {
            var diagnostic = Diagnostic.Create(
                InvalidAttributeUsage,
                location,
                targetType.Name,
                "abstract");
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeNestedOpenGeneric(SymbolAnalysisContext context, INamedTypeSymbol targetType, Location? location)
    {
        // Only check open generic types
        if(!targetType.IsGenericType)
            return;

        // Check all interfaces for nested open generics
        foreach(var iface in targetType.AllInterfaces)
        {
            if(HasNestedOpenGeneric(iface))
            {
                var diagnostic = Diagnostic.Create(
                    NestedOpenGeneric,
                    location,
                    targetType.Name,
                    iface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                context.ReportDiagnostic(diagnostic);
            }
        }

        // Check base classes for nested open generics
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            if(HasNestedOpenGeneric(baseType))
            {
                var diagnostic = Diagnostic.Create(
                    NestedOpenGeneric,
                    location,
                    targetType.Name,
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                context.ReportDiagnostic(diagnostic);
            }
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Checks if a type has nested open generic type arguments.
    /// For example: IHandler&lt;Wrapper&lt;T&gt;&gt; is a nested open generic.
    /// </summary>
    private static bool HasNestedOpenGeneric(INamedTypeSymbol type)
    {
        if(!type.IsGenericType)
            return false;

        foreach(var typeArg in type.TypeArguments)
        {
            // If the type argument itself is an open generic type (has type parameters)
            if(typeArg is INamedTypeSymbol namedTypeArg)
            {
                // Check if it's a constructed generic type with unbound type parameters
                if(namedTypeArg.IsGenericType && HasTypeParameterInArguments(namedTypeArg))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively checks if a type or its type arguments contain unbound type parameters.
    /// </summary>
    private static bool HasTypeParameterInArguments(INamedTypeSymbol type)
    {
        foreach(var typeArg in type.TypeArguments)
        {
            if(typeArg is ITypeParameterSymbol)
                return true;

            if(typeArg is INamedTypeSymbol nestedType && nestedType.IsGenericType)
            {
                if(HasTypeParameterInArguments(nestedType))
                    return true;
            }
        }

        return false;
    }

    private static void AnalyzeDependencies(
        SymbolAnalysisContext context,
        AnalyzerContext analyzerContext,
        INamedTypeSymbol targetType,
        ServiceLifetime currentLifetime,
        Location? location)
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
            context.CancellationToken.ThrowIfCancellationRequested();

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
                context.ReportDiagnostic(Diagnostic.Create(
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
                context.ReportDiagnostic(diagnostic);
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
