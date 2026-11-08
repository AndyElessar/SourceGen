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
            GetAttributeServiceKey(attribute),
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
            attribute.NamedArguments.Any(static a => a.Key == "Factory" && !a.Value.IsNull),
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
                registration.KeyValue,
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
        AddFactoryOpenGenericRegistrations(resolved, analyzerContext);
        AddImportedOpenGenericRegistrations(resolved, analyzerContext, cancellationToken);

        return resolved;
    }

    /// <summary>
    /// Adds the open generic registrations of current-assembly <c>[IocRegisterDefaults]</c> with a <c>Factory</c> and an
    /// open generic target type, like the generator's <c>CreateOpenGenericEntriesFromDefaultSettings</c>.
    /// </summary>
    private static void AddFactoryOpenGenericRegistrations(
        List<ResolvedRegistration> resolved,
        ContainerAnalyzerContext analyzerContext)
    {
        foreach(var defaults in analyzerContext.DefaultSettings)
        {
            if(!defaults.HasFactory || !IsOpenGeneric(defaults.TargetType))
                continue;

            var targetType = defaults.TargetType.OriginalDefinition;
            resolved.Add(new ResolvedRegistration(
                targetType,
                Key: null,
                KeyValue: null,
                [targetType],
                defaults.Tags,
                ExplicitContainer: null));
        }
    }

    /// <summary>
    /// Adds the open generic <c>[IocRegisterFor]</c> registrations of every imported module and, recursively, of the
    /// modules it imports. Like the generator's <c>ExtractOpenGenericEntriesFromModuleRecursive</c>, they are added to
    /// the registrations of the current assembly for every [IocImportModule] of the compilation; they expose the open
    /// generic <c>ServiceTypes</c> and interfaces of the implementation.
    /// </summary>
    private static void AddImportedOpenGenericRegistrations(
        List<ResolvedRegistration> resolved,
        ContainerAnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        var attributeSymbols = analyzerContext.AttributeSymbols;
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach(var import in analyzerContext.ModuleImports)
            AddModule(import.ModuleType);

        void AddModule(INamedTypeSymbol moduleType)
        {
            if(!visited.Add(moduleType))
                return;

            foreach(var attribute in moduleType.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if(attribute.AttributeClass is not { } attrClass)
                    continue;

                if(AnalyzerHelpers.IsIocImportModuleAttribute(attrClass, attributeSymbols))
                {
                    if(attribute.GetImportedModuleType() is { } importedModule)
                        AddModule(importedModule);

                    continue;
                }

                if(!AnalyzerHelpers.IsIoCRegisterForAttribute(attrClass, attributeSymbols)
                    || attribute.GetTargetTypeFromRegisterForAttribute() is not { } targetType
                    || !IsOpenGeneric(targetType))
                {
                    continue;
                }

                var implementationType = targetType.OriginalDefinition;
                var explicitServiceTypes = AnalyzerHelpers.GetExplicitServiceTypes(attribute);
                var serviceTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                foreach(var serviceType in explicitServiceTypes.Count > 0
                             ? explicitServiceTypes.Concat(implementationType.AllInterfaces)
                             : implementationType.AllInterfaces)
                {
                    if(IsOpenGeneric(serviceType))
                        serviceTypes.Add(serviceType.OriginalDefinition);
                }

                if(serviceTypes.Count == 0)
                    continue;

                var (key, _, _) = attribute.GetKeyInfo();
                resolved.Add(new ResolvedRegistration(
                    implementationType,
                    key,
                    GetAttributeServiceKey(attribute),
                    [.. serviceTypes],
                    [.. attribute.GetTags()],
                    ExplicitContainer: null));
            }
        }
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
                    KeyValue: null,
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
            var services = own.Services;
            var hasExternalModule = false;

            if(container.ImportedModules.Length > 0)
            {
                services = new ServiceIndex();
                services.UnionWith(own.Services);
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
                    services.UnionWith(moduleScope.Services);
                    hasExternalModule |= moduleScope.HasExternalModule;
                }
            }

            _building.Remove(container.Symbol);

            scope = new ContainerScope(own, services, hasExternalModule);
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
                return new RegistrationSet(
                    registrations
                        .Where(r => SymbolEqualityComparer.Default.Equals(r.ExplicitContainer, container.Symbol))
                        .ToList(),
                    includeOpenGenerics: false);
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
        /// <param name="includeOpenGenerics">Whether open generic registrations resolve their closed forms (not in ExplicitOnly containers).</param>
        public RegistrationSet(List<ResolvedRegistration> registrations, bool includeOpenGenerics = true)
        {
            Registrations = registrations;
            foreach(var registration in registrations)
            {
                foreach(var serviceType in registration.ServiceTypes)
                {
                    Services.Add(serviceType, registration.KeyValue, includeOpenGenerics);
                    ServiceKeys.Add(new(serviceType, registration.Key));
                }
            }
        }

        public List<ResolvedRegistration> Registrations { get; }

        /// <summary>The services by key value (SGIOC018 and collection checks).</summary>
        public ServiceIndex Services { get; } = new();

        /// <summary>The services by key expression (SGIOC021, which compares keys like the generator's accessor lookup).</summary>
        public HashSet<ServiceSymbolKey> ServiceKeys { get; } = [];
    }

    /// <param name="Own">The registrations of the container itself.</param>
    /// <param name="Services">The services of the container, including the services of imported modules.</param>
    /// <param name="HasExternalModule">Whether the container imports a module of another assembly (directly or transitively).</param>
    private sealed record class ContainerScope(
        RegistrationSet Own,
        ServiceIndex Services,
        bool HasExternalModule);

    /// <summary>
    /// Registered services by type and key value. Generated containers look up keyed services by key value at runtime
    /// (<c>ServiceIdentifier</c>), so keys are compared by constant value; a key given as C# code
    /// (<c>KeyType = KeyType.Csharp</c>) cannot be evaluated and matches any key.
    /// </summary>
    private sealed class ServiceIndex
    {
        private readonly HashSet<INamedTypeSymbol> _nonKeyed = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _keyed = new(SymbolEqualityComparer.Default);
        private readonly HashSet<INamedTypeSymbol> _unknownKeyed = new(SymbolEqualityComparer.Default);
        private readonly HashSet<KeyedService> _keyedValues = [];

        /// <summary>
        /// Adds a service. An open generic service type is added as its generic type definition, which resolves every
        /// closed form (the generator closes open generic registrations on demand); it is skipped when
        /// <paramref name="includeOpenGenerics"/> is <see langword="false"/> (ExplicitOnly containers do not close them).
        /// </summary>
        public void Add(INamedTypeSymbol serviceType, ServiceKeyValue? key, bool includeOpenGenerics)
        {
            if(IsOpenGeneric(serviceType))
            {
                if(!includeOpenGenerics)
                    return;

                serviceType = serviceType.OriginalDefinition;
            }

            if(key is not { } keyValue)
            {
                _nonKeyed.Add(serviceType);
                return;
            }

            _keyed.Add(serviceType);
            if(keyValue.IsUnknown)
                _unknownKeyed.Add(serviceType);
            else
                _keyedValues.Add(new(serviceType, keyValue));
        }

        public void UnionWith(ServiceIndex other)
        {
            _nonKeyed.UnionWith(other._nonKeyed);
            _keyed.UnionWith(other._keyed);
            _unknownKeyed.UnionWith(other._unknownKeyed);
            _keyedValues.UnionWith(other._keyedValues);
        }

        /// <summary>
        /// Gets whether a service is registered. A closed generic type is also resolved by an open generic registration
        /// of its generic type definition when <paramref name="matchOpenGenerics"/> is <see langword="true"/>.
        /// </summary>
        public bool Contains(INamedTypeSymbol serviceType, ServiceKeyValue? key, bool matchOpenGenerics = true)
        {
            if(ContainsExact(serviceType, key))
                return true;

            return matchOpenGenerics
                && serviceType.IsGenericType
                && !SymbolEqualityComparer.Default.Equals(serviceType, serviceType.OriginalDefinition)
                && ContainsExact(serviceType.OriginalDefinition, key);
        }

        private bool ContainsExact(INamedTypeSymbol serviceType, ServiceKeyValue? key)
        {
            if(key is not { } keyValue)
                return _nonKeyed.Contains(serviceType);

            // A key given as C# code is matched by any registration of the type
            if(keyValue.IsUnknown)
                return _keyed.Contains(serviceType) || _nonKeyed.Contains(serviceType);

            return _keyedValues.Contains(new(serviceType, keyValue)) || _unknownKeyed.Contains(serviceType);
        }
    }

    private readonly record struct KeyedService(INamedTypeSymbol ServiceType, ServiceKeyValue Key)
    {
        public bool Equals(KeyedService other) =>
            SymbolEqualityComparer.Default.Equals(ServiceType, other.ServiceType) && Key.Equals(other.Key);

        public override int GetHashCode() =>
            unchecked((SymbolEqualityComparer.Default.GetHashCode(ServiceType) * 397) ^ Key.GetHashCode());
    }

    /// <summary>
    /// A service key: the constant value and its type, or an unknown value for a key given as C# code.
    /// </summary>
    /// <param name="Display">The key as C# code, for diagnostic messages.</param>
    private readonly record struct ServiceKeyValue(ITypeSymbol? Type, object? Value, bool IsUnknown, string Display)
    {
        public static ServiceKeyValue FromConstant(TypedConstant constant)
            => new(constant.Type, constant.Value, IsUnknown: false, constant.ToCSharpString());

        public static ServiceKeyValue Unknown(string display)
            => new(Type: null, Value: null, IsUnknown: true, display);

        public bool Equals(ServiceKeyValue other) =>
            IsUnknown == other.IsUnknown
            && SymbolEqualityComparer.Default.Equals(Type, other.Type)
            && object.Equals(Value, other.Value);

        public override int GetHashCode() =>
            unchecked((SymbolEqualityComparer.Default.GetHashCode(Type) * 397) ^ (Value?.GetHashCode() ?? 0) ^ (IsUnknown ? 1 : 0));
    }

    /// <summary>
    /// Gets the key of a registration or [IocInject] attribute, with the same argument rules as <c>GetKeyInfo</c>:
    /// the first constructor argument (unless it is a lifetime, type or array), otherwise the <c>Key</c> named argument.
    /// </summary>
    private static ServiceKeyValue? GetAttributeServiceKey(AttributeData attribute)
    {
        TypedConstant? key = null;
        if(attribute.ConstructorArguments.Length > 0)
        {
            var ctorArg = attribute.ConstructorArguments[0];
            if(ctorArg.Type?.Name != nameof(ServiceLifetime)
                && ctorArg.Kind is not (TypedConstantKind.Type or TypedConstantKind.Array)
                && !ctorArg.IsNull)
            {
                key = ctorArg;
            }
        }

        if(key is null)
        {
            foreach(var namedArg in attribute.NamedArguments)
            {
                if(namedArg.Key == "Key" && !namedArg.Value.IsNull)
                {
                    key = namedArg.Value;
                    break;
                }
            }
        }

        if(key is not { } keyConstant)
            return null;

        var (_, keyType) = attribute.TryGetNamedArgument<int>("KeyType", 0);
        return keyType == 1
            ? ServiceKeyValue.Unknown(keyConstant.Value?.ToString() ?? "null")
            : ServiceKeyValue.FromConstant(keyConstant);
    }

    #endregion

    #region SGIOC018

    /// <summary>
    /// SGIOC018: Collects the unregistered constructor and injected member dependencies of the implementation types
    /// registered in a container. Service types that are not implementation types (interfaces, base classes) are not
    /// analyzed, because the generator only constructs and injects the implementation type.
    /// Implementations of imported modules are analyzed as part of their own container, and implementations declared in
    /// another assembly are not analyzed. Dependencies that use type parameters of an open generic implementation are
    /// not analyzed, because they depend on the closed forms the generator creates.
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

            if(!implementationTypes.Add(registration.ImplementationType)
                || !registration.ImplementationType.Locations.Any(static l => l.IsInSource))
            {
                continue;
            }

            foreach(var dependency in dependencyCache.Get(registration.ImplementationType, cancellationToken))
            {
                if(!ContainsTypeParameter(dependency.Type)
                    && !IsServiceRegistered(dependency.Type, dependency.Key, scope.Services))
                {
                    var serviceType = dependency.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    unresolvedDependencies.Add((
                        dependency.Location,
                        dependency.Key is { } key ? $"{serviceType} (key {key.Display})" : serviceType));
                }
            }
        }

        return unresolvedDependencies;
    }

    private static bool IsServiceRegistered(INamedTypeSymbol serviceType, ServiceKeyValue? key, ServiceIndex services)
    {
        if(services.Contains(serviceType, key))
            return true;

        // IEnumerable<T> is resolvable if T is registered with the same key
        if(AnalyzerHelpers.IsIEnumerableOfT(serviceType)
            && AnalyzerHelpers.GetEnumerableElementType(serviceType) is { } elementType)
        {
            return services.Contains(elementType, key);
        }

        return false;
    }

    private static bool ContainsTypeParameter(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
        _ => false,
    };

    /// <param name="Key">The service key of a keyed dependency; <see langword="null"/> for a non-keyed dependency.</param>
    private readonly record struct Dependency(Location? Location, INamedTypeSymbol Type, ServiceKeyValue? Key);

    /// <summary>
    /// Caches the constructor and injected member dependencies of implementation types that need a registration
    /// (dependencies that are always resolvable are excluded), so each type is inspected once per compilation.
    /// The key of a dependency follows the generator: <c>[FromKeyedServices(key)]</c> takes precedence over the key of
    /// <c>[IocInject]</c> / <c>[Inject]</c>.
    /// </summary>
    private sealed class DependencyCache
    {
        private readonly Dictionary<INamedTypeSymbol, List<Dependency>> _cache = new(SymbolEqualityComparer.Default);

        public List<Dependency> Get(INamedTypeSymbol implementationType, CancellationToken cancellationToken)
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
                    // Injected IEnumerable<T> properties are resolved even without a registration
                    case IPropertySymbol { Type: INamedTypeSymbol propertyType } property
                        when !AnalyzerHelpers.IsIEnumerableOfT(propertyType):
                        AddMember(property, propertyType, injectAttribute);
                        break;

                    case IMethodSymbol method:
                        AddParameters(method.Parameters);
                        break;

                    case IFieldSymbol { Type: INamedTypeSymbol fieldType } field:
                        AddMember(field, fieldType, injectAttribute);
                        break;
                }
            }

            _cache[implementationType] = dependencies;
            return dependencies;

            void AddParameters(ImmutableArray<IParameterSymbol> parameters)
            {
                foreach(var parameter in parameters)
                {
                    if(parameter.HasExplicitDefaultValue
                        || parameter.Type is not INamedTypeSymbol parameterType
                        || AnalyzerHelpers.IsAlwaysResolvable(parameterType)
                        || !TryGetParameterKey(parameter, out var key))
                    {
                        continue;
                    }

                    dependencies.Add(new(parameter.Locations.FirstOrDefault(), parameterType, key));
                }
            }

            void AddMember(ISymbol member, INamedTypeSymbol memberType, AttributeData injectAttribute)
            {
                if(AnalyzerHelpers.IsAlwaysResolvable(memberType))
                    return;

                var location = injectAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                    ?? member.Locations.FirstOrDefault();
                dependencies.Add(new(location, memberType, GetAttributeServiceKey(injectAttribute)));
            }
        }

        /// <summary>
        /// Gets the key of a parameter dependency. Returns <see langword="false"/> for a parameter that is not resolved
        /// as a service: <c>[ServiceKey]</c>, and <c>[FromKeyedServices]</c> without a key (inherited key).
        /// </summary>
        private static bool TryGetParameterKey(IParameterSymbol parameter, out ServiceKeyValue? key)
        {
            key = null;
            foreach(var attribute in parameter.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if(attrClass is null)
                    continue;

                if(AnalyzerHelpers.IsServiceKeyAttribute(attrClass))
                    return false;

                if(AnalyzerHelpers.IsFromKeyedServicesAttribute(attrClass))
                {
                    if(attribute.ConstructorArguments is not [{ IsNull: false, Value: not null } keyArg])
                        return false;

                    key = ServiceKeyValue.FromConstant(keyArg);
                    return true;
                }

                if(attrClass.IsInject && key is null)
                    key = GetAttributeServiceKey(attribute);
            }

            return true;
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
        ServiceKeyValue? KeyValue,
        ImmutableArray<INamedTypeSymbol> ExplicitServiceTypes,
        bool HasExplicitRegisterAllInterfaces,
        bool RegisterAllInterfaces,
        bool HasExplicitRegisterAllBaseClasses,
        bool RegisterAllBaseClasses,
        ImmutableArray<string> Tags,
        INamedTypeSymbol? ExplicitContainer);

    /// <param name="Key">The key as the generator's code expression (SGIOC021/027/029).</param>
    /// <param name="KeyValue">The key value (SGIOC018).</param>
    private sealed record class ResolvedRegistration(
        INamedTypeSymbol ImplementationType,
        string? Key,
        ServiceKeyValue? KeyValue,
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
        bool hasFactory,
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

        /// <summary>Whether the settings specify a <c>Factory</c>, which registers an open generic target type.</summary>
        public bool HasFactory { get; } = hasFactory;
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
