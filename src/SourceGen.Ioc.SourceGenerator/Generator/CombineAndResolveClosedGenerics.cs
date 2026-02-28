namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
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
        in ImmutableArray<ClosedGenericDependency> serviceProviderInvocations,
        in ImmutableArray<OpenGenericEntry> factoryBasedOpenGenericEntries,
        CancellationToken ct)
    {
        var registrations = new List<ServiceRegistrationWithTags>();

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
                openGenericIndex ??= new Dictionary<string, List<OpenGenericRegistrationInfo>>(StringComparer.Ordinal);
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
                closedGenericDependencies ??= new Dictionary<string, ClosedGenericDependency>(StringComparer.Ordinal);
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
            openGenericIndex ??= new Dictionary<string, List<OpenGenericRegistrationInfo>>(StringComparer.Ordinal);
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
            closedGenericDependencies ??= new Dictionary<string, ClosedGenericDependency>(StringComparer.Ordinal);
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
                closedGenericDependencies ?? new Dictionary<string, ClosedGenericDependency>(StringComparer.Ordinal),
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
        Dictionary<string, ClosedGenericDependency> closedGenericDependencies,
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
        var pendingDependencies = new Queue<ClosedGenericDependency>(closedGenericDependencies.Values);
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
                            // For properties and fields, check the member type
                            if(member.Type is not null)
                            {
                                CollectNewDependencyFromType(
                                    member.Type,
                                    pendingDependencies,
                                    processedDependencies,
                                    generatedClosedGenerics);
                            }

                            // For methods, check each parameter type
                            if(member.Parameters is not null)
                            {
                                foreach(var param in member.Parameters)
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
        // Prefer AllInterfaces over ServiceTypes because AllInterfaces contains
        // the actual interface as implemented (e.g., IRequestHandler<Task<T1>, List<T2>>)
        // while ServiceTypes may contain the open generic definition (e.g., IRequestHandler<TRequest, TResponse>)
        GenericTypeData? matchingOpenServiceType = null;

        // First, check in AllInterfaces (for nested open generics - this is the preferred match)
        foreach(var iface in openGenericInfo.AllInterfaces)
        {
            if(iface is GenericTypeData genericInterface && genericInterface.NameWithoutGeneric == closedServiceGenericType.NameWithoutGeneric)
            {
                matchingOpenServiceType = genericInterface;
                break;
            }
        }

        // If not found in AllInterfaces, check in ServiceTypes
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
        var serviceTypeArgMap = BuildTypeArgumentMapFromServiceType(
            matchingOpenServiceType,
            closedServiceGenericType);
        if(serviceTypeArgMap.IsDefaultOrEmpty)
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
        if(implTypeArgMap.IsDefaultOrEmpty)
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
                closedSvcType);

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

    /// <summary>
    /// Builds a type argument map from open service type to closed service type.
    /// For nested open generics like IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt;,
    /// this extracts the actual type parameters (T -> Entity) by comparing
    /// the nested type arguments.
    /// </summary>
    private static TypeArgMap BuildTypeArgumentMapFromServiceType(
        GenericTypeData? openServiceType,
        GenericTypeData? closedServiceType)
    {
        if(openServiceType is null || closedServiceType is null)
        {
            return default;
        }

        var openTypeParams = openServiceType.TypeParameters;
        var closedTypeParams = closedServiceType.TypeParameters;

        if(openTypeParams is null || closedTypeParams is null)
        {
            return default;
        }

        if(openTypeParams.Length != closedTypeParams.Length)
        {
            return default;
        }

        var typeArgMap = new TypeArgMap(openTypeParams.Length);

        // For non-nested open generics (like ILogger<T>), use direct mapping
        if(!openServiceType.IsNestedOpenGeneric)
        {
            for(int i = 0; i < openTypeParams.Length; i++)
            {
                typeArgMap[openTypeParams[i].ParameterName] = closedTypeParams[i].Type.Name;
            }
            return typeArgMap;
        }

        // For nested open generics, we need to extract the actual type parameter mappings
        // by comparing nested type arguments
        // If extraction fails (incompatible structures), return empty map
        if(!ExtractTypeArgumentMappings(openTypeParams, closedTypeParams, ref typeArgMap))
        {
            return default;
        }

        return typeArgMap;
    }

    /// <summary>
    /// Extracts type argument mappings from nested open generic types using TypeParameters.
    /// For example, for IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt; closed with
    /// IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;,
    /// this extracts T -> Entity.
    /// Returns false if the type structures are incompatible.
    /// </summary>
    private static bool ExtractTypeArgumentMappings(
        ImmutableEquatableArray<TypeParameter> openParams,
        ImmutableEquatableArray<TypeParameter> closedParams,
        ref TypeArgMap typeArgMap)
    {
        for(int i = 0; i < openParams.Length; i++)
        {
            var openParam = openParams[i];
            var closedParam = closedParams[i];
            var openParamType = openParam.Type;
            var closedParamType = closedParam.Type;

            // If the open param type is the same as the parameter name, it's a direct type parameter reference
            // e.g., TypeParameter { ParameterName = "T", Type = { Name = "T" } }
            if(openParamType.Name == openParam.ParameterName)
            {
                // Direct type parameter, map it to the closed type
                typeArgMap[openParam.ParameterName] = closedParamType.Name;
                continue;
            }

            // The open param type is a constructed type with nested type parameters
            // e.g., GenericRequest<T> or List<T>
            // Use the recursively extracted TypeParameters from TypeData
            if(!ExtractTypeArgumentMappingsFromTypeData(openParamType, closedParamType, ref typeArgMap))
            {
                return false; // Incompatible structure
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts type argument mappings by comparing TypeData's TypeParameters recursively.
    /// For example, comparing GenericRequest&lt;T&gt; with GenericRequest&lt;Entity&gt;
    /// extracts T -> Entity.
    /// Returns false if the type structures are incompatible (e.g., different base types).
    /// </summary>
    private static bool ExtractTypeArgumentMappingsFromTypeData(
        TypeData openType,
        TypeData closedType,
        ref TypeArgMap typeArgMap)
    {
        if(openType is not GenericTypeData openGenericType || closedType is not GenericTypeData closedGenericType)
        {
            return openType.Name == closedType.Name;
        }

        // Verify base type names match (excluding generic arguments)
        // e.g., GenericRequest<T> vs TestRequest should fail because GenericRequest != TestRequest
        if(openGenericType.NameWithoutGeneric != closedGenericType.NameWithoutGeneric)
        {
            return false; // Incompatible type structure
        }

        var openTypeParams = openGenericType.TypeParameters;
        var closedTypeParams = closedGenericType.TypeParameters;

        // If no type parameters, nothing to extract but structure is compatible
        if(openTypeParams is null || closedTypeParams is null)
        {
            return true;
        }

        // Arity must match
        if(openTypeParams.Length != closedTypeParams.Length)
        {
            return false;
        }

        for(int i = 0; i < openTypeParams.Length; i++)
        {
            var openTypeParam = openTypeParams[i];
            var closedTypeParam = closedTypeParams[i];
            var openNestedType = openTypeParam.Type;
            var closedNestedType = closedTypeParam.Type;

            // Check if openNestedType is a type parameter (determined at creation time from TypeKind)
            if(openNestedType is TypeParameterTypeData)
            {
                // Direct mapping: T -> Entity (use the full closed type name)
                typeArgMap[openNestedType.Name] = closedNestedType.Name;
            }
            else if(openNestedType is GenericTypeData { TypeParameters: { Length: > 0 } })
            {
                // Nested generic type, recurse using TypeParameters
                if(!ExtractTypeArgumentMappingsFromTypeData(openNestedType, closedNestedType, ref typeArgMap))
                {
                    return false;
                }
            }
            else
            {
                // Non-generic, non-type-parameter type - must match exactly
                if(openNestedType.Name != closedNestedType.Name)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Builds implementation type argument map by mapping service type params to implementation type params.
    /// For nested open generics, this uses the serviceTypeArgMap which already contains
    /// the extracted type parameter mappings (e.g., T -> Entity).
    /// </summary>
    private static TypeArgMap BuildImplTypeArgMapFromServiceTypeMap(
        GenericTypeData openImplType,
        GenericTypeData? openServiceType,
        TypeArgMap serviceTypeArgMap)
    {
        var implTypeParams = openImplType.TypeParameters;

        if(implTypeParams is null)
        {
            return default;
        }

        // For nested open generics, the serviceTypeArgMap already contains
        // the direct type parameter mappings (e.g., T -> Entity)
        // These should match the implementation type parameters directly
        var implTypeArgMap = new TypeArgMap(implTypeParams.Length);
        foreach(var implParam in implTypeParams)
        {
            // Check if the serviceTypeArgMap contains a mapping for this impl type param
            if(serviceTypeArgMap.TryGetValue(implParam.ParameterName, out var closedTypeArg))
            {
                implTypeArgMap[implParam.ParameterName] = closedTypeArg;
            }
        }

        return implTypeArgMap;
    }

    /// <summary>
    /// Builds closed implementation TypeData with substituted constructor parameters.
    /// </summary>
    private static GenericTypeData BuildClosedImplTypeData(
        string closedImplTypeName,
        GenericTypeData openImplType,
        TypeArgMap typeArgMap)
    {
        // Build closed type parameters
        var openTypeParams = openImplType.TypeParameters;
        ImmutableEquatableArray<TypeParameter>? closedTypeParams = null;
        if(openTypeParams is not null && openTypeParams.Length > 0)
        {
            var newParams = new List<TypeParameter>(openTypeParams.Length);
            foreach(var param in openTypeParams)
            {
                if(typeArgMap.TryGetValue(param.ParameterName, out var closedTypeName))
                {
                    var closedType = TypeData.CreateSimple(closedTypeName);
                    newParams.Add(new TypeParameter(param.ParameterName, closedType));
                }
            }
            closedTypeParams = newParams.ToImmutableEquatableArray();
        }

        // Build closed constructor parameters
        var openConstructorParams = openImplType.ConstructorParameters;
        ImmutableEquatableArray<ParameterData>? closedConstructorParams = null;
        if(openConstructorParams is not null && openConstructorParams.Length > 0)
        {
            var newParams = new List<ParameterData>(openConstructorParams.Length);
            foreach(var param in openConstructorParams)
            {
                var newParamTypeName = SubstituteTypeArguments(param.Type.Name, typeArgMap);
                var newParamType = RehydrateTypeData(param.Type, Name: newParamTypeName, IsOpenGeneric: false);
                newParams.Add(param with { Type = newParamType });
            }
            closedConstructorParams = newParams.ToImmutableEquatableArray();
        }

        return TypeData.CreateGeneric(
            closedImplTypeName,
            openImplType.NameWithoutGeneric,
            IsOpenGeneric: false,
            openImplType.GenericArity,
            IsNestedOpenGeneric: false,
            TypeParameters: closedTypeParams,
            ConstructorParameters: closedConstructorParams,
            HasInjectConstructor: openImplType.HasInjectConstructor);
    }

    /// <summary>
    /// Builds all closed service types from open service types using the service type argument map.
    /// </summary>
    private static List<TypeData> BuildClosedServiceTypesFromServiceTypeMap(
        ImmutableEquatableArray<TypeData> openServiceTypes,
        TypeArgMap serviceTypeArgMap,
        TypeArgMap implTypeArgMap)
    {
        var result = new List<TypeData>();

        foreach(var openServiceType in openServiceTypes)
        {
            if(openServiceType is not GenericTypeData openGenericServiceType)
            {
                result.Add(openServiceType);
                continue;
            }

            // Skip non-generic service types
            if(openGenericServiceType.GenericArity == 0)
            {
                result.Add(openServiceType);
                continue;
            }

            // Use service type arg map for service types, impl type arg map for impl types
            var typeArgMapToUse = serviceTypeArgMap;

            // Build closed service type name
            var closedServiceTypeName = SubstituteTypeArguments(openServiceType.Name, typeArgMapToUse);

            // Also try with impl type arg map if service type arg map doesn't fully substitute
            if(closedServiceTypeName.Contains('>') || openGenericServiceType.IsOpenGeneric)
            {
                closedServiceTypeName = SubstituteTypeArguments(closedServiceTypeName, implTypeArgMap);
            }

            // Skip if the substitution didn't fully resolve the type (still contains type parameters)
            // This happens when ServiceTypes contains pure open generics like IRequestHandler<TRequest, TResponse>
            // and the type parameter names don't match the implementation's type parameters
            if(StillContainsUnresolvedTypeParameters(closedServiceTypeName, openServiceType))
            {
                continue;
            }

            var closedServiceType = TypeData.CreateGeneric(
                closedServiceTypeName,
                openGenericServiceType.NameWithoutGeneric,
                IsOpenGeneric: false,
                openGenericServiceType.GenericArity,
                IsNestedOpenGeneric: false,
                TypeParameters: SubstituteTypeParameters(openGenericServiceType.TypeParameters, serviceTypeArgMap));

            result.Add(closedServiceType);
        }

        return result;
    }

    /// <summary>
    /// Checks if the substituted type name still contains unresolved type parameters.
    /// This happens when the original type has type parameters that weren't in the substitution map.
    /// </summary>
    private static bool StillContainsUnresolvedTypeParameters(string closedTypeName, TypeData openServiceType)
    {
        // If the type parameters are null or empty, it's not a generic type to begin with
        var typeParams = openServiceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;
        if(typeParams is null || typeParams.Length == 0)
        {
            return false;
        }

        // Check if any of the original type parameter names still appear in the closed type name
        // This indicates incomplete substitution
        foreach(var typeParam in typeParams)
        {
            var paramTypeName = typeParam.Type.Name;
            // Check if this type parameter is a simple type parameter (not a generic type)
            // and still appears in the closed type name
            if(typeParam.Type is TypeParameterTypeData
                or GenericTypeData { IsOpenGeneric: true, GenericArity: 0 })
            {
                // Check if the parameter name still appears between < and > or < and ,
                if(closedTypeName.Contains($"<{paramTypeName}>") ||
                   closedTypeName.Contains($"<{paramTypeName},") ||
                   closedTypeName.Contains($", {paramTypeName}>") ||
                   closedTypeName.Contains($", {paramTypeName},"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Substitutes type parameters in TypeParameter array with actual types.
    /// Recursively processes nested type parameters for generic types like Task&lt;T&gt;.
    /// </summary>
    private static ImmutableEquatableArray<TypeParameter>? SubstituteTypeParameters(
        ImmutableEquatableArray<TypeParameter>? typeParams,
        TypeArgMap typeArgMap)
    {
        if(typeParams is null || typeParams.Length == 0)
        {
            return null;
        }

        var result = new List<TypeParameter>(typeParams.Length);
        foreach(var param in typeParams)
        {
            var substitutedType = SubstituteTypeData(param.Type, typeArgMap);
            result.Add(param with { Type = substitutedType });
        }

        return result.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Recursively substitutes type arguments in a TypeData, including nested type parameters.
    /// For example, Task&lt;T&gt; with T -> Entity becomes Task&lt;Entity&gt; with proper TypeParameters.
    /// </summary>
    private static TypeData SubstituteTypeData(TypeData typeData, TypeArgMap typeArgMap)
    {
        var newTypeName = SubstituteTypeArguments(typeData.Name, typeArgMap);
        var newTypeParams = SubstituteTypeParameters(
            typeData is GenericTypeData genericTypeData ? genericTypeData.TypeParameters : null,
            typeArgMap);

        return RehydrateTypeData(typeData, Name: newTypeName, IsOpenGeneric: false, TypeParameters: newTypeParams, OverrideTypeParameters: true);
    }

    /// <summary>
    /// Rehydrates a <see cref="TypeData"/> instance while preserving subtype semantics.
    /// </summary>
    private static TypeData RehydrateTypeData(
        TypeData source,
        string? Name = null,
        bool? IsOpenGeneric = null,
        bool? IsNestedOpenGeneric = null,
        ImmutableEquatableArray<TypeParameter>? TypeParameters = null,
        bool OverrideTypeParameters = false)
    {
        var newName = Name ?? source.Name;
        var sourceGenericType = source as GenericTypeData;
        var sourceWrapperType = source as WrapperTypeData;
        var newIsOpenGeneric = IsOpenGeneric ?? sourceGenericType?.IsOpenGeneric ?? false;
        var newIsNestedOpenGeneric = IsNestedOpenGeneric ?? sourceGenericType?.IsNestedOpenGeneric ?? false;
        var newTypeParameters = OverrideTypeParameters ? TypeParameters : sourceGenericType?.TypeParameters;

        if(sourceWrapperType is not null)
        {
            return TypeData.CreateWrapper(
                newName,
                sourceWrapperType.NameWithoutGeneric,
                newIsOpenGeneric,
                sourceWrapperType.GenericArity,
                sourceWrapperType.WrapperKind,
                newIsNestedOpenGeneric,
                newTypeParameters,
                source.ConstructorParameters,
                source.HasInjectConstructor,
                source.InjectionMembers,
                source.AllInterfaces,
                source.AllBaseClasses);
        }

        if(sourceGenericType is not null && (newIsOpenGeneric || sourceGenericType.GenericArity > 0 || newTypeParameters is { Length: > 0 } || source is TypeParameterTypeData))
        {
            return TypeData.CreateGeneric(
                newName,
                sourceGenericType.NameWithoutGeneric,
                newIsOpenGeneric,
                sourceGenericType.GenericArity,
                newIsNestedOpenGeneric,
                newTypeParameters,
                source.ConstructorParameters,
                source.HasInjectConstructor,
                source.InjectionMembers,
                source.AllInterfaces,
                source.AllBaseClasses);
        }

        return TypeData.CreateSimple(
            newName,
            source.ConstructorParameters,
            source.HasInjectConstructor,
            source.InjectionMembers,
            source.AllInterfaces,
            source.AllBaseClasses);
    }

    /// <summary>
    /// Processes decorators for a specific service type, using the service type's type parameters for substitution.
    /// This is used for closed generic registrations where the decorator types need to be closed based on
    /// the service type (interface) rather than the implementation type.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> ProcessDecoratorsForServiceType(
        ImmutableEquatableArray<TypeData> decorators,
        TypeData serviceType)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        var serviceTypeParams = serviceType is GenericTypeData genericServiceType
            ? genericServiceType.TypeParameters
            : null;
        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            // No type parameters, return decorators as-is
            return decorators;
        }

        // Filter decorators based on type constraints
        var interfaceNameSet = BuildInterfaceNameSet(serviceTypeParams);
        var filteredDecorators = FilterDecoratorsCore(decorators, serviceTypeParams, interfaceNameSet);
        if(filteredDecorators.Length == 0)
        {
            return [];
        }

        // Process and substitute type parameters in decorators using service type's type parameters
        var processedDecorators = new List<TypeData>(filteredDecorators.Length);
        foreach(var decorator in filteredDecorators)
        {
            var processedDecorator = SubstituteDecoratorTypeParams(decorator, serviceTypeParams);
            processedDecorators.Add(processedDecorator);
        }

        return processedDecorators.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Substitutes type parameters in a decorator and processes its constructor parameters and injection members.
    /// </summary>
    private static TypeData SubstituteDecoratorTypeParams(
        TypeData decorator,
        ImmutableEquatableArray<TypeParameter> closedTypeParams)
    {
        if(decorator is not GenericTypeData { IsOpenGeneric: true } genericDecorator)
        {
            // No longer need to process decorator parameters for IsServiceParameter
            return decorator;
        }

        // Build type argument map from decorator type params to closed type args
        var decoratorTypeParams = genericDecorator.TypeParameters;
        if(decoratorTypeParams is null || decoratorTypeParams.Length == 0)
        {
            return decorator;
        }

        // Build substitution map: decorator param name -> closed type arg name
        int mapSize = Math.Min(decoratorTypeParams.Length, closedTypeParams.Length);
        var typeArgMap = new TypeArgMap(mapSize);
        for(int i = 0; i < mapSize; i++)
        {
            typeArgMap[decoratorTypeParams[i].ParameterName] = closedTypeParams[i].Type.Name;
        }

        // Substitute in decorator type name
        var closedDecoratorName = SubstituteTypeArguments(decorator.Name, typeArgMap);
        var closedDecoratorNameWithoutGeneric = genericDecorator.NameWithoutGeneric;

        // Substitute in constructor parameters
        var constructorParams = decorator.ConstructorParameters;
        ImmutableEquatableArray<ParameterData>? closedConstructorParams = null;
        if(constructorParams is not null && constructorParams.Length > 0)
        {
            var newParams = new List<ParameterData>(constructorParams.Length);
            foreach(var param in constructorParams)
            {
                // Use SubstituteTypeData to recursively substitute type parameters
                // This ensures nested generics like ILogger<HandlerDecorator1<TRequest, TResponse>>
                // get their TypeParameters correctly substituted
                var substitutedType = SubstituteTypeData(param.Type, typeArgMap);
                var newParamType = RehydrateTypeData(substitutedType, IsNestedOpenGeneric: false);
                newParams.Add(param with { Type = newParamType });
            }
            closedConstructorParams = newParams.ToImmutableEquatableArray();
        }

        // Substitute in injection members (properties, fields, methods with [IocInject] attributes)
        var injectionMembers = decorator.InjectionMembers;
        ImmutableEquatableArray<InjectionMemberData>? closedInjectionMembers = null;
        if(injectionMembers is not null && injectionMembers.Length > 0)
        {
            var newMembers = new List<InjectionMemberData>(injectionMembers.Length);
            foreach(var member in injectionMembers)
            {
                var newMember = SubstituteInjectionMember(member, typeArgMap);
                newMembers.Add(newMember);
            }
            closedInjectionMembers = newMembers.ToImmutableEquatableArray();
        }

        // Substitute in type parameters
        var newTypeParams = SubstituteTypeParameters(decoratorTypeParams, typeArgMap);

        return TypeData.CreateGeneric(
            closedDecoratorName,
            closedDecoratorNameWithoutGeneric,
            IsOpenGeneric: false,
            genericDecorator.GenericArity,
            IsNestedOpenGeneric: false,
            TypeParameters: newTypeParams,
            ConstructorParameters: closedConstructorParams,
            HasInjectConstructor: false,
            InjectionMembers: closedInjectionMembers);
    }

    /// <summary>
    /// Substitutes type parameters in an injection member.
    /// </summary>
    private static InjectionMemberData SubstituteInjectionMember(
        InjectionMemberData member,
        TypeArgMap typeArgMap)
    {
        // For properties and fields, substitute the member type
        TypeData? newType = null;
        if(member.Type is not null)
        {
            var substitutedType = SubstituteTypeData(member.Type, typeArgMap);
            newType = RehydrateTypeData(substitutedType, IsNestedOpenGeneric: false);
        }

        // For methods, substitute each parameter type
        ImmutableEquatableArray<ParameterData>? newParams = null;
        if(member.Parameters is not null && member.Parameters.Length > 0)
        {
            var paramList = new List<ParameterData>(member.Parameters.Length);
            foreach(var param in member.Parameters)
            {
                var substitutedType = SubstituteTypeData(param.Type, typeArgMap);
                var newParamType = RehydrateTypeData(substitutedType, IsNestedOpenGeneric: false);
                paramList.Add(param with { Type = newParamType });
            }
            newParams = paramList.ToImmutableEquatableArray();
        }

        return member with { Type = newType, Parameters = newParams };
    }
}
