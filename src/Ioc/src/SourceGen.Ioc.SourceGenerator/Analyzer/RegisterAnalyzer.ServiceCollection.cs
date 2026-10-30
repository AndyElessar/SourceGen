using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for service collection, registration, and context types.
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// An assembly-level IoCRegisterFor attribute, bound and parsed once at compilation start.
    /// </summary>
    private readonly record struct AssemblyRegistration(
        AttributeData Attribute,
        INamedTypeSymbol TargetType,
        string? Key,
        List<INamedTypeSymbol> ExplicitServiceTypes);

    /// <summary>
    /// Registers the assembly-level IoCRegisterFor attributes and indexes them by their syntax,
    /// so the attribute syntax action reports their diagnostics without binding them again.
    /// </summary>
    private static Dictionary<(SyntaxTree Tree, TextSpan Span), AssemblyRegistration> CollectAssemblyLevelRegistrations(
        Compilation compilation,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var registrations = new Dictionary<(SyntaxTree Tree, TextSpan Span), AssemblyRegistration>();

        foreach(var (attribute, targetType) in AnalyzerHelpers.EnumerateAssemblyLevelRegisterForAttributes(
            compilation, analyzerContext.AttributeSymbols, cancellationToken))
        {
            var (key, _, keyTypeSymbol) = attribute.GetKeyInfo();
            var explicitServiceTypes = AnalyzerHelpers.GetExplicitServiceTypes(attribute);

            var syntaxReference = attribute.ApplicationSyntaxReference;
            if(syntaxReference is not null)
            {
                registrations[(syntaxReference.SyntaxTree, syntaxReference.Span)] = new(attribute, targetType, key, explicitServiceTypes);
            }

            var location = syntaxReference?.GetSyntax(cancellationToken).GetLocation();
            RegisterService(analyzerContext, attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location);
        }

        return registrations;
    }

    /// <summary>
    /// SGIOC001, SGIOC009, SGIOC011 and SGIOC032 for an assembly-level IoCRegisterFor attribute.
    /// </summary>
    private static void AnalyzeAssemblyLevelRegistration(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AssemblyRegistration registration,
        Location location)
        => AnalyzeRegistrationAttribute(
            reportDiagnostic,
            analyzerContext,
            registration.Attribute,
            registration.TargetType,
            registration.Key,
            registration.ExplicitServiceTypes,
            location);

    /// <summary>
    /// First pass: collect services and do immediate validation (SGIOC001, SGIOC009, SGIOC011, SGIOC032).
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
                // Use extension method to get target type (supports both generic and non-generic variants)
                var target = attribute.GetTargetTypeFromRegisterForAttribute();
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

            // Parsed once for the immediate checks and the registration
            var (key, _, keyTypeSymbol) = attribute.GetKeyInfo();
            var explicitServiceTypes = AnalyzerHelpers.GetExplicitServiceTypes(attribute);

            AnalyzeRegistrationAttribute(context.ReportDiagnostic, analyzerContext, attribute, targetType, key, explicitServiceTypes, location);

            // Register service with index for faster lookup
            // Dependency analysis will be done in CompilationEnd after all services are collected
            RegisterService(analyzerContext, attribute, targetType, key, keyTypeSymbol, explicitServiceTypes, location);
        }
    }

    /// <summary>
    /// The checks that run for every registration attribute, type-level or assembly-level.
    /// </summary>
    private static void AnalyzeRegistrationAttribute(
        Action<Diagnostic> reportDiagnostic,
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        string? key,
        List<INamedTypeSymbol> explicitServiceTypes,
        Location? location)
    {
        // SGIOC001: Check if target type is private or abstract
        AnalyzeInvalidAttributeUsage(reportDiagnostic, targetType, location);

        // SGIOC009: Check Instance requires Singleton lifetime
        AnalyzeInstanceLifetime(reportDiagnostic, attribute, location);

        // SGIOC011: Check for duplicated registrations (same implementation type and key)
        AnalyzeDuplicatedRegistration(reportDiagnostic, analyzerContext, attribute, targetType, key, location);

        // SGIOC032: Check explicit service types that would be skipped
        AnalyzeIncompatibleServiceTypes(reportDiagnostic, explicitServiceTypes, targetType, location);
    }

    /// <summary>
    /// Records a registration of a valid target type for the CompilationEnd analyses.
    /// </summary>
    private static void RegisterService(
        AnalyzerContext analyzerContext,
        AttributeData attribute,
        INamedTypeSymbol targetType,
        string? key,
        ITypeSymbol? keyTypeSymbol,
        List<INamedTypeSymbol> explicitServiceTypes,
        Location? location)
    {
        // Skip registration if type is invalid
        if(targetType.IsAbstract && targetType.TypeKind is not TypeKind.Interface)
            return;
        if(targetType.DeclaredAccessibility is Accessibility.Private)
            return;

        // Get lifetime of current service (considering default settings)
        var (hasExplicitLifetime, explicitLifetime) = attribute.TryGetLifetime();
        var lifetime = GetEffectiveLifetime(analyzerContext, targetType, hasExplicitLifetime, explicitLifetime);

        // Check for Factory and Instance (used by ServiceInfo)
        var (hasFactory, hasInstance) = attribute.HasFactoryOrInstance();

        var serviceTypes = AnalyzerHelpers.EnumerateRegisteredServiceTypes(targetType, attribute, explicitServiceTypes);
        RegisterServiceWithIndex(analyzerContext, targetType, lifetime, location, key, keyTypeSymbol, hasFactory, hasInstance, serviceTypes);
    }

    /// <summary>
    /// Resolves key type symbols for KeyType.Csharp registrations using AttributeSyntax + SemanticModel.
    /// This runs in RegisterSyntaxNodeAction to avoid RS1030 (no Compilation.GetSemanticModel calls).
    /// </summary>
    /// <param name="attributeData">The attribute data when it is already known, as for assembly-level registrations.</param>
    private static void ResolveCsharpKeyTypes(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol attributeClass,
        AttributeData? attributeData,
        AnalyzerContext analyzerContext)
    {
        if(!AnalyzerHelpers.IsIoCRegistrationAttribute(attributeClass, analyzerContext.AttributeSymbols))
            return;

        // KeyType can only be set as a named argument and defaults to Value, so skip the AttributeData lookup
        // (a scan of all assembly attributes for assembly-level registrations) when it is absent
        if(!HasNamedArgument(attributeSyntax, "KeyType"))
            return;

        attributeData ??= GetAttributeDataFromSyntax(context, attributeSyntax, attributeClass);
        if(attributeData is null)
            return;

        if(attributeData.GetNamedArgument<int>("KeyType", 0) != 1)
            return;

        var (_, _, resolvedKeyType) = attributeData.GetKeyInfo(context.SemanticModel);
        if(resolvedKeyType is null)
            return;

        var targetType = AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols)
            ? attributeData.GetTargetTypeFromRegisterForAttribute()
            : GetTypeLevelTargetType(context, attributeSyntax);

        if(targetType is null)
            return;

        analyzerContext.ResolvedCsharpKeyTypes[(targetType, attributeSyntax.GetLocation())] = resolvedKeyType;
    }

    private static bool HasNamedArgument(AttributeSyntax attributeSyntax, string name)
    {
        if(attributeSyntax.ArgumentList is not { } argumentList)
            return false;

        foreach(var argument in argumentList.Arguments)
        {
            if(argument.NameEquals?.Name.Identifier.Text == name)
                return true;
        }

        return false;
    }

    private static AttributeData? GetAttributeDataFromSyntax(
        SyntaxNodeAnalysisContext context,
        AttributeSyntax attributeSyntax,
        INamedTypeSymbol attributeClass)
    {
        if(attributeSyntax.Parent is not AttributeListSyntax attributeList)
            return null;

        var syntaxTree = attributeSyntax.SyntaxTree;

        if(attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) is true)
        {
            return context.SemanticModel.Compilation.Assembly.GetAttributes()
                .FirstOrDefault(attr =>
                    SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeClass)
                    && attr.ApplicationSyntaxReference?.SyntaxTree == syntaxTree
                    && attr.ApplicationSyntaxReference?.Span == attributeSyntax.Span);
        }

        var targetType = GetTypeLevelTargetType(context, attributeSyntax);
        if(targetType is null)
            return null;

        return targetType.GetAttributes()
            .FirstOrDefault(attr =>
                SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeClass)
                && attr.ApplicationSyntaxReference?.SyntaxTree == syntaxTree
                && attr.ApplicationSyntaxReference?.Span == attributeSyntax.Span);
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
        if(attributeClass is null)
            return false;

        // Check IoCRegisterAttribute variants (non-generic and generic)
        if(AnalyzerHelpers.IsIoCRegisterAttribute(attributeClass, analyzerContext.AttributeSymbols))
        {
            return true;
        }

        // Check IoCRegisterForAttribute variants (non-generic and generic)
        if(AnalyzerHelpers.IsIoCRegisterForAttribute(attributeClass, analyzerContext.AttributeSymbols))
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
        string? serviceKey,
        ITypeSymbol? keyTypeSymbol,
        bool hasFactory,
        bool hasInstance,
        IEnumerable<INamedTypeSymbol> registrationServiceTypes)
    {
        ImmutableArray<ServiceSymbolKey> registrations = [.. registrationServiceTypes.Select(serviceType => new ServiceSymbolKey(serviceType, serviceKey))];

        // Same implementation type already registered (e.g., multiple assembly-level IocRegisterFor
        // attributes targeting the same impl type with different keys). Record the additional
        // (service type, key) pairs on the existing entry so SGIOC030 analysis sees all registrations.
        if(analyzerContext.RegisteredServices.TryGetValue(targetType, out var existingInfo))
        {
            existingInfo.AddRegistrations(registrations);
            return;
        }

        var hasKey = serviceKey is not null;
        var serviceInfo = new ServiceInfo(targetType, lifetime, location, serviceKey, keyTypeSymbol, hasKey, hasFactory, hasInstance, registrations);
        if(!analyzerContext.RegisteredServices.TryAdd(targetType, serviceInfo))
        {
            // Another registration of the type was added concurrently
            analyzerContext.RegisteredServices[targetType].AddRegistrations(registrations);
            return;
        }

        // Only build type index for non-keyed services.
        // Keyed services are resolved by key + type, not by type alone,
        // so including them in the type-only index could lead to false positive
        // circular dependency (SGIOC002) or lifetime conflict (SGIOC003-005) diagnostics.
        if(hasKey)
            return;

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
    /// Collects default settings from IoCRegisterDefaultSettingsAttribute on the assembly.
    /// Uses the shared ExtractDefaultSettings method from Constants.
    /// Also tracks duplicated target types for SGIOC012 reporting.
    /// </summary>
    private static DefaultSettingsMap CollectDefaults(
        Compilation compilation,
        IocAttributeSymbols attributeSymbols,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> seenTargetTypes,
        CancellationToken cancellationToken)
    {
        if(attributeSymbols.IocRegisterDefaultsAttribute is null && attributeSymbols.IocRegisterDefaultsAttribute_T1 is null)
            return new DefaultSettingsMap([]);

        var settingsBuilder = ImmutableArray.CreateBuilder<DefaultSettingsModel>();

        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attributeClass = attribute.AttributeClass;
            if(attributeClass is null)
                continue;

            // Check if this is an IoCRegisterDefaultsAttribute (non-generic or generic)
            if(!AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attributeClass, attributeSymbols))
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
                var tags = settings.Tags;

                // Build effective tags list using helper method
                var effectiveTags = AnalyzerHelpers.GetEffectiveTags(tags);

                // SGIOC012: Check each effective tag for duplicates
                var hasDuplicate = false;
                foreach(var tag in effectiveTags)
                {
                    var defaultKey = (targetTypeName, tag);
                    if(!seenTargetTypes.TryAdd(defaultKey, attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()))
                    {
                        hasDuplicate = true;
                        break; // Only need to find one duplicate
                    }
                }

                if(hasDuplicate)
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
            if(TryGetDefaultSettingsIndex(analyzerContext, iface, out var index))
                return defaultSettings[index].Lifetime;
        }

        // Check default settings for matching base classes
        var baseType = targetType.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            if(TryGetDefaultSettingsIndex(analyzerContext, baseType, out var index))
                return defaultSettings[index].Lifetime;

            baseType = baseType.BaseType;
        }

        // Default lifetime is Transient (as defined in TryGetLifetime)
        return explicitLifetime;
    }

    /// <summary>
    /// Finds the default settings for a service type by exact name, then by generic name and arity
    /// (e.g., <c>IGenericTest&lt;&gt;</c> matches <c>IGenericTest&lt;T&gt;</c>). Uses the same names as
    /// the generator's <c>GetTypeData</c> without building the type data.
    /// </summary>
    private static bool TryGetDefaultSettingsIndex(AnalyzerContext analyzerContext, INamedTypeSymbol type, out int index)
    {
        var defaultSettings = analyzerContext.DefaultSettings;
        var name = analyzerContext.TypeNames.GetOrAdd(type, static type => type.BuildTypeName());

        if(defaultSettings.TryGetExactMatches(name, out index))
            return true;

        // Error types have no generic type data
        return type is { IsGenericType: true, TypeKind: not TypeKind.Error }
            && defaultSettings.TryGetGenericMatches(TransformExtensions.GetNameWithoutGeneric(name), type.Arity, out index);
    }

    private readonly struct SymbolLocationComparer : IEqualityComparer<(INamedTypeSymbol Type, Location? Location)>
    {
        public bool Equals((INamedTypeSymbol Type, Location? Location) x, (INamedTypeSymbol Type, Location? Location) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type) && x.Location == y.Location;

        public int GetHashCode((INamedTypeSymbol Type, Location? Location) obj)
            => unchecked((SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397) ^ (obj.Location?.GetHashCode() ?? 0));
    }

    private readonly struct RegistrationKeyComparer : IEqualityComparer<(INamedTypeSymbol Type, string? Key, string Tag)>
    {
        public bool Equals((INamedTypeSymbol Type, string? Key, string Tag) x, (INamedTypeSymbol Type, string? Key, string Tag) y)
            => SymbolEqualityComparer.Default.Equals(x.Type, y.Type)
                && StringComparer.Ordinal.Equals(x.Key, y.Key)
                && StringComparer.Ordinal.Equals(x.Tag, y.Tag);

        public int GetHashCode((INamedTypeSymbol Type, string? Key, string Tag) obj)
            => unchecked((((SymbolEqualityComparer.Default.GetHashCode(obj.Type) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Key ?? string.Empty)) * 397)
                ^ StringComparer.Ordinal.GetHashCode(obj.Tag));
    }

    private sealed class AnalyzerContext(
        IocAttributeSymbols attributeSymbols,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> registeredServices,
        ConcurrentDictionary<INamedTypeSymbol, ServiceInfo> serviceTypeIndex,
        DefaultSettingsMap defaultSettings,
        ConcurrentBag<(string TargetTypeName, Location? Location)> duplicatedDefaults,
        ConcurrentDictionary<(string TargetTypeName, string Tag), Location?> seenDefaultTargetTypes,
        IocFeatures features)
    {
        public IocAttributeSymbols AttributeSymbols { get; } = attributeSymbols;
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
        public ConcurrentDictionary<(INamedTypeSymbol Type, string? Key, string Tag), Location?> RegistrationKeys { get; } = new(new RegistrationKeyComparer());

        /// <summary>
        /// Stores resolved key type symbols for KeyType.Csharp registrations where nameof() was used.
        /// Populated by RegisterSyntaxNodeAction, consumed by SGIOC015 in CompilationEnd.
        /// Key: (TargetType, AttributeLocation) to handle multiple registrations on the same type.
        /// </summary>
        public ConcurrentDictionary<(INamedTypeSymbol Type, Location? Location), ITypeSymbol> ResolvedCsharpKeyTypes { get; } = new(new SymbolLocationComparer());

        /// <summary>
        /// Type names used to match default settings, cached because registrations share interfaces and base classes.
        /// </summary>
        public ConcurrentDictionary<INamedTypeSymbol, string> TypeNames { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Enabled generation features parsed from SourceGenIocFeatures.
        /// </summary>
        public IocFeatures Features { get; } = features;
    }

    private sealed record ServiceInfo
    {
        public INamedTypeSymbol Type { get; }
        public ServiceLifetime Lifetime { get; }
        public Location? Location { get; }

        /// <summary>
        /// The registration key string, or null if the service is unkeyed.
        /// </summary>
        public string? ServiceKey { get; }

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
        /// All (service type, key) pairs from every registration that maps to this implementation type.
        /// Populated during collection to capture all registrations including assembly-level duplicates
        /// where <see cref="RegisteredServices"/> only stores the first entry per implementation type.
        /// Used by SGIOC030 analysis to correctly identify all async-init-only service type/key pairs.
        /// </summary>
        public ImmutableArray<ServiceSymbolKey> AllRegistrations => _allRegistrations;

        private ImmutableArray<ServiceSymbolKey> _allRegistrations;

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
            string? serviceKey,
            ITypeSymbol? keyTypeSymbol,
            bool hasKey,
            bool hasFactory,
            bool hasInstance,
            ImmutableArray<ServiceSymbolKey> registrations)
        {
            Type = type;
            Lifetime = lifetime;
            Location = location;
            _allRegistrations = registrations;
            ServiceKey = serviceKey;
            KeyTypeSymbol = keyTypeSymbol;
            HasKey = hasKey;
            HasFactory = hasFactory;
            HasInstance = hasInstance;

            // Cache constructor lookup
            Constructor = type.SpecifiedOrPrimaryOrMostParametersConstructor;

            // Cache injected members
            InjectedMembers = [.. type.GetInjectedMembers()];
        }

        /// <summary>
        /// Adds the (service type, key) pairs of another registration of this implementation type.
        /// </summary>
        public void AddRegistrations(ImmutableArray<ServiceSymbolKey> registrations)
            => ImmutableInterlocked.Update(ref _allRegistrations, static (current, added) => current.AddRange(added), registrations);
    }
}
