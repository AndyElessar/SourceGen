namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    private static ImmutableEquatableArray<ServiceRegistrationModel> GenerateServiceRegistrations(
        in ImmutableArray<RegistrationData> registrations,
        DefaultSettingsMap defaultSettings,
        CancellationToken ct)
    {
        var result = new List<ServiceRegistrationModel>((int)(registrations.Length * 1.5));

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

            // Determine decorators (registration's if present, otherwise default's)
            var decorators = registration.Decorators.Length > 0
                ? registration.Decorators
                : (matchingDefault?.Decorators ?? registration.Decorators);

            // Create registrations for each valid service type
            CreateRegistrations(
                registration,
                serviceTypesToRegister,
                lifetime,
                decorators,
                result);
        }

        return result.ToImmutableEquatableArray();
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

        // Process base classes first, then interfaces
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
    /// Creates service registrations for each valid service type.
    /// </summary>
    private static void CreateRegistrations(
        RegistrationData registration,
        HashSet<TypeData> serviceTypesToRegister,
        ServiceLifetime lifetime,
        ImmutableEquatableArray<TypeData> decorators,
        List<ServiceRegistrationModel> result)
    {
        var implementationType = registration.ImplementationType;
        var isOpenGenericImplementation = implementationType.IsOpenGeneric;

        // Skip if implementation has nested open generic (cannot be registered)
        if(implementationType.IsNestedOpenGeneric)
        {
            return;
        }

        foreach(var serviceType in serviceTypesToRegister)
        {
            if(!IsValidServiceType(serviceType, implementationType, isOpenGenericImplementation, registration.ValidOpenGenericServiceTypes))
            {
                continue;
            }

            // Filter decorators based on type parameter constraints for this specific service type
            var filteredDecorators = FilterDecorators(decorators, serviceType);

            // Process decorators (mark which constructor parameters are service parameters)
            var processedDecorators = ProcessDecorators(filteredDecorators, implementationType);

            result.Add(new ServiceRegistrationModel(
                serviceType,
                implementationType,
                lifetime,
                registration.Key,
                registration.KeyType,
                isOpenGenericImplementation,
                processedDecorators));
        }
    }

    /// <summary>
    /// Filters decorators based on their type parameter constraints.
    /// Only decorators whose constraints are satisfied by the service type's generic arguments will be included.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> FilterDecorators(
        ImmutableEquatableArray<TypeData> decorators,
        TypeData serviceType)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        // If service type has no generic arguments, we can't validate constraints
        // For non-generic service types, all decorators should pass through
        var serviceTypeParams = serviceType.TypeParameters;
        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            return decorators;
        }

        List<TypeData>? filteredList = null;
        for(int i = 0; i < decorators.Length; i++)
        {
            var decorator = decorators[i];
            if(SatisfiesConstraints(decorator, serviceTypeParams))
            {
                filteredList?.Add(decorator);
            }
            else
            {
                // First decorator that doesn't satisfy constraints - create filtered list
                if(filteredList is null)
                {
                    filteredList = new List<TypeData>(decorators.Length);
                    for(int j = 0; j < i; j++)
                    {
                        filteredList.Add(decorators[j]);
                    }
                }
            }
        }

        return filteredList?.ToImmutableEquatableArray() ?? decorators;
    }

    /// <summary>
    /// Checks if a decorator satisfies its type parameter constraints with the given service type parameters.
    /// </summary>
    private static bool SatisfiesConstraints(
        TypeData decorator,
        ImmutableEquatableArray<TypeParameter> serviceTypeParams)
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
                if(!SatisfiesTypeConstraint(serviceParam, constraintType))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if an actual type parameter satisfies a type constraint.
    /// </summary>
    /// <remarks>
    /// For a constraint like <c>where TRequest : IQuery&lt;TRequest, TResponse&gt;</c>:
    /// - constraintType.Name = "global::Ns.IQuery&lt;TRequest, TResponse&gt;"
    /// - constraintType.TypeParameters = [(TRequest, TRequest), (TResponse, TResponse)]
    /// - serviceParam is the actual type assigned to TRequest (e.g., TestCommand or TestQuery)
    /// 
    /// We need to check if the actual type's interfaces include IQuery with matching type parameters.
    /// </remarks>
    private static bool SatisfiesTypeConstraint(
        TypeParameter serviceParam,
        TypeData constraintType)
    {
        var actualType = serviceParam.Type;

        // Direct match (for non-generic constraints)
        if(actualType.Name == constraintType.Name)
        {
            return true;
        }

        // Check if the actual type implements the constraint interface
        var implementedInterfaces = actualType.AllInterfaces;
        if(implementedInterfaces is null || implementedInterfaces.Length == 0)
        {
            // No interface information available, assume constraint is not satisfied
            // for open generic constraints
            return !constraintType.IsOpenGeneric;
        }

        // For open generic constraints (like IQuery<TRequest, TResponse>), check if
        // the actual type implements an interface with the same base name
        if(constraintType.IsOpenGeneric)
        {
            // Check if any implemented interface matches the constraint's base type
            foreach(var iface in implementedInterfaces)
            {
                if(iface.NameWithoutGeneric == constraintType.NameWithoutGeneric)
                {
                    return true;
                }
            }
            return false;
        }
        else
        {
            // For closed generic or non-generic constraints, check for exact match
            foreach(var iface in implementedInterfaces)
            {
                if(iface.Name == constraintType.Name)
                {
                    return true;
                }
            }
            return false;
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
        TypeData implementationType)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        // Build set of all assignable type names (with their non-generic variants for matching)
        var serviceTypeNames = BuildServiceTypeNameSet(implementationType);

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
