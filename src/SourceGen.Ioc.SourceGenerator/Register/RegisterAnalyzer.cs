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
        DuplicatedKeyedServiceAttribute
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

        // First pass: collect services and do immediate validation (SGIOC001)
        context.RegisterSymbolAction(ctx => CollectAndValidateNamedType(ctx, analyzerContext), SymbolKind.NamedType);

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

            // SGIOC009: Check Instance requires Singleton lifetime
            AnalyzeInstanceLifetime(context.ReportDiagnostic, attribute, location);
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

            // SGIOC009: Check Instance requires Singleton lifetime
            AnalyzeInstanceLifetime(context.ReportDiagnostic, attribute, location);

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

        // Check if this is an IoC registration attribute
        var comparer = SymbolEqualityComparer.Default;
        if(!comparer.Equals(attributeClass, analyzerContext.IoCRegisterAttribute) &&
           !comparer.Equals(attributeClass, analyzerContext.IoCRegisterForAttribute))
        {
            return;
        }

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
