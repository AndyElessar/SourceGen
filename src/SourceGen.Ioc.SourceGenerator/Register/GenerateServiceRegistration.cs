namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    /// <summary>
    /// Key for the default registration method (services not excluded from default).
    /// </summary>
    private const string DefaultMethodKey = "";

    /// <summary>
    /// Generates service registrations grouped by method name.
    /// The dictionary key is the tag name (empty string for default method).
    /// </summary>
    private static ImmutableEquatableDictionary<string, ImmutableEquatableArray<ServiceRegistrationModel>> GenerateServiceRegistrations(
        in ImmutableArray<RegistrationData> registrations,
        DefaultSettingsMap defaultSettings,
        CancellationToken ct)
    {
        var methodGroups = new Dictionary<string, List<ServiceRegistrationModel>>(StringComparer.Ordinal);

        // Reusable buffers to reduce allocations
        var matchedDefaultIndices = new HashSet<int>();
        var matchedServiceTypes = new List<TypeData>();
        var serviceTypesToRegister = new HashSet<TypeData>();

        foreach(var registration in registrations)
        {
            ct.ThrowIfCancellationRequested();

            matchedDefaultIndices.Clear();
            matchedServiceTypes.Clear();
            serviceTypesToRegister.Clear();

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

            var tags = MergeTags(registration.Tags, matchingDefault?.Tags);

            var excludeFromDefault = registration.Tags.Length > 0 || registration.ExcludeFromDefault
                ? registration.ExcludeFromDefault
                : (matchingDefault?.ExcludeFromDefault ?? false);

            // Build service type names once per registration
            var serviceTypeNames = BuildServiceTypeNameSet(registration.ImplementationType);

            // Create registrations for each valid service type and add to appropriate groups
            CreateRegistrationsGrouped(
                registration,
                serviceTypesToRegister,
                lifetime,
                decorators,
                tags,
                excludeFromDefault,
                serviceTypeNames,
                methodGroups);
        }

        return methodGroups
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .ToImmutableEquatableDictionary(
                static kvp => kvp.Key,
                static kvp => kvp.Value.ToImmutableEquatableArray());
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
    /// Merges tags from registration with tags from default settings.
    /// Registration tags take precedence; if empty, uses default's tags.
    /// </summary>
    private static ImmutableEquatableArray<string> MergeTags(
        ImmutableEquatableArray<string> registrationTags,
        ImmutableEquatableArray<string>? defaultTags)
    {
        if(registrationTags.Length > 0)
        {
            return registrationTags;
        }

        return defaultTags ?? registrationTags;
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
    /// Creates service registrations for each valid service type and groups them by method.
    /// </summary>
    private static void CreateRegistrationsGrouped(
        RegistrationData registration,
        HashSet<TypeData> serviceTypesToRegister,
        ServiceLifetime lifetime,
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<string> tags,
        bool excludeFromDefault,
        HashSet<string> serviceTypeNames,
        Dictionary<string, List<ServiceRegistrationModel>> methodGroups)
    {
        var implementationType = registration.ImplementationType;
        var isOpenGenericImplementation = implementationType.IsOpenGeneric;

        // Skip if implementation has nested open generic (cannot be registered)
        if(implementationType.IsNestedOpenGeneric)
        {
            return;
        }

        // key = serviceType's NameWithoutGeneric for generic, Name for non-generic
        Dictionary<string, ImmutableEquatableArray<TypeData>>? decoratorFilterCache = null;
        if(decorators.Length > 0)
        {
            decoratorFilterCache = new Dictionary<string, ImmutableEquatableArray<TypeData>>(StringComparer.Ordinal);
        }

        foreach(var serviceType in serviceTypesToRegister)
        {
            if(!IsValidServiceType(serviceType, implementationType, isOpenGenericImplementation, registration.ValidOpenGenericServiceTypes))
            {
                continue;
            }

            // Filter decorators based on type parameter constraints for this specific service type (with caching)
            var filteredDecorators = FilterDecorators(decorators, serviceType, decoratorFilterCache);

            // Process decorators (mark which constructor parameters are service parameters)
            var processedDecorators = ProcessDecorators(filteredDecorators, serviceTypeNames);

            var model = new ServiceRegistrationModel(
                serviceType,
                implementationType,
                lifetime,
                registration.Key,
                registration.KeyType,
                isOpenGenericImplementation,
                processedDecorators);

            // Add to default method if not excluded
            if(!excludeFromDefault)
            {
                AddToMethodGroup(methodGroups, DefaultMethodKey, model);
            }

            // Add to each tag's method
            foreach(var tag in tags)
            {
                AddToMethodGroup(methodGroups, tag, model);
            }
        }
    }

    /// <summary>
    /// Adds a registration model to the specified method group.
    /// </summary>
    private static void AddToMethodGroup(
        Dictionary<string, List<ServiceRegistrationModel>> methodGroups,
        string methodKey,
        ServiceRegistrationModel model)
    {
        if(!methodGroups.TryGetValue(methodKey, out var list))
        {
            list = [];
            methodGroups[methodKey] = list;
        }
        list.Add(model);
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
    /// Pre-processes decorators to mark which constructor parameters are service parameters.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> ProcessDecorators(
        ImmutableEquatableArray<TypeData> decorators,
        HashSet<string> serviceTypeNames)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        var processedDecorators = new List<TypeData>(decorators.Length);
        foreach(var decorator in decorators)
        {
            processedDecorators.Add(ProcessDecoratorParameters(decorator, serviceTypeNames));
        }

        return processedDecorators.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Builds a set of service type names (both full and non-generic variants) for parameter matching.
    /// </summary>
    private static HashSet<string> BuildServiceTypeNameSet(TypeData implementationType)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        AddTypeNameVariants(result, implementationType);

        if(implementationType.AllBaseClasses is not null)
        {
            foreach(var baseClass in implementationType.AllBaseClasses)
            {
                AddTypeNameVariants(result, baseClass);
            }
        }

        if(implementationType.AllInterfaces is not null)
        {
            foreach(var iface in implementationType.AllInterfaces)
            {
                AddTypeNameVariants(result, iface);
            }
        }

        return result;
    }

    /// <summary>
    /// Adds both the full name and non-generic name variants to the set.
    /// </summary>
    private static void AddTypeNameVariants(HashSet<string> set, TypeData type)
    {
        set.Add(type.Name);
        if(type.Name != type.NameWithoutGeneric)
        {
            set.Add(type.NameWithoutGeneric);
        }
    }

    /// <summary>
    /// Processes a single decorator's constructor parameters to mark service parameters.
    /// </summary>
    private static TypeData ProcessDecoratorParameters(TypeData decorator, HashSet<string> serviceTypeNames)
    {
        var constructorParams = decorator.ConstructorParameters;
        if(constructorParams is null || constructorParams.Length == 0)
        {
            return decorator;
        }

        var processedParams = new List<ConstructorParameterData>(constructorParams.Length);
        foreach(var param in constructorParams)
        {
            var isServiceParam = IsServiceTypeParameter(param.Type, serviceTypeNames);
            processedParams.Add(param with { IsServiceParameter = isServiceParam });
        }

        return decorator with
        {
            ConstructorParameters = processedParams.ToImmutableEquatableArray()
        };
    }

    /// <summary>
    /// Checks if a parameter type matches any of the service types.
    /// </summary>
    private static bool IsServiceTypeParameter(TypeData paramType, HashSet<string> serviceTypeNames)
    {
        // Direct match on full name or non-generic name
        return serviceTypeNames.Contains(paramType.Name)
            || serviceTypeNames.Contains(paramType.NameWithoutGeneric);
    }
}
