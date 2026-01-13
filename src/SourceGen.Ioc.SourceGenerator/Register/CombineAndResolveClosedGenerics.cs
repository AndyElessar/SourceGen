namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    /// <summary>
    /// Pipeline 2: Combines all basic registration results and resolves closed generic dependencies.
    /// This method re-runs when any BasicRegistrationResult changes or when invocations change.
    /// </summary>
    /// <param name="basicResults">The basic registration results from pipeline 1.</param>
    /// <param name="serviceProviderInvocations">Closed generic types from GetService/GetRequiredService invocations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Service registrations grouped by method name.</returns>
    private static ImmutableEquatableDictionary<string, ImmutableEquatableArray<ServiceRegistrationModel>> CombineAndResolveClosedGenerics(
        in ImmutableArray<BasicRegistrationResult> basicResults,
        in ImmutableArray<ClosedGenericDependency> serviceProviderInvocations,
        CancellationToken ct)
    {
        var methodGroups = new Dictionary<string, List<ServiceRegistrationModel>>(StringComparer.Ordinal);

        // Use List to allow multiple implementations per service type key
        // (e.g., GenericRequestHandler<T> and GenericRequestHandler2<T> both implement IRequestHandler<,>)
        Dictionary<string, List<OpenGenericRegistrationInfo>>? openGenericIndex = null;
        Dictionary<string, ClosedGenericDependency>? closedGenericDependencies = null;

        // Process each basic result
        foreach(var result in basicResults)
        {
            ct.ThrowIfCancellationRequested();

            // Add service registrations to method groups
            foreach(var model in result.ServiceRegistrations)
            {
                if(!result.TagOnly)
                {
                    AddToMethodGroup(methodGroups, DefaultMethodKey, model);
                }

                foreach(var tag in result.Tags)
                {
                    AddToMethodGroup(methodGroups, tag, model);
                }
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

        // Generate factory registrations only when both dictionaries have data
        if(openGenericIndex is not null && closedGenericDependencies is not null)
        {
            GenerateClosedGenericFactoryRegistrations(
                openGenericIndex,
                closedGenericDependencies,
                methodGroups,
                ct);
        }

        return methodGroups
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .ToImmutableEquatableDictionary(
                static kvp => kvp.Key,
                static kvp => kvp.Value.ToImmutableEquatableArray());
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
    /// Generates factory registrations for closed generic types that depend on open generic registrations.
    /// </summary>
    private static void GenerateClosedGenericFactoryRegistrations(
        Dictionary<string, List<OpenGenericRegistrationInfo>> openGenericIndex,
        Dictionary<string, ClosedGenericDependency> closedGenericDependencies,
        Dictionary<string, List<ServiceRegistrationModel>> methodGroups,
        CancellationToken ct)
    {
        if(openGenericIndex.Count == 0 || closedGenericDependencies.Count == 0)
        {
            return;
        }

        // Track already generated closed generic registrations to avoid duplicates
        var generatedClosedGenerics = new HashSet<string>(StringComparer.Ordinal);

        // First, collect all existing registrations to avoid duplicates
        // This includes both implementation types and service types
        foreach(var group in methodGroups.Values)
        {
            foreach(var model in group)
            {
                generatedClosedGenerics.Add(model.ImplementationType.Name);
                generatedClosedGenerics.Add(model.ServiceType.Name);
            }
        }

        foreach(var kvp in closedGenericDependencies)
        {
            ct.ThrowIfCancellationRequested();

            var closedTypeName = kvp.Key;
            var dependency = kvp.Value;

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

            // Try each open generic registration to find one that matches the closed type structure
            foreach(var openGenericInfo in openGenericInfoList)
            {
                // Generate the closed generic factory registration
                // Returns true if successful, allowing us to break early
                if(TryGenerateClosedGenericRegistration(
                    dependency.ClosedType,
                    openGenericInfo,
                    methodGroups,
                    generatedClosedGenerics))
                {
                    break; // Found a matching registration, stop searching
                }
            }
        }
    }

    /// <summary>
    /// Tries to generate a closed generic registration based on an open generic registration.
    /// The closedServiceType is the closed service type from the dependency (e.g., IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;).
    /// Returns true if registration was successfully generated, false if type structure is incompatible.
    /// </summary>
    private static bool TryGenerateClosedGenericRegistration(
        TypeData closedServiceType,
        OpenGenericRegistrationInfo openGenericInfo,
        Dictionary<string, List<ServiceRegistrationModel>> methodGroups,
        HashSet<string> generatedClosedGenerics)
    {
        var openImplType = openGenericInfo.ImplementationType;

        // Find the matching open service type to build the type argument map
        // First, check in ServiceTypes
        TypeData? matchingOpenServiceType = null;
        foreach(var serviceType in openGenericInfo.ServiceTypes)
        {
            if(serviceType.NameWithoutGeneric == closedServiceType.NameWithoutGeneric)
            {
                matchingOpenServiceType = serviceType;
                break;
            }
        }

        // If not found in ServiceTypes, check in AllInterfaces (for nested open generics)
        if(matchingOpenServiceType is null)
        {
            foreach(var iface in openGenericInfo.AllInterfaces)
            {
                if(iface.NameWithoutGeneric == closedServiceType.NameWithoutGeneric)
                {
                    matchingOpenServiceType = iface;
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
            closedServiceType);
        if(serviceTypeArgMap.IsDefaultOrEmpty)
        {
            return false; // Cannot build substitution map - type structure incompatible
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

        // Create registration for the implementation type itself
        var implModel = new ServiceRegistrationModel(
            closedImplType,
            closedImplType,
            openGenericInfo.Lifetime,
            openGenericInfo.Key,
            openGenericInfo.KeyType,
            IsOpenGeneric: false,
            [],
            openGenericInfo.InjectionMembers,
            openGenericInfo.Factory,
            openGenericInfo.Instance);

        // Add to method groups
        if(!openGenericInfo.TagOnly)
        {
            AddToMethodGroup(methodGroups, DefaultMethodKey, implModel);
        }
        foreach(var tag in openGenericInfo.Tags)
        {
            AddToMethodGroup(methodGroups, tag, implModel);
        }
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
            if(closedSvcType.IsNestedOpenGeneric || closedSvcType.IsOpenGeneric)
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
                IsOpenGeneric: false,
                serviceTypeDecorators,
                openGenericInfo.InjectionMembers,
                openGenericInfo.Factory,
                openGenericInfo.Instance);

            if(!openGenericInfo.TagOnly)
            {
                AddToMethodGroup(methodGroups, DefaultMethodKey, serviceModel);
            }
            foreach(var tag in openGenericInfo.Tags)
            {
                AddToMethodGroup(methodGroups, tag, serviceModel);
            }
            generatedClosedGenerics.Add(closedSvcType.Name);
        }

        return true; // Successfully generated registration
    }

    /// <summary>
    /// Builds a type argument map from open service type to closed service type.
    /// For nested open generics like IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt;,
    /// this extracts the actual type parameters (T -> Entity) by comparing
    /// the nested type arguments.
    /// </summary>
    private static TypeArgMap BuildTypeArgumentMapFromServiceType(
        TypeData openServiceType,
        TypeData closedServiceType)
    {
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
        // Verify base type names match (excluding generic arguments)
        // e.g., GenericRequest<T> vs TestRequest should fail because GenericRequest != TestRequest
        if(openType.NameWithoutGeneric != closedType.NameWithoutGeneric)
        {
            return false; // Incompatible type structure
        }

        var openTypeParams = openType.TypeParameters;
        var closedTypeParams = closedType.TypeParameters;

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
            if(openNestedType.IsTypeParameter)
            {
                // Direct mapping: T -> Entity (use the full closed type name)
                typeArgMap[openNestedType.Name] = closedNestedType.Name;
            }
            else if(openNestedType.TypeParameters is not null && openNestedType.TypeParameters.Length > 0)
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
        TypeData openImplType,
        TypeData openServiceType,
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
    private static TypeData BuildClosedImplTypeData(
        string closedImplTypeName,
        TypeData openImplType,
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
                    var closedType = new TypeData(
                        closedTypeName,
                        closedTypeName, // For concrete types, Name == NameWithoutGeneric
                        IsOpenGeneric: false,
                        GenericArity: 0);
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
                var newParamType = param.Type with
                {
                    Name = newParamTypeName,
                    IsOpenGeneric = false
                };
                newParams.Add(param with { Type = newParamType });
            }
            closedConstructorParams = newParams.ToImmutableEquatableArray();
        }

        return new TypeData(
            closedImplTypeName,
            openImplType.NameWithoutGeneric,
            IsOpenGeneric: false,
            openImplType.GenericArity,
            IsNestedOpenGeneric: false,
            IsTypeParameter: false, // Closed implementation types are not type parameters
            IsNonEnumerableCollection: false, // Closed impl types are not collection types
            closedTypeParams,
            closedConstructorParams,
            openImplType.HasInjectConstructor);
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
            // Skip non-generic service types
            if(openServiceType.GenericArity == 0)
            {
                result.Add(openServiceType);
                continue;
            }

            // Use service type arg map for service types, impl type arg map for impl types
            var typeArgMapToUse = serviceTypeArgMap;

            // Build closed service type name
            var closedServiceTypeName = SubstituteTypeArguments(openServiceType.Name, typeArgMapToUse);

            // Also try with impl type arg map if service type arg map doesn't fully substitute
            if(closedServiceTypeName.Contains('>') || openServiceType.IsOpenGeneric)
            {
                closedServiceTypeName = SubstituteTypeArguments(closedServiceTypeName, implTypeArgMap);
            }

            var closedServiceType = new TypeData(
                closedServiceTypeName,
                openServiceType.NameWithoutGeneric,
                IsOpenGeneric: false,
                openServiceType.GenericArity,
                IsNestedOpenGeneric: false,
                IsTypeParameter: false, // Closed service types are not type parameters
                IsNonEnumerableCollection: false, // Closed service types are not collection types
                SubstituteTypeParameters(openServiceType.TypeParameters, serviceTypeArgMap));

            result.Add(closedServiceType);
        }

        return result;
    }

    /// <summary>
    /// Substitutes type parameters in TypeParameter array with actual types.
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
            var newTypeName = SubstituteTypeArguments(param.Type.Name, typeArgMap);
            var newType = param.Type with { Name = newTypeName, IsOpenGeneric = false };
            result.Add(param with { Type = newType });
        }

        return result.ToImmutableEquatableArray();
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

        var serviceTypeParams = serviceType.TypeParameters;
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
    /// Substitutes type parameters in a decorator and processes its constructor parameters.
    /// </summary>
    private static TypeData SubstituteDecoratorTypeParams(
        TypeData decorator,
        ImmutableEquatableArray<TypeParameter> closedTypeParams)
    {
        if(!decorator.IsOpenGeneric)
        {
            // No longer need to process decorator parameters for IsServiceParameter
            return decorator;
        }

        // Build type argument map from decorator type params to closed type args
        var decoratorTypeParams = decorator.TypeParameters;
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
        var closedDecoratorNameWithoutGeneric = decorator.NameWithoutGeneric;

        // Substitute in constructor parameters
        var constructorParams = decorator.ConstructorParameters;
        ImmutableEquatableArray<ParameterData>? closedConstructorParams = null;
        if(constructorParams is not null && constructorParams.Length > 0)
        {
            var newParams = new List<ParameterData>(constructorParams.Length);
            foreach(var param in constructorParams)
            {
                var newParamTypeName = SubstituteTypeArguments(param.Type.Name, typeArgMap);
                var newParamType = param.Type with
                {
                    Name = newParamTypeName,
                    IsOpenGeneric = false
                };
                newParams.Add(param with { Type = newParamType });
            }
            closedConstructorParams = newParams.ToImmutableEquatableArray();
        }

        // Substitute in type parameters
        var newTypeParams = SubstituteTypeParameters(decoratorTypeParams, typeArgMap);

        return new TypeData(
            closedDecoratorName,
            closedDecoratorNameWithoutGeneric,
            IsOpenGeneric: false,
            decorator.GenericArity,
            IsNestedOpenGeneric: false,
            IsTypeParameter: false, // Closed decorator types are not type parameters
            IsNonEnumerableCollection: false, // Closed decorator types are not collection types
            newTypeParams,
            closedConstructorParams);
    }
}
