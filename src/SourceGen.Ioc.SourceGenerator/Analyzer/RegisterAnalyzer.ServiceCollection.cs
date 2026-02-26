using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for service collection, registration, and context types.
/// </summary>
public sealed partial class RegisterAnalyzer
{
    private static ImmutableHashSet<SyntaxTree> CollectAssemblyLevelRegistrations(
        Compilation compilation,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var syntaxTreesBuilder = ImmutableHashSet.CreateBuilder<SyntaxTree>();

        // Check if any IoCRegisterForAttribute variant is available
        if (analyzerContext.AttributeSymbols.IocRegisterForAttribute is null && analyzerContext.AttributeSymbols.IocRegisterForAttribute_T1 is null)
            return syntaxTreesBuilder.ToImmutable();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
                continue;

            // Check if this is an IoCRegisterForAttribute (non-generic or generic)
            if (!AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols))
                continue;

            // Track which syntax tree contains this attribute
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if (syntaxReference?.SyntaxTree is { } syntaxTree)
            {
                syntaxTreesBuilder.Add(syntaxTree);
            }

            // Get target type from attribute (constructor arg for non-generic, type parameter for generic)
            var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
            if (targetType is null)
                continue;

            // Skip invalid types
            if (targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if (targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            var location = syntaxReference?.GetSyntax(cancellationToken).GetLocation();

            var (hasExplicitLifetime, explicitLifetime) = attribute.TryGetLifetime();
            var lifetime = GetEffectiveLifetime(analyzerContext, targetType, hasExplicitLifetime, explicitLifetime);

            // Get key type for SGIOC013/SGIOC014 analysis
            var (key, _, keyTypeSymbol) = attribute.GetKeyInfo();

            // Check for Factory and Instance (used by ServiceInfo)
            var (hasFactory, hasInstance) = attribute.HasFactoryOrInstance();

            RegisterServiceWithIndex(analyzerContext, targetType, lifetime, location, keyTypeSymbol, key is not null, hasFactory, hasInstance);
        }

        return syntaxTreesBuilder.ToImmutable();
    }

    private static void AnalyzeAssemblyLevelRegistrations(
        SemanticModelAnalysisContext context,
        AnalyzerContext analyzerContext,
        ImmutableHashSet<SyntaxTree> assemblyAttributeSyntaxTrees)
    {
        // Check if any IoCRegisterForAttribute variant is available
        if (analyzerContext.AttributeSymbols.IocRegisterForAttribute is null && analyzerContext.AttributeSymbols.IocRegisterForAttribute_T1 is null)
            return;

        // Only analyze if this syntax tree contains assembly-level attributes
        if (!assemblyAttributeSyntaxTrees.Contains(context.SemanticModel.SyntaxTree))
            return;

        foreach (var attribute in context.SemanticModel.Compilation.Assembly.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Only process attributes from the current syntax tree
            var syntaxReference = attribute.ApplicationSyntaxReference;
            if (syntaxReference?.SyntaxTree != context.SemanticModel.SyntaxTree)
                continue;

            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
                continue;

            // Check if this is an IoCRegisterForAttribute (non-generic or generic)
            if (!AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols))
                continue;

            // Get target type from attribute (constructor arg for non-generic, type parameter for generic)
            var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
            if (targetType is null)
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
        if (context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        foreach (var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (!TryGetIoCAttribute(attribute, analyzerContext, out var isIoCRegisterFor))
                continue;

            INamedTypeSymbol targetType;

            if (isIoCRegisterFor)
            {
                // Use extension method to get target type (supports both generic and non-generic variants)
                var target = attribute.GetTargetTypeFromRegisterForAttribute();
                if (target is null)
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
            if (targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
                continue;
            if (targetType.DeclaredAccessibility is Accessibility.Private)
                continue;

            // Get lifetime of current service (considering default settings)
            var (hasExplicitLifetime, explicitLifetime) = attribute.TryGetLifetime();
            var currentLifetime = GetEffectiveLifetime(analyzerContext, targetType, hasExplicitLifetime, explicitLifetime);

            // Get key type for SGIOC013/SGIOC014 analysis
            var (key, _, keyTypeSymbol) = attribute.GetKeyInfo();

            // Check for Factory and Instance (used by ServiceInfo)
            var (hasFactory, hasInstance) = attribute.HasFactoryOrInstance();

            // Register service with index for faster lookup
            // Dependency analysis will be done in CompilationEnd after all services are collected
            RegisterServiceWithIndex(analyzerContext, targetType, currentLifetime, location, keyTypeSymbol, key is not null, hasFactory, hasInstance);
        }
    }

    /// <summary>
    /// Resolves key type symbols for KeyType.Csharp registrations using AttributeSyntax + SemanticModel.
    /// This runs in RegisterSyntaxNodeAction to avoid RS1030 (no Compilation.GetSemanticModel calls).
    /// </summary>
    private static void ResolveCsharpKeyTypes(
        SyntaxNodeAnalysisContext context,
        AnalyzerContext analyzerContext)
    {
        if (context.Node is not AttributeSyntax attributeSyntax)
            return;

        var attributeSymbol = context.SemanticModel.GetSymbolInfo(attributeSyntax, context.CancellationToken).Symbol;
        if (attributeSymbol is not IMethodSymbol attributeConstructor)
            return;

        var attributeClass = attributeConstructor.ContainingType;
        if (attributeClass is null)
            return;

        if (!AnalyzerHelpers.IsIoCRegistrationAttribute(attributeClass, analyzerContext.AttributeSymbols))
            return;

        var attributeData = GetAttributeDataFromSyntax(context, attributeSyntax, attributeClass);
        if (attributeData is null)
            return;

        if (attributeData.GetNamedArgument<int>("KeyType", 0) != 1)
            return;

        var (_, _, resolvedKeyType) = attributeData.GetKeyInfo(context.SemanticModel);
        if (resolvedKeyType is null)
            return;

        var targetType = AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols)
            ? attributeData.GetTargetTypeFromRegisterForAttribute()
            : GetTypeLevelTargetType(context, attributeSyntax);

        if (targetType is null)
            return;

        analyzerContext.ResolvedCsharpKeyTypes[(targetType, attributeSyntax.GetLocation())] = resolvedKeyType;
    }

    private static AttributeData? GetAttributeDataFromSyntax(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol attributeClass)
    {
        if (attributeSyntax.Parent is not AttributeListSyntax attributeList)
            return null;

        var syntaxTree = attributeSyntax.SyntaxTree;

        if (attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) is true)
        {
            return context.SemanticModel.Compilation.Assembly.GetAttributes()
                .FirstOrDefault(attr =>
                    SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeClass)
                    && attr.ApplicationSyntaxReference?.SyntaxTree == syntaxTree
                    && attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).Span == attributeSyntax.Span);
        }

        var targetType = GetTypeLevelTargetType(context, attributeSyntax);
        if (targetType is null)
            return null;

        return targetType.GetAttributes()
            .FirstOrDefault(attr =>
                SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeClass)
                && attr.ApplicationSyntaxReference?.SyntaxTree == syntaxTree
                && attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).Span == attributeSyntax.Span);
    }

    private static INamedTypeSymbol? GetTypeLevelTargetType(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax)
    {
        var declaration = attributeSyntax.Parent?.Parent;

        return declaration switch
        {
            TypeDeclarationSyntax typeDeclaration => context.SemanticModel.GetDeclaredSymbol(typeDeclaration, context.CancellationToken),
            _ => null
        };
    }

    /// <summary>
    /// Checks if the attribute is an IoC registration attribute and returns which type.
    /// Supports both non-generic and generic variants.
    /// </summary>
    private static bool TryGetIoCAttribute(AttributeData attribute, AnalyzerContext analyzerContext, out bool isIoCRegisterFor)
    {
        isIoCRegisterFor = false;
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
            return false;

        // Check IoCRegisterAttribute variants (non-generic and generic)
        if (AnalyzerHelpers.IsIoCRegisterAttribute(attributeClass, analyzerContext.AttributeSymbols))
        {
            return true;
        }

        // Check IoCRegisterForAttribute variants (non-generic and generic)
        if (AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols))
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
        Location? location,
        ITypeSymbol? keyTypeSymbol = null,
        bool hasKey = false,
        bool hasFactory = false,
        bool hasInstance = false)
    {
        var serviceInfo = new ServiceInfo(targetType, lifetime, location, keyTypeSymbol, hasKey, hasFactory, hasInstance);

        if (!analyzerContext.RegisteredServices.TryAdd(targetType, serviceInfo))
            return; // Already registered

        // Only build type index for non-keyed services.
        // Keyed services are resolved by key + type, not by type alone,
        // so including them in the type-only index could lead to false positive
        // circular dependency (SGIOC002) or lifetime conflict (SGIOC003-005) diagnostics.
        if (hasKey)
            return;

        // Build index for interfaces
        foreach (var iface in targetType.AllInterfaces)
        {
            analyzerContext.ServiceTypeIndex.TryAdd(iface, serviceInfo);
        }

        // Build index for base classes
        var baseType = targetType.BaseType;
        while (baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            analyzerContext.ServiceTypeIndex.TryAdd(baseType, serviceInfo);
            baseType = baseType.BaseType;
        }
    }

    /// <summary>
    /// Collects default settings from IoCRegisterDefaultSettingsAttribute on the assembly.
    /// Uses the shared ExtractDefaultSettings method from Constants.
    /// Also tracks duplicated target types for SGIOC012 reporting.
    /// </summary>
    private static DefaultSettingsMap CollectDefaults(
        Compilation compilation,
        IoCAttributeSymbols attributeSymbols,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> seenTargetTypes,
        CancellationToken cancellationToken)
    {
        if (attributeSymbols.IocRegisterDefaultsAttribute is null && attributeSymbols.IocRegisterDefaultsAttribute_T1 is null)
            return new DefaultSettingsMap([]);

        var settingsBuilder = ImmutableArray.CreateBuilder<DefaultSettingsModel>();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
                continue;

            // Check if this is an IoCRegisterDefaultsAttribute (non-generic or generic)
            if (!AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, attributeSymbols))
            {
                continue;
            }

            // Use shared method to extract default settings
            // Use different extraction method for generic vs non-generic attributes
            var settings = attributeClass.IsGenericType
                ? attribute.ExtractDefaultSettingsFromGenericAttribute()
                : attribute.ExtractDefaultSettings();
            if (settings is not null)
            {
                var targetTypeName = settings.TargetServiceType.Name;
                var tags = settings.Tags;

                // Build effective tags list using helper method
                var effectiveTags = AnalyzerHelpers.GetEffectiveTags(tags);

                // SGIOC012: Check each effective tag for duplicates
                var hasDuplicate = false;
                foreach (var tag in effectiveTags)
                {
                    var defaultKey = (targetTypeName, tag);
                    if (!seenTargetTypes.TryAdd(defaultKey, attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()))
                    {
                        hasDuplicate = true;
                        break; // Only need to find one duplicate
                    }
                }

                if (hasDuplicate)
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
        if (hasExplicitLifetime)
            return explicitLifetime;

        var defaultSettings = analyzerContext.DefaultSettings;
        if (defaultSettings.IsEmpty)
            return explicitLifetime;

        // Check default settings for matching interfaces
        foreach (var iface in targetType.AllInterfaces)
        {
            var ifaceTypeData = iface.GetTypeData();

            // Try exact match first
            if (defaultSettings.TryGetExactMatches(ifaceTypeData.Name, out var exactIndex))
                return defaultSettings[exactIndex].Lifetime;

            // Try generic match (e.g., IGenericTest<> matches IGenericTest<T>)
            if (iface.IsGenericType
                && ifaceTypeData is GenericTypeData genericInterfaceTypeData
                && defaultSettings.TryGetGenericMatches(genericInterfaceTypeData.NameWithoutGeneric, genericInterfaceTypeData.GenericArity, out var genericIndex))
                return defaultSettings[genericIndex].Lifetime;
        }

        // Check default settings for matching base classes
        var baseType = targetType.BaseType;
        while (baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            var baseTypeData = baseType.GetTypeData();

            // Try exact match first
            if (defaultSettings.TryGetExactMatches(baseTypeData.Name, out var exactIndex))
                return defaultSettings[exactIndex].Lifetime;

            // Try generic match for base classes
            if (baseType.IsGenericType
                && baseTypeData is GenericTypeData genericBaseTypeData
                && defaultSettings.TryGetGenericMatches(genericBaseTypeData.NameWithoutGeneric, genericBaseTypeData.GenericArity, out var genericIndex))
                return defaultSettings[genericIndex].Lifetime;

            baseType = baseType.BaseType;
        }

        // Default lifetime is Transient (as defined in TryGetLifetime)
        return explicitLifetime;
    }

    private readonly struct SymbolLocationComparer : IEqualityComparer<(INamedTypeSymbol Type, Location? Location)>
    {
        public bool Equals((INamedTypeSymbol Type, Location? Location) x, (INamedTypeSymbol Type, Location? Location) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && x.Location == y.Location;

        public int GetHashCode((INamedTypeSymbol Type, Location? Location) obj)
            => unchecked((SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397) ^ (obj.Location?.GetHashCode() ?? 0));
    }

    private sealed class AnalyzerContext(
        IoCAttributeSymbols attributeSymbols,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> serviceTypeIndex,
        DefaultSettingsMap defaultSettings,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> seenDefaultTargetTypes)
    {
        public IoCAttributeSymbols AttributeSymbols { get; } = attributeSymbols;
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
        /// Tracks seen IoCRegisterDefaults (target type name, single tag) pairs across assembly and type-level attributes (SGIOC012).
        /// Services without tags use an empty string tag for comparison.
        /// </summary>
        public ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> SeenDefaultTargetTypes { get; } = seenDefaultTargetTypes;

        /// <summary>
        /// Tracks registered (ImplementationType, Key, single Tag) tuples to detect duplicates (SGIOC011).
        /// Each tag is tracked separately; duplicates are detected when any single tag matches.
        /// Services without tags use an empty string tag for comparison.
        /// Value is the first registration location for diagnostic reporting.
        /// </summary>
        public ConcurrentDictionary<(string TypeName, string? Key, string Tag), Location?> RegistrationKeys { get; } = [];

        /// <summary>
        /// Stores resolved key type symbols for KeyType.Csharp registrations where nameof() was used.
        /// Populated by RegisterSyntaxNodeAction, consumed by SGIOC015 in CompilationEnd.
        /// Key: (TargetType, AttributeLocation) to handle multiple registrations on the same type.
        /// </summary>
        public ConcurrentDictionary<(INamedTypeSymbol Type, Location? Location), ITypeSymbol> ResolvedCsharpKeyTypes { get; } = new(new SymbolLocationComparer());
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

        /// <summary>
        /// The type symbol of the registration key, or null if no key is specified or KeyType is Csharp.
        /// </summary>
        public ITypeSymbol? KeyTypeSymbol { get; }

        /// <summary>
        /// Indicates whether a Key is specified (regardless of KeyType).
        /// </summary>
        public bool HasKey { get; }

        /// <summary>
        /// Indicates whether a Factory method is specified.
        /// </summary>
        public bool HasFactory { get; }

        /// <summary>
        /// Indicates whether an Instance is specified.
        /// </summary>
        public bool HasInstance { get; }

        /// <summary>
        /// Cached constructor to avoid repeated SpecifiedOrPrimaryOrMostParametersConstructor lookups.
        /// </summary>
        public IMethodSymbol? Constructor { get; }

        /// <summary>
        /// Cached list of members with [IocInject] or [Inject] attribute to avoid repeated GetMembers() and attribute lookups.
        /// </summary>
        public ImmutableArray<(ISymbol Member, AttributeData InjectAttribute)> InjectedMembers { get; }

        public ServiceInfo(
            INamedTypeSymbol type,
            ServiceLifetime lifetime,
            Location? location,
            ITypeSymbol? keyTypeSymbol = null,
            bool hasKey = false,
            bool hasFactory = false,
            bool hasInstance = false)
        {
            Type = type;
            Lifetime = lifetime;
            Location = location;
            FullyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            KeyTypeSymbol = keyTypeSymbol;
            HasKey = hasKey;
            HasFactory = hasFactory;
            HasInstance = hasInstance;

            // Cache constructor lookup
            Constructor = type.SpecifiedOrPrimaryOrMostParametersConstructor;

            // Cache injected members
            InjectedMembers = [.. type.GetInjectedMembers()];
        }
    }
}
