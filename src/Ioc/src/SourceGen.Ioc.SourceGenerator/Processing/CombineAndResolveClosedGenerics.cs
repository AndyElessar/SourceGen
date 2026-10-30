namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ImmutableEquatableArray<ClosedGenericDependency> CollectUniqueClosedGenericDependencies(
        ImmutableArray<ClosedGenericDependency> invocations,
        ImmutableArray<ClosedGenericDependency> discoveries,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dependencies = new List<ClosedGenericDependency>();
        Add(invocations);
        Add(discoveries);
        return dependencies.ToImmutableEquatableArray();

        void Add(ImmutableArray<ClosedGenericDependency> candidates)
        {
            foreach(var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if(seen.Add(candidate.ClosedTypeName))
                    dependencies.Add(candidate);
            }
        }
    }

    /// <summary>
    /// Pipeline 2: Combines all basic registration results and resolves closed generic dependencies.
    /// This method re-runs when any BasicRegistrationResult changes or when invocations change.
    /// </summary>
    /// <param name="basicResults">The basic registration results from pipeline 1.</param>
    /// <param name="serviceProviderInvocations">Closed generic types from GetService/GetRequiredService invocations.</param>
    /// <param name="factoryBasedOpenGenericEntries">Open generic entries from IocRegisterDefaults with Factory (without explicit implementation types).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Service registrations with tags for deferred method grouping.</returns>
    private static ImmutableEquatableArray<ServiceRegistrationWithTags> CombineAndResolveClosedGenerics(
        in ImmutableArray<BasicRegistrationResult> basicResults,
        in ImmutableEquatableArray<ClosedGenericDependency> serviceProviderInvocations,
        in ImmutableArray<OpenGenericEntry> factoryBasedOpenGenericEntries,
        CancellationToken ct)
    {
        List<ServiceRegistrationWithTags> registrations = [];

        // Use List to allow multiple implementations per service type key
        // (e.g., GenericRequestHandler<T> and GenericRequestHandler2<T> both implement IRequestHandler<,>)
        Dictionary<string, List<OpenGenericRegistrationInfo>>? openGenericIndex = null;
        Dictionary<string, ClosedGenericDependency>? closedGenericDependencies = null;

        // Process each basic result
        foreach(var result in basicResults)
        {
            ct.ThrowIfCancellationRequested();

            // Add service registrations with their tags
            foreach(var model in result.ServiceRegistrations)
            {
                registrations.Add(new ServiceRegistrationWithTags(model, result.Tags));
            }

            // Index open generic entries - store ALL implementations per service type key
            if(result.OpenGenericEntries.Length > 0)
            {
                openGenericIndex ??= new Dictionary<string, List<OpenGenericRegistrationInfo>>(result.OpenGenericEntries.Length, StringComparer.Ordinal);
                foreach(var entry in result.OpenGenericEntries)
                {
                    if(!openGenericIndex.TryGetValue(entry.ServiceTypeKey, out var list))
                    {
                        list = [];
                        openGenericIndex[entry.ServiceTypeKey] = list;
                    }
                    list.Add(entry.RegistrationInfo);
                }
            }

            // Collect closed generic dependencies from constructor parameters
            if(result.ClosedGenericDependencies.Length > 0)
            {
                closedGenericDependencies ??= new Dictionary<string, ClosedGenericDependency>(result.ClosedGenericDependencies.Length, StringComparer.Ordinal);
                foreach(var dep in result.ClosedGenericDependencies)
                {
                    if(!closedGenericDependencies.ContainsKey(dep.ClosedTypeName))
                    {
                        closedGenericDependencies[dep.ClosedTypeName] = dep;
                    }
                }
            }
        }

        // Index factory-based open generic entries from IocRegisterDefaults with Factory
        if(factoryBasedOpenGenericEntries.Length > 0)
        {
            openGenericIndex ??= new Dictionary<string, List<OpenGenericRegistrationInfo>>(factoryBasedOpenGenericEntries.Length, StringComparer.Ordinal);
            foreach(var entry in factoryBasedOpenGenericEntries)
            {
                if(!openGenericIndex.TryGetValue(entry.ServiceTypeKey, out var list))
                {
                    list = [];
                    openGenericIndex[entry.ServiceTypeKey] = list;
                }
                list.Add(entry.RegistrationInfo);
            }
        }

        // Collect closed generic dependencies from GetService/GetRequiredService invocations
        if(serviceProviderInvocations.Length > 0)
        {
            closedGenericDependencies ??= new Dictionary<string, ClosedGenericDependency>(serviceProviderInvocations.Length, StringComparer.Ordinal);
            foreach(var dep in serviceProviderInvocations)
            {
                if(!closedGenericDependencies.ContainsKey(dep.ClosedTypeName))
                {
                    closedGenericDependencies[dep.ClosedTypeName] = dep;
                }
            }
        }

        // Generate factory registrations when we have open generics to potentially close
        // Even if there are no direct closed generic dependencies, decorators may have closed generic dependencies
        if(openGenericIndex is not null)
        {
            GenerateClosedGenericFactoryRegistrations(
                openGenericIndex,
                closedGenericDependencies,
                registrations,
                ct);
        }

        return registrations.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Generates factory registrations for closed generic types that depend on open generic registrations.
    /// Iteratively processes dependencies including those from decorator constructor parameters.
    /// </summary>
    private static void GenerateClosedGenericFactoryRegistrations(
        Dictionary<string, List<OpenGenericRegistrationInfo>> openGenericIndex,
        Dictionary<string, ClosedGenericDependency>? closedGenericDependencies,
        List<ServiceRegistrationWithTags> registrations,
        CancellationToken ct)
    {
        // No open generics to close - nothing to do
        if(openGenericIndex.Count == 0)
        {
            return;
        }

        // Track already generated closed generic registrations to avoid duplicates
        var generatedClosedGenerics = new HashSet<string>(StringComparer.Ordinal);

        // First, collect all existing registrations to avoid duplicates
        // This includes both implementation types and service types
        foreach(var regWithTags in registrations)
        {
            var model = regWithTags.Registration;
            generatedClosedGenerics.Add(model.ImplementationType.Name);
            generatedClosedGenerics.Add(model.ServiceType.Name);
        }

        // Use a queue for iterative processing of dependencies
        var pendingDependencies = closedGenericDependencies is not null
            ? new Queue<ClosedGenericDependency>(closedGenericDependencies.Values)
            : new Queue<ClosedGenericDependency>();
        var processedDependencies = new HashSet<string>(StringComparer.Ordinal);

        // No dependencies to process - nothing to do
        if(pendingDependencies.Count == 0)
        {
            return;
        }

        while(pendingDependencies.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var dependency = pendingDependencies.Dequeue();
            var closedTypeName = dependency.ClosedTypeName;

            // Skip if already processed
            if(!processedDependencies.Add(closedTypeName))
            {
                continue;
            }

            // Check if the open generic version is registered
            if(!openGenericIndex.TryGetValue(dependency.OpenGenericKey, out var openGenericInfoList))
            {
                continue;
            }

            // Skip if this closed generic is already registered
            if(generatedClosedGenerics.Contains(closedTypeName))
            {
                continue;
            }

            // Track registration count before generating
            var registrationCountBefore = registrations.Count;

            // Try each open generic registration to find one that matches the closed type structure
            foreach(var openGenericInfo in openGenericInfoList)
            {
                // Generate the closed generic factory registration
                // Returns true if successful, allowing us to break early
                if(TryGenerateClosedGenericRegistration(
                    dependency.ClosedType,
                    openGenericInfo,
                    registrations,
                    generatedClosedGenerics))
                {
                    break; // Found a matching registration, stop searching
                }
            }

            // Collect new dependencies from newly generated registrations (including decorator parameters)
            for(var i = registrationCountBefore; i < registrations.Count; i++)
            {
                var newReg = registrations[i].Registration;

                // Collect from decorator constructor parameters
                foreach(var decorator in newReg.Decorators)
                {
                    if(decorator.ConstructorParameters is { Length: > 0 })
                    {
                        // Skip first parameter (it's the decorated service)
                        foreach(var param in decorator.ConstructorParameters.Skip(1))
                        {
                            CollectNewDependencyFromType(
                                param.Type,
                                pendingDependencies,
                                processedDependencies,
                                generatedClosedGenerics);
                        }
                    }

                    // Collect from decorator injection members (properties, fields, methods with [IocInject] attribute)
                    if(decorator.InjectionMembers is { Length: > 0 })
                    {
                        foreach(var member in decorator.InjectionMembers)
                        {
                            switch(member)
                            {
                                // For properties and fields, check the member type
                                case PropertyOrFieldInjectionData propertyOrField:
                                    CollectNewDependencyFromType(
                                        propertyOrField.Type,
                                        pendingDependencies,
                                        processedDependencies,
                                        generatedClosedGenerics);
                                    break;

                                // For methods, check each parameter type
                                case MethodInjectionData method:
                                    foreach(var param in method.Parameters)
                                    {
                                        CollectNewDependencyFromType(
                                            param.Type,
                                            pendingDependencies,
                                            processedDependencies,
                                            generatedClosedGenerics);
                                    }
                                    break;
                            }
                        }
                    }
                }

                // Collect from constructor parameters
                if(newReg.ImplementationType.ConstructorParameters is { Length: > 0 })
                {
                    foreach(var param in newReg.ImplementationType.ConstructorParameters)
                    {
                        CollectNewDependencyFromType(
                            param.Type,
                            pendingDependencies,
                            processedDependencies,
                            generatedClosedGenerics);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Collects a new closed generic dependency from a type and adds it to the pending queue.
    /// </summary>
    private static void CollectNewDependencyFromType(
        TypeData paramType,
        Queue<ClosedGenericDependency> pendingDependencies,
        HashSet<string> processedDependencies,
        HashSet<string> generatedClosedGenerics)
    {
        if(paramType is not GenericTypeData genericParamType)
        {
            return;
        }

        // Skip if not a closed generic type
        if(genericParamType.GenericArity == 0 || genericParamType.IsOpenGeneric || genericParamType.IsNestedOpenGeneric)
        {
            return;
        }

        // Skip if already processed or registered
        if(processedDependencies.Contains(paramType.Name) || generatedClosedGenerics.Contains(paramType.Name))
        {
            return;
        }

        // Add as a new dependency to process
        var dependency = new ClosedGenericDependency(
            paramType.Name,
            paramType,
            genericParamType.NameWithoutGeneric);
        pendingDependencies.Enqueue(dependency);
    }

    /// <summary>
    /// Tries to generate a closed generic registration based on an open generic registration.
    /// The closedServiceType is the closed service type from the dependency (e.g., IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;).
    /// Returns true if registration was successfully generated, false if type structure is incompatible.
    /// </summary>
    private static bool TryGenerateClosedGenericRegistration(
        TypeData closedServiceType,
        OpenGenericRegistrationInfo openGenericInfo,
        List<ServiceRegistrationWithTags> registrations,
        HashSet<string> generatedClosedGenerics)
    {
        if(closedServiceType is not GenericTypeData closedServiceGenericType)
        {
            return false;
        }

        if(openGenericInfo.ImplementationType is not GenericTypeData openImplType)
        {
            return false;
        }

        // Check if this is a factory-only registration (no explicit implementation type)
        // In this case, ImplementationType equals ServiceType (set in CreateOpenGenericEntriesFromDefaultSettings)
        if(openGenericInfo.ServiceTypes.Length == 0 || openGenericInfo.ServiceTypes[0] is not GenericTypeData firstServiceType)
        {
            return false;
        }

        var isFactoryOnlyRegistration = openGenericInfo.Factory is not null &&
            openImplType.NameWithoutGeneric == firstServiceType.NameWithoutGeneric &&
            openGenericInfo.AllInterfaces.Length == 0;

        // Find the matching open service type to build the type argument map
        // Prefer the forms the implementation declares (its interfaces, base classes, or itself), because they contain
        // the service type as implemented (e.g., IRequestHandler<Task<T1>, List<T2>> or IHandler<TRequest, string>)
        // while ServiceTypes may contain the open generic definition (e.g., IRequestHandler<TRequest, TResponse>)
        var implementedForm = FindImplementedForm(openImplType, openGenericInfo.AllInterfaces, closedServiceGenericType);
        var matchingOpenServiceType = implementedForm;

        // If the implementation does not declare it, check in ServiceTypes
        if(matchingOpenServiceType is null)
        {
            foreach(var serviceType in openGenericInfo.ServiceTypes)
            {
                if(serviceType is GenericTypeData genericServiceType && genericServiceType.NameWithoutGeneric == closedServiceGenericType.NameWithoutGeneric)
                {
                    matchingOpenServiceType = genericServiceType;
                    break;
                }
            }
        }

        if(matchingOpenServiceType is null)
        {
            return false; // No matching open service type found
        }

        // Build type argument substitution map from service type parameters
        // Map: open service type param name -> closed service type arg name
        // A form the implementation declares is written with the implementation's type parameters, so matching it
        // against the closed service type maps them directly and rejects closed types it cannot produce.
        Dictionary<string, string>? serviceTypeArgMap;
        if(implementedForm is not null && !isFactoryOnlyRegistration)
        {
            if(MatchAllTypeArguments(openImplType, implementedForm, closedServiceGenericType) is not { } typeArguments)
            {
                return false; // The implementation cannot produce this closed service type
            }
            serviceTypeArgMap = ToTypeArgMap(typeArguments);
        }
        else
        {
            serviceTypeArgMap = BuildTypeArgumentMapFromServiceType(
                matchingOpenServiceType,
                closedServiceGenericType);
        }
        if(serviceTypeArgMap is not { Count: > 0 })
        {
            return false; // Cannot build substitution map - type structure incompatible
        }

        // For factory-only registrations, we only generate service type registrations
        // since there's no explicit implementation type to register
        if(isFactoryOnlyRegistration)
        {
            return TryGenerateFactoryOnlyRegistration(
                closedServiceType,
                openGenericInfo,
                registrations,
                generatedClosedGenerics);
        }

        // Build mapping from open implementation type params to closed type args
        // This requires mapping through the service type params
        var implTypeArgMap = BuildImplTypeArgMapFromServiceTypeMap(
            openImplType,
            matchingOpenServiceType,
            serviceTypeArgMap);
        if(implTypeArgMap is not { Count: > 0 })
        {
            return false;
        }

        // Build closed implementation type name
        var closedImplTypeName = SubstituteTypeArguments(openImplType.Name, implTypeArgMap);

        // Build closed implementation type with substituted constructor parameters
        var closedImplType = BuildClosedImplTypeData(
            closedImplTypeName,
            openImplType,
            implTypeArgMap);

        // Build all closed service types from the open generic's service types
        var closedServiceTypes = BuildClosedServiceTypesFromServiceTypeMap(
            openGenericInfo.ServiceTypes,
            openImplType,
            openGenericInfo.AllInterfaces,
            serviceTypeArgMap,
            implTypeArgMap);

        // For generic factory methods (with [IocGenericFactory]), factory is only applicable to service types
        // because generic type mapping requires matching service type template.
        // For regular factories, apply to both implementation and service types.
        var implFactory = openGenericInfo.Factory?.TypeParameterCount > 0 ? null : openGenericInfo.Factory;

        // Create registration for the implementation type itself
        var implModel = new ServiceRegistrationModel(
            closedImplType,
            closedImplType,
            openGenericInfo.Lifetime,
            openGenericInfo.Key,
            openGenericInfo.KeyType,
            openGenericInfo.KeyValueType,
            IsOpenGeneric: false,
            [],
            openGenericInfo.InjectionMembers,
            Factory: implFactory,
            openGenericInfo.Instance);

        // Add registration with tags
        registrations.Add(new ServiceRegistrationWithTags(implModel, openGenericInfo.Tags));
        generatedClosedGenerics.Add(closedImplType.Name);

        // Create registrations for each closed service type
        foreach(var closedSvcType in closedServiceTypes)
        {
            if(closedSvcType.Name == closedImplType.Name)
            {
                continue; // Skip if same as implementation type
            }

            // Skip if already registered
            if(generatedClosedGenerics.Contains(closedSvcType.Name))
            {
                continue;
            }

            // Skip nested open generic service types
            if(closedSvcType is GenericTypeData { IsNestedOpenGeneric: true } or GenericTypeData { IsOpenGeneric: true })
            {
                continue;
            }

            // Process decorators for this specific service type
            // Use service type's type parameters for decorator substitution
            var serviceTypeDecorators = ProcessDecoratorsForServiceType(
                openGenericInfo.Decorators,
                WithTypeArgumentsOf(closedSvcType, closedServiceGenericType));

            var serviceModel = new ServiceRegistrationModel(
                closedSvcType,
                closedImplType,
                openGenericInfo.Lifetime,
                openGenericInfo.Key,
                openGenericInfo.KeyType,
                openGenericInfo.KeyValueType,
                IsOpenGeneric: false,
                serviceTypeDecorators,
                openGenericInfo.InjectionMembers,
                openGenericInfo.Factory,
                openGenericInfo.Instance);

            registrations.Add(new ServiceRegistrationWithTags(serviceModel, openGenericInfo.Tags));
            generatedClosedGenerics.Add(closedSvcType.Name);
        }

        return true; // Successfully generated registration
    }

    /// <summary>
    /// Generates a factory-only closed generic registration.
    /// This is used when IocRegisterDefaults has a Factory but no explicit ImplementationType.
    /// Only generates service type registration using the factory method.
    /// </summary>
    private static bool TryGenerateFactoryOnlyRegistration(
        TypeData closedServiceType,
        OpenGenericRegistrationInfo openGenericInfo,
        List<ServiceRegistrationWithTags> registrations,
        HashSet<string> generatedClosedGenerics)
    {
        if(closedServiceType is not GenericTypeData { IsNestedOpenGeneric: false, IsOpenGeneric: false })
        {
            return false;
        }

        // Skip if already registered
        if(generatedClosedGenerics.Contains(closedServiceType.Name))
        {
            return true; // Already registered, consider it success
        }

        // Process decorators for this specific service type
        var serviceTypeDecorators = ProcessDecoratorsForServiceType(
            openGenericInfo.Decorators,
            closedServiceType);

        // For factory-only registration, both ServiceType and ImplementationType are the same (the service type)
        // The factory method will create the actual instance
        var serviceModel = new ServiceRegistrationModel(
            closedServiceType,
            closedServiceType, // Use service type as implementation type since factory creates the instance
            openGenericInfo.Lifetime,
            openGenericInfo.Key,
            openGenericInfo.KeyType,
            openGenericInfo.KeyValueType,
            IsOpenGeneric: false,
            serviceTypeDecorators,
            openGenericInfo.InjectionMembers,
            openGenericInfo.Factory,
            openGenericInfo.Instance);

        registrations.Add(new ServiceRegistrationWithTags(serviceModel, openGenericInfo.Tags));
        generatedClosedGenerics.Add(closedServiceType.Name);

        return true; // Successfully generated registration
    }
}
