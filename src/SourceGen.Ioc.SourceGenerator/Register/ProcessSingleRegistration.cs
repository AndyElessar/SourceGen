namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    /// <summary>
    /// Key for the default registration method (services not excluded from default).
    /// </summary>
    private const string DefaultMethodKey = "";

    /// <summary>
    /// Pipeline 1: Processes a single registration with default settings.
    /// This method can be cached per registration - only re-runs when the registration or default settings change.
    /// </summary>
    /// <param name="registration">The registration data to process.</param>
    /// <param name="defaultSettings">The default settings map.</param>
    /// <returns>The processed registration result with all resolved settings.</returns>
    private static BasicRegistrationResult ProcessSingleRegistration(
        RegistrationData registration,
        DefaultSettingsMap defaultSettings)
    {
        // Reusable buffers
        var matchedDefaultIndices = new HashSet<int>();
        var matchedServiceTypes = new List<TypeData>();
        var serviceTypesToRegister = new HashSet<TypeData>();

        // Find matching default settings from base classes and interfaces
        int bestDefaultIndex = FindMatchingDefaults(
            registration.AllBaseClasses,
            registration.AllInterfaces,
            defaultSettings,
            matchedDefaultIndices,
            matchedServiceTypes);

        DefaultSettingsModel? matchingDefault = bestDefaultIndex >= 0 ? defaultSettings[bestDefaultIndex] : null;

        // Merge settings (explicit > default > registration default)
        var (lifetime, registerAllInterfaces, registerAllBaseClasses) = MergeSettings(registration, matchingDefault);

        // Collect all service types to register
        CollectServiceTypes(
            registration,
            matchingDefault,
            matchedServiceTypes,
            registerAllInterfaces,
            registerAllBaseClasses,
            serviceTypesToRegister);

        var decorators = registration.Decorators.Length > 0
            ? registration.Decorators
            : (matchingDefault?.Decorators ?? registration.Decorators);

        var tags = registration.Tags.Length > 0
            ? registration.Tags
            : (matchingDefault?.Tags ?? registration.Tags);

        var excludeFromDefault = registration.Tags.Length > 0 || registration.ExcludeFromDefault
            ? registration.ExcludeFromDefault
            : (matchingDefault?.ExcludeFromDefault ?? false);

        // Create service registration models
        var serviceRegistrations = CreateServiceRegistrations(
            registration,
            serviceTypesToRegister,
            lifetime,
            decorators);

        // Create open generic entries for indexing (if applicable)
        var openGenericEntries = CreateOpenGenericEntries(
            registration,
            serviceTypesToRegister,
            lifetime,
            decorators,
            tags,
            excludeFromDefault);

        // Collect closed generic dependencies
        var closedGenericDependencies = CollectClosedGenericDependenciesFromRegistration(registration);

        return new BasicRegistrationResult(
            serviceRegistrations,
            tags,
            excludeFromDefault,
            openGenericEntries,
            closedGenericDependencies);
    }

    /// <summary>
    /// Finds matching default settings from base classes and interfaces.
    /// </summary>
    /// <returns>The best matching default index, or -1 if none found.</returns>
    private static int FindMatchingDefaults(
        ImmutableEquatableArray<TypeData> baseClasses,
        ImmutableEquatableArray<TypeData> interfaces,
        DefaultSettingsMap defaultSettings,
        HashSet<int> matchedDefaultIndices,
        List<TypeData> matchedServiceTypes)
    {
        int bestDefaultIndex = -1;

        foreach(var candidate in baseClasses)
        {
            TryMatchDefaultSettings(candidate, defaultSettings, matchedDefaultIndices, matchedServiceTypes, ref bestDefaultIndex);
        }

        foreach(var candidate in interfaces)
        {
            TryMatchDefaultSettings(candidate, defaultSettings, matchedDefaultIndices, matchedServiceTypes, ref bestDefaultIndex);
        }

        return bestDefaultIndex;
    }

    /// <summary>
    /// Attempts to match a candidate type against default settings.
    /// </summary>
    private static void TryMatchDefaultSettings(
        TypeData candidate,
        DefaultSettingsMap defaultSettings,
        HashSet<int> matchedDefaultIndices,
        List<TypeData> matchedServiceTypes,
        ref int bestDefaultIndex)
    {
        // Check exact matches
        if(defaultSettings.TryGetExactMatches(candidate.Name, out var index) && matchedDefaultIndices.Add(index))
        {
            matchedServiceTypes.Add(candidate);
            if(bestDefaultIndex < 0) bestDefaultIndex = index;
        }

        // Check generic matches (only if type has generic parameters)
        if(candidate.IsOpenGeneric || candidate.Name != candidate.NameWithoutGeneric)
        {
            if(defaultSettings.TryGetGenericMatches(candidate.NameWithoutGeneric, candidate.GenericArity, out var gIndex)
                && matchedDefaultIndices.Add(gIndex))
            {
                matchedServiceTypes.Add(candidate);
                if(bestDefaultIndex < 0) bestDefaultIndex = gIndex;
            }
        }
    }

    /// <summary>
    /// Merges registration settings with default settings.
    /// </summary>
    private static (ServiceLifetime Lifetime, bool RegisterAllInterfaces, bool RegisterAllBaseClasses) MergeSettings(
        RegistrationData registration,
        DefaultSettingsModel? matchingDefault)
    {
        var lifetime = registration.HasExplicitLifetime
            ? registration.Lifetime
            : (matchingDefault?.Lifetime ?? registration.Lifetime);

        var registerAllInterfaces = registration.HasExplicitRegisterAllInterfaces
            ? registration.RegisterAllInterfaces
            : (matchingDefault?.RegisterAllInterfaces ?? registration.RegisterAllInterfaces);

        var registerAllBaseClasses = registration.HasExplicitRegisterAllBaseClasses
            ? registration.RegisterAllBaseClasses
            : (matchingDefault?.RegisterAllBaseClasses ?? registration.RegisterAllBaseClasses);

        return (lifetime, registerAllInterfaces, registerAllBaseClasses);
    }

    /// <summary>
    /// Collects all service types to register based on settings.
    /// </summary>
    private static void CollectServiceTypes(
        RegistrationData registration,
        DefaultSettingsModel? matchingDefault,
        List<TypeData> matchedServiceTypes,
        bool registerAllInterfaces,
        bool registerAllBaseClasses,
        HashSet<TypeData> serviceTypesToRegister)
    {
        // Always register the implementation type itself
        serviceTypesToRegister.Add(registration.ImplementationType);

        // Add explicit service types from registration
        foreach(var st in registration.ServiceTypes)
        {
            serviceTypesToRegister.Add(st);
        }

        // Add service types from default settings
        if(matchingDefault is not null)
        {
            foreach(var st in matchingDefault.ServiceTypes)
            {
                serviceTypesToRegister.Add(st);
            }
        }

        // Add matched interfaces/base classes from default settings lookup
        foreach(var matchedType in matchedServiceTypes)
        {
            serviceTypesToRegister.Add(matchedType);
        }

        // Add all interfaces if requested
        if(registerAllInterfaces)
        {
            foreach(var iface in registration.AllInterfaces)
            {
                serviceTypesToRegister.Add(iface);
            }
        }

        // Add all base classes if requested
        if(registerAllBaseClasses)
        {
            foreach(var baseClass in registration.AllBaseClasses)
            {
                serviceTypesToRegister.Add(baseClass);
            }
        }
    }

    /// <summary>
    /// Creates service registration models for each valid service type.
    /// </summary>
    private static ImmutableEquatableArray<ServiceRegistrationModel> CreateServiceRegistrations(
        RegistrationData registration,
        HashSet<TypeData> serviceTypesToRegister,
        ServiceLifetime lifetime,
        ImmutableEquatableArray<TypeData> decorators)
    {
        var implementationType = registration.ImplementationType;
        var isOpenGenericImplementation = implementationType.IsOpenGeneric;

        // Skip if implementation has nested open generic (cannot be registered)
        if(implementationType.IsNestedOpenGeneric)
        {
            return [];
        }

        // Decorator filter cache
        Dictionary<string, ImmutableEquatableArray<TypeData>>? decoratorFilterCache = null;
        if(decorators.Length > 0)
        {
            decoratorFilterCache = new Dictionary<string, ImmutableEquatableArray<TypeData>>(StringComparer.Ordinal);
        }

        var registrations = new List<ServiceRegistrationModel>();

        foreach(var serviceType in serviceTypesToRegister)
        {
            if(!IsValidServiceType(serviceType, implementationType, isOpenGenericImplementation, registration.ValidOpenGenericServiceTypes))
            {
                continue;
            }

            // Filter decorators based on type parameter constraints
            var filteredDecorators = FilterDecorators(decorators, serviceType, decoratorFilterCache);

            var model = new ServiceRegistrationModel(
                serviceType,
                implementationType,
                lifetime,
                registration.Key,
                registration.KeyType,
                isOpenGenericImplementation,
                filteredDecorators,
                registration.InjectionMembers);

            registrations.Add(model);
        }

        return registrations.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Validates if a service type can be registered with the given implementation.
    /// </summary>
    private static bool IsValidServiceType(
        TypeData serviceType,
        TypeData implementationType,
        bool isOpenGenericImplementation,
        ImmutableEquatableSet<string> validOpenGenericServiceTypes)
    {
        // Open generic implementation requires open generic service type (and vice versa)
        if(isOpenGenericImplementation != serviceType.IsOpenGeneric)
        {
            return false;
        }

        // Skip nested open generic types (cannot be properly registered with DI container)
        if(serviceType.IsNestedOpenGeneric)
        {
            return false;
        }

        // For open generic service types (excluding the implementation type itself),
        // verify the implementation actually implements it correctly
        if(serviceType.IsOpenGeneric && serviceType.Name != implementationType.Name)
        {
            var serviceTypeKey = $"{serviceType.NameWithoutGeneric}`{serviceType.GenericArity}";
            if(!validOpenGenericServiceTypes.Contains(serviceTypeKey))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Filters decorators.
    /// Uses the service type's generic base name as cache key for similar types.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> FilterDecorators(
        ImmutableEquatableArray<TypeData> decorators,
        TypeData serviceType,
        Dictionary<string, ImmutableEquatableArray<TypeData>>? cache)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        // If service type has no generic arguments, all decorators pass through
        var serviceTypeParams = serviceType.TypeParameters;
        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            return decorators;
        }

        // Use NameWithoutGeneric + arity as cache key for similar generic types
        var cacheKey = $"{serviceType.NameWithoutGeneric}`{serviceType.GenericArity}";

        // Check cache first
        if(cache is not null && cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var interfaceNameSet = BuildInterfaceNameSet(serviceTypeParams);

        var result = FilterDecoratorsCore(decorators, serviceTypeParams, interfaceNameSet);

        // Store in cache
        cache?.Add(cacheKey, result);

        return result;
    }

    /// <summary>
    /// Builds a HashSet of interface names from all service type parameters for fast lookup.
    /// </summary>
    private static HashSet<string>? BuildInterfaceNameSet(ImmutableEquatableArray<TypeParameter> serviceTypeParams)
    {
        HashSet<string>? result = null;

        foreach(var param in serviceTypeParams)
        {
            var interfaces = param.Type.AllInterfaces;
            if(interfaces is null || interfaces.Length == 0)
            {
                continue;
            }

            result ??= new HashSet<string>(StringComparer.Ordinal);
            foreach(var iface in interfaces)
            {
                result.Add(iface.Name);
                if(iface.Name != iface.NameWithoutGeneric)
                {
                    result.Add(iface.NameWithoutGeneric);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Core decorator filtering logic.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> FilterDecoratorsCore(
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<TypeParameter> serviceTypeParams,
        HashSet<string>? interfaceNameSet)
    {
        var filteredList = new List<TypeData>(decorators.Length);
        foreach(var decorator in decorators)
        {
            if(SatisfiesConstraints(decorator, serviceTypeParams, interfaceNameSet))
            {
                filteredList.Add(decorator);
            }
        }

        // Return original if no filtering occurred
        return filteredList.Count == decorators.Length
            ? decorators
            : filteredList.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Checks if a decorator satisfies its type parameter constraints with the given service type parameters.
    /// </summary>
    private static bool SatisfiesConstraints(
        TypeData decorator,
        ImmutableEquatableArray<TypeParameter> serviceTypeParams,
        HashSet<string>? interfaceNameSet)
    {
        var decoratorTypeParams = decorator.TypeParameters;
        if(decoratorTypeParams is null || decoratorTypeParams.Length == 0)
        {
            return true;
        }

        // Arity mismatch - cannot apply decorator
        if(decoratorTypeParams.Length != serviceTypeParams.Length)
        {
            return true; // Let it pass, runtime will handle this
        }

        // Check each decorator type parameter's constraints
        for(int i = 0; i < decoratorTypeParams.Length; i++)
        {
            var decoratorParam = decoratorTypeParams[i];
            var serviceParam = serviceTypeParams[i];

            // Check type constraints
            var constraintTypes = decoratorParam.ConstraintTypes;
            if(constraintTypes is null || constraintTypes.Length == 0)
            {
                continue;
            }

            foreach(var constraintType in constraintTypes)
            {
                if(!SatisfiesTypeConstraintCore(serviceParam, constraintType, interfaceNameSet))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Type constraint check.
    /// </summary>
    /// <remarks>
    /// For a constraint like <c>where TRequest : IQuery&lt;TRequest, TResponse&gt;</c>:
    /// - constraintType.Name = "global::Ns.IQuery&lt;TRequest, TResponse&gt;"
    /// - constraintType.TypeParameters = [(TRequest, TRequest), (TResponse, TResponse)]
    /// - serviceParam is the actual type assigned to TRequest (e.g., TestCommand or TestQuery)
    /// 
    /// We need to check if the actual type's interfaces include IQuery with matching type parameters.
    /// Uses HashSet for O(1) lookup instead of O(n) iteration.
    /// </remarks>
    private static bool SatisfiesTypeConstraintCore(
        TypeParameter serviceParam,
        TypeData constraintType,
        HashSet<string>? interfaceNameSet)
    {
        var actualType = serviceParam.Type;

        // Direct match (for non-generic constraints)
        if(actualType.Name == constraintType.Name)
        {
            return true;
        }

        // No interface set means no interfaces to check
        if(interfaceNameSet is null)
        {
            // No interface information available, assume constraint is not satisfied
            // for open generic constraints
            return !constraintType.IsOpenGeneric;
        }

        if(constraintType.IsOpenGeneric)
        {
            // For open generic constraints, check NameWithoutGeneric
            return interfaceNameSet.Contains(constraintType.NameWithoutGeneric);
        }
        else
        {
            // For closed generic or non-generic constraints, check exact name
            return interfaceNameSet.Contains(constraintType.Name);
        }
    }

    /// <summary>
    /// Creates open generic entries for indexing during closed generic resolution.
    /// </summary>
    private static ImmutableEquatableArray<OpenGenericEntry> CreateOpenGenericEntries(
        RegistrationData registration,
        HashSet<TypeData> serviceTypesToRegister,
        ServiceLifetime lifetime,
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<string> tags,
        bool excludeFromDefault)
    {
        var implementationType = registration.ImplementationType;

        // Only for open generic implementations (not nested)
        if(!implementationType.IsOpenGeneric || implementationType.IsNestedOpenGeneric)
        {
            return [];
        }

        var info = new OpenGenericRegistrationInfo(
            implementationType,
            serviceTypesToRegister.ToImmutableEquatableArray(),
            registration.AllInterfaces,
            lifetime,
            registration.Key,
            registration.KeyType,
            decorators,
            tags,
            excludeFromDefault,
            registration.InjectionMembers);

        var entries = new List<OpenGenericEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        // Index by each open generic service type
        foreach(var serviceType in serviceTypesToRegister)
        {
            if(!serviceType.IsOpenGeneric)
            {
                continue;
            }

            var key = serviceType.NameWithoutGeneric;
            if(addedKeys.Add(key))
            {
                entries.Add(new OpenGenericEntry(key, info));
            }
        }

        // Also index by all interfaces that are open generics
        foreach(var iface in registration.AllInterfaces)
        {
            if(!iface.IsOpenGeneric)
            {
                continue;
            }

            var key = iface.NameWithoutGeneric;
            if(addedKeys.Add(key))
            {
                entries.Add(new OpenGenericEntry(key, info));
            }
        }

        return entries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Collects closed generic dependencies from a registration's constructor parameters.
    /// </summary>
    private static ImmutableEquatableArray<ClosedGenericDependency> CollectClosedGenericDependenciesFromRegistration(
        RegistrationData registration)
    {
        var constructorParams = registration.ImplementationType.ConstructorParameters;
        if(constructorParams is null || constructorParams.Length == 0)
        {
            return [];
        }

        var dependencies = new List<ClosedGenericDependency>();

        foreach(var param in constructorParams)
        {
            var paramType = param.Type;
            // Check if this is a closed generic type (has generic arguments but is not open generic)
            if(paramType.GenericArity > 0 && !paramType.IsOpenGeneric && !paramType.IsNestedOpenGeneric)
            {
                dependencies.Add(new ClosedGenericDependency(
                    paramType.Name,
                    paramType,
                    paramType.NameWithoutGeneric));
            }
        }

        return dependencies.ToImmutableEquatableArray();
    }
}
