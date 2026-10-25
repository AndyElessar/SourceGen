namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Processes a single registration that comes from IocRegisterDefaultsAttribute.ImplementationTypes.
    /// These registrations already have all settings explicitly set from the defaults attribute,
    /// so no default settings lookup is needed.
    /// </summary>
    /// <param name="registration">The registration data to process.</param>
    /// <returns>The processed registration result.</returns>
    private static BasicRegistrationResult ProcessSingleRegistrationFromDefaults(RegistrationData registration, CancellationToken ct)
    {
        // Use the explicit settings from the registration (already set from defaults attribute)
        return ProcessRegistrationCore(
            registration,
            lifetime: registration.Lifetime,
            registerAllInterfaces: registration.RegisterAllInterfaces,
            registerAllBaseClasses: registration.RegisterAllBaseClasses,
            decorators: registration.Decorators,
            tags: registration.Tags,
            factory: registration.Factory,
            additionalServiceTypesFromDefaults: null,
            ct);
    }

    /// <summary>
    /// Pipeline 1: Processes a single registration with default settings.
    /// This method can be cached per registration - only re-runs when the registration or default settings change.
    /// </summary>
    /// <param name="registration">The registration data to process.</param>
    /// <param name="defaultSettings">The default settings map.</param>
    /// <returns>The processed registration result with all resolved settings.</returns>
    private static BasicRegistrationResult ProcessSingleRegistration(
        RegistrationData registration,
        DefaultSettingsMap defaultSettings,
        CancellationToken ct)
    {
        // Reusable buffers for default settings lookup
        var matchedDefaultIndices = new List<int>();
        var matchedServiceTypes = new List<TypeData>();

        // Find matching default settings from base classes and interfaces
        ct.ThrowIfCancellationRequested();
        int bestDefaultIndex = FindMatchingDefaults(
            registration.AllBaseClasses,
            registration.AllInterfaces,
            defaultSettings,
            matchedDefaultIndices,
            matchedServiceTypes);

        DefaultSettingsModel? matchingDefault = bestDefaultIndex >= 0 ? defaultSettings[bestDefaultIndex] : null;

        // Merge settings (explicit > default > fallback lifetime)
        var (lifetime, registerAllInterfaces, registerAllBaseClasses) = MergeSettings(registration, matchingDefault, defaultSettings.FallbackLifetime);

        var decorators = registration.Decorators.Length > 0
            ? registration.Decorators
            : (matchingDefault?.Decorators ?? registration.Decorators);

        var tags = registration.Tags.Length > 0
            ? registration.Tags
            : (matchingDefault?.Tags ?? registration.Tags);

        // Factory: explicit registration Factory takes precedence over default settings
        var factory = registration.Factory ?? matchingDefault?.Factory;

        // Collect additional service types from matching defaults
        IEnumerable<TypeData>? additionalServiceTypes = null;
        if(matchingDefault is not null || matchedServiceTypes.Count > 0)
        {
            additionalServiceTypes = GetAdditionalServiceTypesFromDefaults(matchingDefault, matchedServiceTypes);
        }

        return ProcessRegistrationCore(
            registration,
            lifetime,
            registerAllInterfaces,
            registerAllBaseClasses,
            decorators,
            tags,
            factory,
            additionalServiceTypes,
            ct);
    }

    /// <summary>
    /// Core processing logic for a single registration with resolved settings.
    /// </summary>
    private static BasicRegistrationResult ProcessRegistrationCore(
        RegistrationData registration,
        ServiceLifetime lifetime,
        bool registerAllInterfaces,
        bool registerAllBaseClasses,
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<string> tags,
        FactoryMethodData? factory,
        IEnumerable<TypeData>? additionalServiceTypesFromDefaults,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var serviceTypesToRegister = new List<TypeData>
        {
            // Always register the implementation type itself
            registration.ImplementationType
        };
        var addedTypeNames = new HashSet<string>(StringComparer.Ordinal) { registration.ImplementationType.Name };

        // Add explicit service types from registration
        foreach(var st in registration.ServiceTypes)
        {
            if(addedTypeNames.Add(st.Name))
            {
                serviceTypesToRegister.Add(st);
            }
        }

        // Add service types from default settings
        if(additionalServiceTypesFromDefaults is not null)
        {
            foreach(var st in additionalServiceTypesFromDefaults)
            {
                if(addedTypeNames.Add(st.Name))
                {
                    serviceTypesToRegister.Add(st);
                }
            }
        }

        // Add all interfaces if requested
        if(registerAllInterfaces)
        {
            foreach(var iface in registration.AllInterfaces)
            {
                if(addedTypeNames.Add(iface.Name))
                {
                    serviceTypesToRegister.Add(iface);
                }
            }
        }

        // Add all base classes if requested
        if(registerAllBaseClasses)
        {
            foreach(var baseClass in registration.AllBaseClasses)
            {
                if(addedTypeNames.Add(baseClass.Name))
                {
                    serviceTypesToRegister.Add(baseClass);
                }
            }
        }

        // Create service registration models
        var serviceRegistrations = CreateServiceRegistrations(
            registration,
            serviceTypesToRegister,
            lifetime,
            decorators,
            factory);

        // Create open generic entries for indexing (if applicable)
        var openGenericEntries = CreateOpenGenericEntries(
            registration,
            serviceTypesToRegister,
            lifetime,
            decorators,
            tags,
            factory);

        // Collect closed generic dependencies from constructor parameters, injection members, factory params,
        // and also from closed decorators' constructor parameters
        var closedGenericDependencies = CollectClosedGenericDependenciesFromRegistration(registration, serviceRegistrations);

        return new BasicRegistrationResult(
            serviceRegistrations,
            tags,
            openGenericEntries,
            closedGenericDependencies);
    }

    /// <summary>
    /// Creates service registration models for each valid service type.
    /// </summary>
    private static ImmutableEquatableArray<ServiceRegistrationModel> CreateServiceRegistrations(
        RegistrationData registration,
        List<TypeData> serviceTypesToRegister,
        ServiceLifetime lifetime,
        ImmutableEquatableArray<TypeData> decorators,
        FactoryMethodData? factory)
    {
        var implementationType = registration.ImplementationType;
        var isOpenGenericImplementation = implementationType is GenericTypeData { IsOpenGeneric: true };

        // Skip if implementation has nested open generic (cannot be registered)
        if(implementationType is GenericTypeData { IsNestedOpenGeneric: true })
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
            // When serviceType == implementationType (self-registration), decorators cannot be applied
            // because decorators return the interface type, not the implementation type
            var isSelfRegistration = serviceType.Name == implementationType.Name;
            var filteredDecorators = isSelfRegistration
                ? []
                : FilterDecorators(decorators, serviceType, decoratorFilterCache);

            var model = new ServiceRegistrationModel(
                serviceType,
                implementationType,
                lifetime,
                registration.Key,
                registration.KeyType,
                registration.KeyValueType,
                isOpenGenericImplementation,
                filteredDecorators,
                registration.InjectionMembers,
                factory,
                registration.Instance);

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
        var isOpenGenericServiceType = serviceType is GenericTypeData { IsOpenGeneric: true };
        if(isOpenGenericImplementation != isOpenGenericServiceType)
        {
            return false;
        }

        // Skip nested open generic types (cannot be properly registered with DI container)
        if(serviceType is GenericTypeData { IsNestedOpenGeneric: true })
        {
            return false;
        }

        // For open generic service types (excluding the implementation type itself),
        // verify the implementation actually implements it correctly
        if(isOpenGenericServiceType && serviceType.Name != implementationType.Name)
        {
            if(serviceType is not GenericTypeData genericServiceType)
            {
                return false;
            }

            var serviceTypeKey = $"{genericServiceType.NameWithoutGeneric}`{genericServiceType.GenericArity}";
            if(!validOpenGenericServiceTypes.Contains(serviceTypeKey))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Filters and closes decorators for a specific service type.
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
        var serviceTypeParams = serviceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;
        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            return decorators;
        }

        // Use full service type name as cache key (includes specific type arguments)
        var cacheKey = serviceType.Name;

        // Check cache first
        if(cache is not null && cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var filtered = FilterDecoratorsCore(decorators, serviceTypeParams);

        // Substitute type parameters in decorators to close open generic decorators
        var result = SubstituteDecoratorsTypeParams(filtered, serviceTypeParams);

        // Store in cache
        cache?.Add(cacheKey, result);

        return result;
    }

    /// <summary>
    /// Substitutes type parameters in decorators using the service type's type parameters.
    /// This closes open generic decorators to match the specific service type.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> SubstituteDecoratorsTypeParams(
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<TypeParameter> serviceTypeParams)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        var result = new List<TypeData>(decorators.Length);
        foreach(var decorator in decorators)
        {
            var closed = SubstituteDecoratorTypeParams(decorator, serviceTypeParams);
            result.Add(closed);
        }

        return result.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Core decorator filtering logic.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> FilterDecoratorsCore(
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<TypeParameter> serviceTypeParams)
    {
        var filteredList = new List<TypeData>(decorators.Length);
        foreach(var decorator in decorators)
        {
            if(SatisfiesConstraints(decorator, serviceTypeParams))
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
        ImmutableEquatableArray<TypeParameter> serviceTypeParams)
    {
        var decoratorTypeParams = decorator is GenericTypeData genericDecorator
            ? genericDecorator.TypeParameters
            : null;
        if(decoratorTypeParams is null || decoratorTypeParams.Length == 0)
        {
            return true;
        }

        // Arity mismatch - cannot apply decorator
        if(decoratorTypeParams.Length != serviceTypeParams.Length)
        {
            return true; // Let it pass, runtime will handle this
        }

        // Check each decorator type parameter's constraints against the type argument at the same position
        for(int i = 0; i < decoratorTypeParams.Length; i++)
        {
            var constraintTypes = decoratorTypeParams[i].ConstraintTypes;
            if(constraintTypes is null || constraintTypes.Length == 0)
            {
                continue;
            }

            foreach(var constraintType in constraintTypes)
            {
                if(!SatisfiesTypeConstraintCore(serviceTypeParams[i].Type, constraintType))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Checks whether a type argument is, implements, or derives from a constraint type.
    /// </summary>
    /// <remarks>
    /// For a constraint like <c>where TRequest : IQuery&lt;TRequest, TResponse&gt;</c>, which is open generic,
    /// the type argument must implement some <c>IQuery&lt;,&gt;</c>, so names are compared without type arguments.
    /// Closed generic and non-generic constraints compare full names.
    /// </remarks>
    private static bool SatisfiesTypeConstraintCore(TypeData typeArgument, TypeData constraintType)
    {
        // Direct match (for non-generic constraints)
        if(typeArgument.Name == constraintType.Name)
        {
            return true;
        }

        var constraintName = constraintType is GenericTypeData { IsOpenGeneric: true } genericConstraintType
            ? genericConstraintType.NameWithoutGeneric
            : constraintType.Name;

        return ContainsName(typeArgument.AllInterfaces, constraintName)
            || ContainsName(typeArgument.AllBaseClasses, constraintName);

        static bool ContainsName(ImmutableEquatableArray<TypeData>? types, string name)
        {
            if(types is null)
            {
                return false;
            }

            foreach(var type in types)
            {
                if(type.Name == name || type is GenericTypeData genericType && genericType.NameWithoutGeneric == name)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Creates open generic entries for indexing during closed generic resolution.
    /// </summary>
    private static ImmutableEquatableArray<OpenGenericEntry> CreateOpenGenericEntries(
        RegistrationData registration,
        List<TypeData> serviceTypesToRegister,
        ServiceLifetime lifetime,
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<string> tags,
        FactoryMethodData? factory)
    {
        var implementationType = registration.ImplementationType;

        // Only for open generic implementations (not nested)
        if(implementationType is not GenericTypeData { IsOpenGeneric: true, IsNestedOpenGeneric: false })
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
            registration.KeyValueType,
            decorators,
            tags,
            registration.InjectionMembers,
            factory,
            registration.Instance);

        var entries = new List<OpenGenericEntry>();
        var addedKeys = new List<string>();

        // Index by each open generic service type
        foreach(var serviceType in serviceTypesToRegister)
        {
            if(serviceType is not GenericTypeData { IsOpenGeneric: true } genericServiceType)
            {
                continue;
            }

            var key = genericServiceType.NameWithoutGeneric;
            if(!addedKeys.Contains(key))
            {
                addedKeys.Add(key);
                entries.Add(new OpenGenericEntry(key, info));
            }
        }

        // Also index by all interfaces that are open generics
        foreach(var iface in registration.AllInterfaces)
        {
            if(iface is not GenericTypeData { IsOpenGeneric: true } genericInterface)
            {
                continue;
            }

            var key = genericInterface.NameWithoutGeneric;
            if(!addedKeys.Contains(key))
            {
                addedKeys.Add(key);
                entries.Add(new OpenGenericEntry(key, info));
            }
        }

        return entries.ToImmutableEquatableArray();
    }
}
