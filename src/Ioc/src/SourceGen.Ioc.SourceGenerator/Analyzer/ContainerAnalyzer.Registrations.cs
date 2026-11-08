using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

partial class ContainerAnalyzer
{
    #region Collection

    /// <summary>
    /// Collects the [IocRegister] / [IocRegisterFor] registrations, type-level [IocRegisterDefaults] and
    /// [IocImportModule] attributes of a named type.
    /// </summary>
    private static void CollectRegistrationAttributes(SymbolAnalysisContext context, ContainerAnalyzerContext analyzerContext)
    {
        if(context.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        var attributeSymbols = analyzerContext.AttributeSymbols;
        foreach(var attribute in typeSymbol.GetAttributes())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            if(AnalyzerHelpers.IsIoCRegisterAttribute(attrClass, attributeSymbols))
            {
                analyzerContext.Registrations.Add(CreateRawRegistration(typeSymbol, attribute, explicitContainer: null));
            }
            else if(AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, attributeSymbols))
            {
                // [IocRegisterFor] on a container class is also an explicit registration of that container (ExplicitOnly)
                var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
                if(targetType is not null)
                    analyzerContext.Registrations.Add(CreateRawRegistration(targetType, attribute, explicitContainer: typeSymbol));
            }
            else if(AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attrClass, attributeSymbols))
            {
                if(CreateDefaultSettings(attribute) is { } defaults)
                    analyzerContext.DefaultSettings.Add(defaults);
            }
            else if(AnalyzerHelpers.IsIocImportModuleAttribute(attrClass, attributeSymbols))
            {
                if(attribute.GetImportedModuleType() is { } moduleType)
                    analyzerContext.ModuleImports.Add(new ModuleImport(moduleType, attribute));
            }
        }
    }

    /// <summary>
    /// Collects assembly-level [IocRegisterFor] registrations and [IocRegisterDefaults] settings.
    /// </summary>
    private static void CollectAssemblyLevelAttributes(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var attributeSymbols = analyzerContext.AttributeSymbols;
        foreach(var attribute in compilation.Assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var attrClass = attribute.AttributeClass;
            if(attrClass is null)
                continue;

            if(AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, attributeSymbols))
            {
                var targetType = attribute.GetTargetTypeFromRegisterForAttribute();
                if(targetType is not null)
                    analyzerContext.Registrations.Add(CreateRawRegistration(targetType, attribute, explicitContainer: null));
            }
            else if(AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attrClass, attributeSymbols))
            {
                if(CreateDefaultSettings(attribute) is { } defaults)
                    analyzerContext.DefaultSettings.Add(defaults);
            }
        }
    }

    private static RawRegistration CreateRawRegistration(
        INamedTypeSymbol implementationType,
        AttributeData attribute,
        INamedTypeSymbol? explicitContainer)
    {
        var (key, _, _) = attribute.GetKeyInfo();
        var (hasRegisterAllInterfaces, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        var (hasRegisterAllBaseClasses, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();

        return new RawRegistration(
            implementationType,
            key,
            [.. AnalyzerHelpers.GetExplicitServiceTypes(attribute)],
            hasRegisterAllInterfaces,
            registerAllInterfaces,
            hasRegisterAllBaseClasses,
            registerAllBaseClasses,
            [.. attribute.GetTags()],
            explicitContainer);
    }

    /// <summary>
    /// Reads an [IocRegisterDefaults] / [IocRegisterDefaults&lt;T&gt;] attribute.
    /// Mirrors <c>ExtractDefaultSettings</c> / <c>ExtractDefaultSettingsFromGenericAttribute</c>.
    /// </summary>
    private static DefaultSettings? CreateDefaultSettings(AttributeData attribute)
    {
        var attrClass = attribute.AttributeClass;
        if(attrClass is null)
            return null;

        INamedTypeSymbol? targetType;
        if(attrClass.IsGenericType)
        {
            if(attrClass.TypeArguments.Length == 0 || attribute.ConstructorArguments.Length < 1)
                return null;

            targetType = attrClass.TypeArguments[0] as INamedTypeSymbol;
        }
        else
        {
            if(attribute.ConstructorArguments.Length < 2)
                return null;

            targetType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        }

        if(targetType is null)
            return null;

        var (_, registerAllInterfaces) = attribute.TryGetRegisterAllInterfaces();
        var (_, registerAllBaseClasses) = attribute.TryGetRegisterAllBaseClasses();

        return new DefaultSettings(
            targetType,
            [.. attribute.GetServiceTypeSymbols()],
            registerAllInterfaces,
            registerAllBaseClasses,
            [.. attribute.GetTags()],
            [.. attribute.GetImplementationTypeSymbols()],
            attrClass.IsGenericType,
            attribute.ApplicationSyntaxReference);
    }

    #endregion

    #region Registration resolution

    /// <summary>
    /// Applies default settings to every collected registration, in the same way as the generator's
    /// <c>ProcessSingleRegistration</c>: matching defaults add service types, tags, and RegisterAllInterfaces /
    /// RegisterAllBaseClasses values the registration does not set.
    /// </summary>
    private static List<ResolvedRegistration> ResolveRegistrations(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var defaults = BuildDefaultSettingsLookup(compilation, analyzerContext, cancellationToken);

        var resolved = new List<ResolvedRegistration>();
        var matched = new List<DefaultSettings>();
        foreach(var registration in analyzerContext.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var implementationType = registration.ImplementationType;
            var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { implementationType };
            AddExplicitServiceTypes(serviceTypes, implementationType, registration.ExplicitServiceTypes);

            // Base classes are matched before interfaces; the first match is the best default
            matched.Clear();
            DefaultSettings? best = null;
            if(!defaults.IsEmpty)
            {
                foreach(var candidate in EnumerateBaseClasses(implementationType))
                    MatchDefaults(candidate);

                foreach(var candidate in implementationType.AllInterfaces)
                    MatchDefaults(candidate);
            }

            if(best is not null)
                AddExplicitServiceTypes(serviceTypes, implementationType, best.ServiceTypes);

            var registerAllInterfaces = registration.HasExplicitRegisterAllInterfaces
                ? registration.RegisterAllInterfaces
                : best?.RegisterAllInterfaces ?? registration.RegisterAllInterfaces;
            if(registerAllInterfaces)
                serviceTypes.UnionWith(implementationType.AllInterfaces);

            var registerAllBaseClasses = registration.HasExplicitRegisterAllBaseClasses
                ? registration.RegisterAllBaseClasses
                : best?.RegisterAllBaseClasses ?? registration.RegisterAllBaseClasses;
            if(registerAllBaseClasses)
                serviceTypes.UnionWith(EnumerateBaseClasses(implementationType));

            var tags = registration.Tags.Length > 0
                ? registration.Tags
                : best?.Tags ?? registration.Tags;

            resolved.Add(new ResolvedRegistration(
                implementationType,
                registration.Key,
                [.. serviceTypes],
                tags,
                registration.ExplicitContainer));

            void MatchDefaults(INamedTypeSymbol candidate)
            {
                if(defaults.TryGetExact(candidate, out var exact) && !matched.Contains(exact))
                {
                    matched.Add(exact);
                    serviceTypes.Add(candidate);
                    best ??= exact;
                }

                if(candidate.IsGenericType
                    && defaults.TryGetGeneric(candidate, out var generic)
                    && !matched.Contains(generic))
                {
                    matched.Add(generic);
                    serviceTypes.Add(candidate);
                    best ??= generic;
                }
            }
        }

        AddDefaultSettingsImplementationTypes(resolved, analyzerContext, cancellationToken);

        return resolved;
    }

    /// <summary>
    /// Adds the registrations of <c>[IocRegisterDefaults(ImplementationTypes = ...)]</c> in the current assembly.
    /// Mirrors the generator's <c>CreateRegistrationDataFromDefaultSettings</c>: the settings of the attribute apply
    /// directly, without a default settings lookup.
    /// </summary>
    private static void AddDefaultSettingsImplementationTypes(
        List<ResolvedRegistration> resolved,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        foreach(var defaults in analyzerContext.DefaultSettings)
        {
            foreach(var implementationType in defaults.ImplementationTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A closed implementation is registered as the types of its hierarchy named like the target or the
                // service types; when none matches (e.g. Razor components), as the target and service types themselves.
                var declaredServiceTypes = IsOpenGeneric(implementationType)
                    ? []
                    : FindServiceTypesByName(implementationType, defaults);
                if(declaredServiceTypes.Count == 0)
                    declaredServiceTypes = [.. defaults.ServiceTypes, defaults.TargetType];

                var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { implementationType };
                AddExplicitServiceTypes(serviceTypes, implementationType, declaredServiceTypes);

                if(defaults.RegisterAllInterfaces)
                    serviceTypes.UnionWith(implementationType.AllInterfaces);

                if(defaults.RegisterAllBaseClasses)
                    serviceTypes.UnionWith(EnumerateBaseClasses(implementationType));

                resolved.Add(new ResolvedRegistration(
                    implementationType,
                    Key: null,
                    [.. serviceTypes],
                    defaults.Tags,
                    ExplicitContainer: null));
            }
        }

        // Mirrors FindClosedServiceTypesFromImplementation, which compares names without generic arguments
        static List<INamedTypeSymbol> FindServiceTypesByName(INamedTypeSymbol implementationType, DefaultSettings defaults)
        {
            var result = new List<INamedTypeSymbol>();
            foreach(var target in defaults.ServiceTypes.Prepend(defaults.TargetType))
            {
                foreach(var candidate in implementationType.AllInterfaces.Concat(EnumerateBaseClasses(implementationType)))
                {
                    if(candidate.Name == target.Name
                        && SymbolEqualityComparer.Default.Equals(candidate.ContainingSymbol, target.ContainingSymbol))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Adds explicit service types like the generator's <c>AddExplicitServiceType</c>: a closed implementation is
    /// registered as each closed form of an open generic service type that it implements or inherits
    /// (e.g. <c>typeof(IHandler&lt;&gt;)</c> for <c>Handler : IHandler&lt;string&gt;</c>).
    /// Without such a form, the open type is kept.
    /// </summary>
    private static void AddExplicitServiceTypes(
        HashSet<INamedTypeSymbol> serviceTypes,
        INamedTypeSymbol implementationType,
        IEnumerable<INamedTypeSymbol> explicitServiceTypes)
    {
        var isClosedImplementation = !IsOpenGeneric(implementationType);
        foreach(var serviceType in explicitServiceTypes)
        {
            if(isClosedImplementation && serviceType.IsUnboundGenericType)
            {
                var closed = false;
                foreach(var candidate in implementationType.AllInterfaces.Concat(EnumerateBaseClasses(implementationType)))
                {
                    if(candidate.IsGenericType
                        && SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, serviceType.OriginalDefinition))
                    {
                        serviceTypes.Add(candidate);
                        closed = true;
                    }
                }

                if(closed)
                    continue;
            }

            serviceTypes.Add(serviceType);
        }
    }

    private static bool IsOpenGeneric(INamedTypeSymbol type)
        => type.IsUnboundGenericType
            || (type.IsGenericType && type.TypeArguments.Any(static arg => arg.TypeKind == TypeKind.TypeParameter));

    /// <summary>
    /// Builds the default settings lookup with the generator's precedence: settings of the current assembly
    /// (non-generic attributes, then generic attributes, each in source order), then settings of imported module
    /// assemblies. The first settings for a target type win.
    /// </summary>
    private static DefaultSettingsLookup BuildDefaultSettingsLookup(
        Compilation compilation,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var lookup = new DefaultSettingsLookup();
        var treeOrder = new Dictionary<SyntaxTree, int>();
        var treeIndex = 0;
        foreach(var tree in compilation.SyntaxTrees)
            treeOrder[tree] = treeIndex++;

        foreach(var defaults in analyzerContext.DefaultSettings
                     .OrderBy(static d => d.IsGeneric)
                     .ThenBy(d => GetSourceOrder(d.Syntax, treeOrder)))
        {
            lookup.Add(defaults);
        }

        // Imported module assemblies: settings on the module type, then assembly-level settings
        foreach(var import in analyzerContext.ModuleImports
                     .OrderBy(static i => i.Attribute.AttributeClass?.IsGenericType == true)
                     .ThenBy(i => GetSourceOrder(i.Attribute.ApplicationSyntaxReference, treeOrder)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var moduleType = import.ModuleType.IsGenericType ? import.ModuleType.OriginalDefinition : import.ModuleType;
            var moduleAssembly = moduleType.ContainingAssembly;
            if(moduleAssembly is null || SymbolEqualityComparer.Default.Equals(moduleAssembly, compilation.Assembly))
                continue;

            AddImportedDefaults(moduleType.GetAttributes());
            AddImportedDefaults(moduleAssembly.GetAttributes());
        }

        return lookup;

        void AddImportedDefaults(ImmutableArray<AttributeData> attributes)
        {
            foreach(var attribute in attributes)
            {
                if(attribute.AttributeClass is { } attrClass
                    && AnalyzerHelpers.IsIoCRegisterDefaultsAttribute(attrClass, analyzerContext.AttributeSymbols)
                    && CreateDefaultSettings(attribute) is { } defaults)
                {
                    lookup.Add(defaults);
                }
            }
        }
    }

    private static (int Tree, int Position) GetSourceOrder(SyntaxReference? syntax, Dictionary<SyntaxTree, int> treeOrder)
    {
        if(syntax is null)
            return (int.MaxValue, int.MaxValue);

        return (treeOrder.TryGetValue(syntax.SyntaxTree, out var index) ? index : int.MaxValue, syntax.Span.Start);
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateBaseClasses(INamedTypeSymbol type)
    {
        var baseType = type.BaseType;
        while(baseType is not null && baseType.SpecialType is not SpecialType.System_Object)
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }
    }

    #endregion

    #region Container scopes

    /// <summary>
    /// Computes the registrations of each container the way the generator selects them
    /// (ExplicitOnly → [IocRegisterFor] on the container, otherwise all registrations filtered by IncludeTags),
    /// plus the services of its imported modules.
    /// </summary>
    private sealed class ContainerScopeBuilder(
        List<ResolvedRegistration> registrations,
        Dictionary<INamedTypeSymbol, ContainerInfo> containers,
        IAssemblySymbol assembly)
    {
        private readonly Dictionary<INamedTypeSymbol, ContainerScope> _scopes = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _building = new(SymbolEqualityComparer.Default);
        private RegistrationSet? _allRegistrations;

        public ContainerScope GetScope(ContainerInfo container)
        {
            if(_scopes.TryGetValue(container.Symbol, out var scope))
                return scope;

            _building.Add(container.Symbol);

            var own = GetOwnRegistrations(container);
            var serviceTypes = own.ServiceTypes;
            var hasExternalModule = false;

            if(container.ImportedModules.Length > 0)
            {
                serviceTypes = new HashSet<INamedTypeSymbol>(own.ServiceTypes, SymbolEqualityComparer.Default);
                foreach(var module in container.ImportedModules)
                {
                    if(!SymbolEqualityComparer.Default.Equals(module.ContainingAssembly, assembly))
                    {
                        // Registrations of another assembly cannot be enumerated
                        hasExternalModule = true;
                        continue;
                    }

                    // Circular imports are reported by SGIOC025
                    if(_building.Contains(module) || !containers.TryGetValue(module, out var moduleContainer))
                        continue;

                    var moduleScope = GetScope(moduleContainer);
                    serviceTypes.UnionWith(moduleScope.ServiceTypes);
                    hasExternalModule |= moduleScope.HasExternalModule;
                }
            }

            _building.Remove(container.Symbol);

            scope = new ContainerScope(own, serviceTypes, hasExternalModule);
            _scopes[container.Symbol] = scope;
            return scope;
        }

        /// <summary>
        /// Gets whether the container uses every registration of the compilation, so it can share analysis results.
        /// </summary>
        public static bool UsesAllRegistrations(ContainerInfo container)
            => !container.ExplicitOnly && container.IncludeTags.IsEmpty && container.ImportedModules.IsEmpty;

        private RegistrationSet GetOwnRegistrations(ContainerInfo container)
        {
            if(container.ExplicitOnly)
            {
                return new RegistrationSet(registrations
                    .Where(r => SymbolEqualityComparer.Default.Equals(r.ExplicitContainer, container.Symbol))
                    .ToList());
            }

            if(container.IncludeTags.IsEmpty)
                return _allRegistrations ??= new RegistrationSet(registrations);

            var includeTags = new HashSet<string>(container.IncludeTags, StringComparer.Ordinal);
            return new RegistrationSet(registrations
                .Where(r => r.Tags.Any(includeTags.Contains))
                .ToList());
        }
    }

    /// <summary>
    /// The registrations of a container (without imported modules) and the service types they expose.
    /// </summary>
    private sealed class RegistrationSet
    {
        public RegistrationSet(List<ResolvedRegistration> registrations)
        {
            Registrations = registrations;
            foreach(var registration in registrations)
            {
                foreach(var serviceType in registration.ServiceTypes)
                {
                    ServiceTypes.Add(serviceType);
                    ServiceKeys.Add(new(serviceType, registration.Key));
                }
            }
        }

        public List<ResolvedRegistration> Registrations { get; }

        public HashSet<INamedTypeSymbol> ServiceTypes { get; } = new(SymbolEqualityComparer.Default);

        public HashSet<ServiceSymbolKey> ServiceKeys { get; } = [];
    }

    /// <param name="Own">The registrations of the container itself.</param>
    /// <param name="ServiceTypes">The service types of the container, including the services of imported modules.</param>
    /// <param name="HasExternalModule">Whether the container imports a module of another assembly (directly or transitively).</param>
    private sealed record class ContainerScope(
        RegistrationSet Own,
        HashSet<INamedTypeSymbol> ServiceTypes,
        bool HasExternalModule);

    #endregion

    #region SGIOC018

    /// <summary>
    /// SGIOC018: Collects the unregistered constructor and injected member dependencies of the implementation types
    /// registered in a container. Service types that are not implementation types (interfaces, base classes) are not
    /// analyzed, because the generator only constructs and injects the implementation type.
    /// Implementations of imported modules are analyzed as part of their own container.
    /// </summary>
    private static List<(Location? Location, string ServiceType)> CollectUnresolvedDependencies(
        ContainerScope scope,
        DependencyCache dependencyCache,
        CancellationToken cancellationToken)
    {
        var unresolvedDependencies = new List<(Location? Location, string ServiceType)>();
        var implementationTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach(var registration in scope.Own.Registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!implementationTypes.Add(registration.ImplementationType))
                continue;

            foreach(var (location, dependencyType) in dependencyCache.Get(registration.ImplementationType, cancellationToken))
            {
                if(!IsServiceRegistered(dependencyType, scope.ServiceTypes))
                {
                    unresolvedDependencies.Add((
                        location,
                        dependencyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }
            }
        }

        return unresolvedDependencies;
    }

    private static bool IsServiceRegistered(INamedTypeSymbol serviceType, HashSet<INamedTypeSymbol> serviceTypes)
    {
        if(serviceTypes.Contains(serviceType))
            return true;

        // IEnumerable<T> is resolvable if T is registered
        if(AnalyzerHelpers.IsIEnumerableOfT(serviceType)
            && AnalyzerHelpers.GetEnumerableElementType(serviceType) is { } elementType)
        {
            return serviceTypes.Contains(elementType);
        }

        return false;
    }

    /// <summary>
    /// Caches the constructor and injected member dependencies of implementation types that need a registration
    /// (dependencies that are always resolvable are excluded), so each type is inspected once per compilation.
    /// </summary>
    private sealed class DependencyCache
    {
        private readonly Dictionary<INamedTypeSymbol, List<(Location? Location, INamedTypeSymbol Type)>> _cache =
            new(SymbolEqualityComparer.Default);

        public List<(Location? Location, INamedTypeSymbol Type)> Get(INamedTypeSymbol implementationType, CancellationToken cancellationToken)
        {
            if(_cache.TryGetValue(implementationType, out var dependencies))
                return dependencies;

            dependencies = [];

            var constructor = implementationType.SpecifiedOrPrimaryOrMostParametersConstructor;
            if(constructor is not null)
                AddParameters(constructor.Parameters);

            foreach(var (member, injectAttribute) in implementationType.GetInjectedMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch(member)
                {
                    case IPropertySymbol property
                        when !AnalyzerHelpers.IsPropertyAlwaysResolvable(property, injectAttribute)
                             && property.Type is INamedTypeSymbol propertyType
                             && !AnalyzerHelpers.IsAlwaysResolvable(propertyType):
                        dependencies.Add((GetInjectLocation(injectAttribute, property), propertyType));
                        break;

                    case IMethodSymbol method:
                        AddParameters(method.Parameters);
                        break;

                    case IFieldSymbol field
                        when !AnalyzerHelpers.IsFieldAlwaysResolvable(field, injectAttribute)
                             && field.Type is INamedTypeSymbol fieldType
                             && !AnalyzerHelpers.IsAlwaysResolvable(fieldType):
                        dependencies.Add((GetInjectLocation(injectAttribute, field), fieldType));
                        break;
                }
            }

            _cache[implementationType] = dependencies;
            return dependencies;

            void AddParameters(ImmutableArray<IParameterSymbol> parameters)
            {
                foreach(var parameter in parameters)
                {
                    if(!AnalyzerHelpers.IsParameterAlwaysResolvable(parameter)
                        && parameter.Type is INamedTypeSymbol parameterType
                        && !AnalyzerHelpers.IsAlwaysResolvable(parameterType))
                    {
                        dependencies.Add((parameter.Locations.FirstOrDefault(), parameterType));
                    }
                }
            }

            Location? GetInjectLocation(AttributeData injectAttribute, ISymbol member)
                => injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                    ?? member.Locations.FirstOrDefault();
        }
    }

    #endregion

    #region Models

    /// <param name="ExplicitContainer">
    /// The type an [IocRegisterFor] attribute is applied to; it is an explicit registration when that type is an
    /// ExplicitOnly container. <see langword="null"/> for [IocRegister] and assembly-level registrations.
    /// </param>
    private sealed record class RawRegistration(
        INamedTypeSymbol ImplementationType,
        string? Key,
        ImmutableArray<INamedTypeSymbol> ExplicitServiceTypes,
        bool HasExplicitRegisterAllInterfaces,
        bool RegisterAllInterfaces,
        bool HasExplicitRegisterAllBaseClasses,
        bool RegisterAllBaseClasses,
        ImmutableArray<string> Tags,
        INamedTypeSymbol? ExplicitContainer);

    private sealed record class ResolvedRegistration(
        INamedTypeSymbol ImplementationType,
        string? Key,
        ImmutableArray<INamedTypeSymbol> ServiceTypes,
        ImmutableArray<string> Tags,
        INamedTypeSymbol? ExplicitContainer);

    /// <summary>
    /// Settings of an [IocRegisterDefaults] attribute. Compared by reference, like the generator's default settings indices.
    /// </summary>
    private sealed class DefaultSettings(
        INamedTypeSymbol targetType,
        ImmutableArray<INamedTypeSymbol> serviceTypes,
        bool registerAllInterfaces,
        bool registerAllBaseClasses,
        ImmutableArray<string> tags,
        ImmutableArray<INamedTypeSymbol> implementationTypes,
        bool isGeneric,
        SyntaxReference? syntax)
    {
        public INamedTypeSymbol TargetType { get; } = targetType;
        public ImmutableArray<INamedTypeSymbol> ServiceTypes { get; } = serviceTypes;
        public bool RegisterAllInterfaces { get; } = registerAllInterfaces;
        public bool RegisterAllBaseClasses { get; } = registerAllBaseClasses;
        public ImmutableArray<string> Tags { get; } = tags;

        /// <summary>
        /// Implementation types registered with these settings. Only registrations of the current assembly are
        /// created; the generator ignores them for settings imported from a module assembly.
        /// </summary>
        public ImmutableArray<INamedTypeSymbol> ImplementationTypes { get; } = implementationTypes;
        public bool IsGeneric { get; } = isGeneric;
        public SyntaxReference? Syntax { get; } = syntax;
    }

    /// <summary>
    /// Mirrors the generator's <c>DefaultSettingsMap</c>: exact matches by type, and generic matches by generic
    /// type definition. The first settings added for a key win.
    /// </summary>
    private sealed class DefaultSettingsLookup
    {
        private readonly Dictionary<INamedTypeSymbol, DefaultSettings> _exact = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<INamedTypeSymbol, DefaultSettings> _generic = new(SymbolEqualityComparer.Default);

        public bool IsEmpty => _exact.Count == 0;

        public void Add(DefaultSettings defaults)
        {
            var targetType = defaults.TargetType;
            if(!_exact.ContainsKey(targetType))
                _exact[targetType] = defaults;

            if(targetType.IsGenericType && !_generic.ContainsKey(targetType.OriginalDefinition))
                _generic[targetType.OriginalDefinition] = defaults;
        }

        public bool TryGetExact(INamedTypeSymbol type, out DefaultSettings defaults)
            => _exact.TryGetValue(type, out defaults!);

        public bool TryGetGeneric(INamedTypeSymbol type, out DefaultSettings defaults)
            => _generic.TryGetValue(type.OriginalDefinition, out defaults!);
    }

    private readonly record struct ModuleImport(INamedTypeSymbol ModuleType, AttributeData Attribute);

    private sealed record class ContainerInfo(
        INamedTypeSymbol Symbol,
        bool IntegrateServiceProvider,
        bool ExplicitOnly,
        ImmutableArray<string> IncludeTags,
        ImmutableArray<INamedTypeSymbol> ImportedModules);

    private sealed class ContainerAnalyzerContext(IocAttributeSymbols attributeSymbols, IocFeatures features)
    {
        public IocAttributeSymbols AttributeSymbols { get; } = attributeSymbols;
        public IocFeatures Features { get; } = features;
        public ConcurrentBag<RawRegistration> Registrations { get; } = [];
        public ConcurrentBag<DefaultSettings> DefaultSettings { get; } = [];
        public ConcurrentBag<ModuleImport> ModuleImports { get; } = [];
        public ConcurrentBag<ContainerInfo> Containers { get; } = [];
        public ConcurrentBag<(INamedTypeSymbol Container, INamedTypeSymbol Module)> ImportEdges { get; } = [];
    }

    #endregion
}
