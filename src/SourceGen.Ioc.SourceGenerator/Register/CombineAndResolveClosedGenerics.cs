namespace SourceGen.Ioc.SourceGenerator.Register;

partial class RegisterSourceGenerator
{
    /// <summary>
    /// Pipeline 2: Combines all basic registration results and resolves closed generic dependencies.
    /// This method re-runs when any BasicRegistrationResult changes.
    /// </summary>
    /// <param name="basicResults">The basic registration results from pipeline 1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Service registrations grouped by method name.</returns>
    private static ImmutableEquatableDictionary<string, ImmutableEquatableArray<ServiceRegistrationModel>> CombineAndResolveClosedGenerics(
        in ImmutableArray<BasicRegistrationResult> basicResults,
        CancellationToken ct)
    {
        var methodGroups = new Dictionary<string, List<ServiceRegistrationModel>>(StringComparer.Ordinal);

        // Build open generic index from all results
        var openGenericIndex = new Dictionary<string, OpenGenericRegistrationInfo>(StringComparer.Ordinal);

        // Collect all closed generic dependencies
        var closedGenericDependencies = new Dictionary<string, ClosedGenericDependency>(StringComparer.Ordinal);

        // Process each basic result
        foreach(var result in basicResults)
        {
            ct.ThrowIfCancellationRequested();

            // Add service registrations to method groups
            foreach(var model in result.ServiceRegistrations)
            {
                if(!result.ExcludeFromDefault)
                {
                    AddToMethodGroup(methodGroups, DefaultMethodKey, model);
                }

                foreach(var tag in result.Tags)
                {
                    AddToMethodGroup(methodGroups, tag, model);
                }
            }

            // Index open generic entries
            foreach(var entry in result.OpenGenericEntries)
            {
                if(!openGenericIndex.ContainsKey(entry.ServiceTypeKey))
                {
                    openGenericIndex[entry.ServiceTypeKey] = entry.RegistrationInfo;
                }
            }

            // Collect closed generic dependencies
            foreach(var dep in result.ClosedGenericDependencies)
            {
                if(!closedGenericDependencies.ContainsKey(dep.ClosedTypeName))
                {
                    closedGenericDependencies[dep.ClosedTypeName] = dep;
                }
            }
        }

        // Generate factory registrations for closed generic dependencies
        GenerateClosedGenericFactoryRegistrations(
            openGenericIndex,
            closedGenericDependencies,
            methodGroups,
            ct);

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
        Dictionary<string, OpenGenericRegistrationInfo> openGenericIndex,
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
            if(!openGenericIndex.TryGetValue(dependency.OpenGenericKey, out var openGenericInfo))
            {
                continue;
            }

            // Skip if this closed generic is already registered
            if(generatedClosedGenerics.Contains(closedTypeName))
            {
                continue;
            }

            // Generate the closed generic factory registration
            GenerateClosedGenericRegistration(
                dependency.ClosedType,
                openGenericInfo,
                methodGroups,
                generatedClosedGenerics);
        }
    }

    /// <summary>
    /// Generates a closed generic registration based on an open generic registration.
    /// The closedServiceType is the closed service type from the dependency (e.g., IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;)
    /// </summary>
    private static void GenerateClosedGenericRegistration(
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
            return; // No matching open service type found
        }

        // Build type argument substitution map from service type parameters
        // Map: open service type param name -> closed service type arg name
        var serviceTypeArgMap = BuildTypeArgumentMapFromServiceType(
            matchingOpenServiceType,
            closedServiceType);
        if(serviceTypeArgMap is null || serviceTypeArgMap.Count == 0)
        {
            return; // Cannot build substitution map
        }

        // Build mapping from open implementation type params to closed type args
        // This requires mapping through the service type params
        var implTypeArgMap = BuildImplTypeArgMapFromServiceTypeMap(
            openImplType,
            matchingOpenServiceType,
            serviceTypeArgMap);
        if(implTypeArgMap is null)
        {
            return;
        }

        // Build closed implementation type name
        var closedImplTypeName = SubstituteTypeArguments(openImplType.Name, implTypeArgMap);

        // Build closed implementation type with substituted constructor parameters
        var closedImplType = BuildClosedImplTypeData(
            closedImplTypeName,
            openImplType,
            implTypeArgMap);

        // Build the set of service type names for decorator processing
        var serviceTypeNames = new HashSet<string>(StringComparer.Ordinal);
        AddTypeNameVariants(serviceTypeNames, closedImplType);
        AddTypeNameVariants(serviceTypeNames, closedServiceType);

        // Build all closed service types from the open generic's service types
        var closedServiceTypes = BuildClosedServiceTypesFromServiceTypeMap(
            openGenericInfo.ServiceTypes,
            serviceTypeArgMap,
            implTypeArgMap);
        foreach(var svcType in closedServiceTypes)
        {
            AddTypeNameVariants(serviceTypeNames, svcType);
        }

        // Create registration for the implementation type itself
        var implModel = new ServiceRegistrationModel(
            closedImplType,
            closedImplType,
            openGenericInfo.Lifetime,
            openGenericInfo.Key,
            openGenericInfo.KeyType,
            IsOpenGeneric: false,
            [],
            openGenericInfo.InjectionMembers);

        // Add to method groups
        if(!openGenericInfo.ExcludeFromDefault)
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
                closedSvcType,
                serviceTypeNames);

            var serviceModel = new ServiceRegistrationModel(
                closedSvcType,
                closedImplType,
                openGenericInfo.Lifetime,
                openGenericInfo.Key,
                openGenericInfo.KeyType,
                IsOpenGeneric: false,
                serviceTypeDecorators,
                openGenericInfo.InjectionMembers);

            if(!openGenericInfo.ExcludeFromDefault)
            {
                AddToMethodGroup(methodGroups, DefaultMethodKey, serviceModel);
            }
            foreach(var tag in openGenericInfo.Tags)
            {
                AddToMethodGroup(methodGroups, tag, serviceModel);
            }
            generatedClosedGenerics.Add(closedSvcType.Name);
        }
    }

    /// <summary>
    /// Builds a type argument map from open service type to closed service type.
    /// For nested open generics like IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt;,
    /// this extracts the actual type parameters (T -> Entity) by comparing
    /// the nested type arguments.
    /// </summary>
    private static Dictionary<string, string>? BuildTypeArgumentMapFromServiceType(
        TypeData openServiceType,
        TypeData closedServiceType)
    {
        var openTypeParams = openServiceType.TypeParameters;
        var closedTypeParams = closedServiceType.TypeParameters;

        if(openTypeParams is null || closedTypeParams is null)
        {
            return null;
        }

        if(openTypeParams.Length != closedTypeParams.Length)
        {
            return null;
        }

        var typeArgMap = new Dictionary<string, string>(StringComparer.Ordinal);

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
        ExtractTypeArgumentMappings(openTypeParams, closedTypeParams, typeArgMap);

        return typeArgMap.Count > 0 ? typeArgMap : null;
    }

    /// <summary>
    /// Extracts type argument mappings from nested open generic types.
    /// For example, for IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt; closed with
    /// IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;,
    /// this extracts T -> Entity.
    /// </summary>
    private static void ExtractTypeArgumentMappings(
        ImmutableEquatableArray<TypeParameter> openParams,
        ImmutableEquatableArray<TypeParameter> closedParams,
        Dictionary<string, string> typeArgMap)
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
            // Since TypeParameters may be null for nested types (CreateBasicTypeData doesn't recursively extract),
            // we extract mappings by parsing the type names
            ExtractTypeArgumentMappingsFromNames(openParamType.Name, closedParamType.Name, typeArgMap);
        }
    }

    /// <summary>
    /// Extracts type argument mappings by parsing and comparing type names.
    /// For example, comparing "GenericRequest&lt;T&gt;" with "GenericRequest&lt;Entity&gt;"
    /// extracts T -> Entity.
    /// </summary>
    private static void ExtractTypeArgumentMappingsFromNames(
        string openTypeName,
        string closedTypeName,
        Dictionary<string, string> typeArgMap)
    {
        // Parse the type arguments from both names
        var openTypeArgs = ParseTypeArguments(openTypeName);
        var closedTypeArgs = ParseTypeArguments(closedTypeName);

        if(openTypeArgs.Count != closedTypeArgs.Count)
        {
            return;
        }

        for(int i = 0; i < openTypeArgs.Count; i++)
        {
            var openArg = openTypeArgs[i];
            var closedArg = closedTypeArgs[i];

            // Check if openArg is a simple type parameter (single identifier)
            if(IsSimpleTypeParameter(openArg))
            {
                // Direct mapping: T -> Entity
                typeArgMap[openArg] = closedArg;
            }
            else if(openArg.Contains('<'))
            {
                // Nested generic type, recurse
                ExtractTypeArgumentMappingsFromNames(openArg, closedArg, typeArgMap);
            }
        }
    }

    /// <summary>
    /// Parses type arguments from a generic type name.
    /// For example, "IHandler&lt;Request&lt;T&gt;, List&lt;T&gt;&gt;" returns ["Request&lt;T&gt;", "List&lt;T&gt;"].
    /// </summary>
    private static List<string> ParseTypeArguments(string typeName)
    {
        var result = new List<string>();

        int genericStart = typeName.IndexOf('<');
        if(genericStart < 0)
        {
            return result;
        }

        // Find the matching closing bracket
        int depth = 0;
        int argStart = genericStart + 1;

        for(int i = genericStart; i < typeName.Length; i++)
        {
            char c = typeName[i];
            if(c == '<')
            {
                depth++;
            }
            else if(c == '>')
            {
                depth--;
                if(depth == 0)
                {
                    // End of type arguments
                    var arg = typeName.Substring(argStart, i - argStart).Trim();
                    if(!string.IsNullOrEmpty(arg))
                    {
                        result.Add(arg);
                    }
                    break;
                }
            }
            else if(c == ',' && depth == 1)
            {
                // Argument separator at the top level
                var arg = typeName.Substring(argStart, i - argStart).Trim();
                if(!string.IsNullOrEmpty(arg))
                {
                    result.Add(arg);
                }
                argStart = i + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Determines if a type name is a simple type parameter (single identifier like "T").
    /// </summary>
    private static bool IsSimpleTypeParameter(string typeName)
    {
        // A simple type parameter is a single identifier (no dots, no generics, no global::)
        // It should be a short name like "T", "TRequest", etc.
        if(string.IsNullOrEmpty(typeName) || typeName.Contains('<') || typeName.Contains('.'))
        {
            return false;
        }

        // Check if it's a valid identifier (starts with letter or underscore, followed by letters/digits/underscores)
        if(!char.IsLetter(typeName[0]) && typeName[0] != '_')
        {
            return false;
        }

        for(int i = 1; i < typeName.Length; i++)
        {
            if(!char.IsLetterOrDigit(typeName[i]) && typeName[i] != '_')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds implementation type argument map by mapping service type params to implementation type params.
    /// For nested open generics, this uses the serviceTypeArgMap which already contains
    /// the extracted type parameter mappings (e.g., T -> Entity).
    /// </summary>
    private static Dictionary<string, string>? BuildImplTypeArgMapFromServiceTypeMap(
        TypeData openImplType,
        TypeData openServiceType,
        Dictionary<string, string> serviceTypeArgMap)
    {
        var implTypeParams = openImplType.TypeParameters;

        if(implTypeParams is null)
        {
            return null;
        }

        // For nested open generics, the serviceTypeArgMap already contains
        // the direct type parameter mappings (e.g., T -> Entity)
        // These should match the implementation type parameters directly
        var implTypeArgMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach(var implParam in implTypeParams)
        {
            // Check if the serviceTypeArgMap contains a mapping for this impl type param
            if(serviceTypeArgMap.TryGetValue(implParam.ParameterName, out var closedTypeArg))
            {
                implTypeArgMap[implParam.ParameterName] = closedTypeArg;
            }
        }

        return implTypeArgMap.Count > 0 ? implTypeArgMap : null;
    }

    /// <summary>
    /// Builds closed implementation TypeData with substituted constructor parameters.
    /// </summary>
    private static TypeData BuildClosedImplTypeData(
        string closedImplTypeName,
        TypeData openImplType,
        Dictionary<string, string> typeArgMap)
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
        ImmutableEquatableArray<ConstructorParameterData>? closedConstructorParams = null;
        if(openConstructorParams is not null && openConstructorParams.Length > 0)
        {
            var newParams = new List<ConstructorParameterData>(openConstructorParams.Length);
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
            closedTypeParams,
            closedConstructorParams);
    }

    /// <summary>
    /// Builds all closed service types from open service types using the service type argument map.
    /// </summary>
    private static List<TypeData> BuildClosedServiceTypesFromServiceTypeMap(
        ImmutableEquatableArray<TypeData> openServiceTypes,
        Dictionary<string, string> serviceTypeArgMap,
        Dictionary<string, string> implTypeArgMap)
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
                SubstituteTypeParameters(openServiceType.TypeParameters, serviceTypeArgMap));

            result.Add(closedServiceType);
        }

        return result;
    }

    /// <summary>
    /// Substitutes type parameters in a type name with actual type arguments.
    /// </summary>
    private static string SubstituteTypeArguments(string typeName, Dictionary<string, string> typeArgMap)
    {
        var result = typeName;
        foreach(var kvp in typeArgMap)
        {
            result = ReplaceTypeParameter(result, kvp.Key, kvp.Value);
        }
        return result;
    }

    /// <summary>
    /// Substitutes type parameters in TypeParameter array with actual types.
    /// </summary>
    private static ImmutableEquatableArray<TypeParameter>? SubstituteTypeParameters(
        ImmutableEquatableArray<TypeParameter>? typeParams,
        Dictionary<string, string> typeArgMap)
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
        TypeData serviceType,
        HashSet<string> serviceTypeNames)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        var serviceTypeParams = serviceType.TypeParameters;
        if(serviceTypeParams is null || serviceTypeParams.Length == 0)
        {
            return ProcessDecorators(decorators, serviceTypeNames);
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
            var processedDecorator = SubstituteDecoratorTypeParams(decorator, serviceTypeParams, serviceTypeNames);
            processedDecorators.Add(processedDecorator);
        }

        return processedDecorators.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Processes decorators for a closed generic type, substituting type parameters.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> ProcessDecoratorsForClosedGeneric(
        ImmutableEquatableArray<TypeData> decorators,
        TypeData closedType,
        HashSet<string> serviceTypeNames)
    {
        if(decorators.Length == 0)
        {
            return decorators;
        }

        var typeParams = closedType.TypeParameters;
        if(typeParams is null || typeParams.Length == 0)
        {
            return ProcessDecorators(decorators, serviceTypeNames);
        }

        // Filter decorators based on type constraints
        var filteredDecorators = FilterDecoratorsForClosedType(decorators, typeParams);
        if(filteredDecorators.Length == 0)
        {
            return [];
        }

        // Process and substitute type parameters in decorators
        var processedDecorators = new List<TypeData>(filteredDecorators.Length);
        foreach(var decorator in filteredDecorators)
        {
            var processedDecorator = SubstituteDecoratorTypeParams(decorator, typeParams, serviceTypeNames);
            processedDecorators.Add(processedDecorator);
        }

        return processedDecorators.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Filters decorators based on type parameter constraints for a closed type.
    /// </summary>
    private static ImmutableEquatableArray<TypeData> FilterDecoratorsForClosedType(
        ImmutableEquatableArray<TypeData> decorators,
        ImmutableEquatableArray<TypeParameter> closedTypeParams)
    {
        // Build interface name set for constraint checking
        var interfaceNameSet = BuildInterfaceNameSet(closedTypeParams);

        return FilterDecoratorsCore(decorators, closedTypeParams, interfaceNameSet);
    }

    /// <summary>
    /// Substitutes type parameters in a decorator and processes its constructor parameters.
    /// </summary>
    private static TypeData SubstituteDecoratorTypeParams(
        TypeData decorator,
        ImmutableEquatableArray<TypeParameter> closedTypeParams,
        HashSet<string> serviceTypeNames)
    {
        if(!decorator.IsOpenGeneric)
        {
            return ProcessDecoratorParameters(decorator, serviceTypeNames);
        }

        // Build type argument map from decorator type params to closed type args
        var decoratorTypeParams = decorator.TypeParameters;
        if(decoratorTypeParams is null || decoratorTypeParams.Length == 0)
        {
            return ProcessDecoratorParameters(decorator, serviceTypeNames);
        }

        // Build substitution map: decorator param name -> closed type arg name
        var typeArgMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for(int i = 0; i < decoratorTypeParams.Length && i < closedTypeParams.Length; i++)
        {
            typeArgMap[decoratorTypeParams[i].ParameterName] = closedTypeParams[i].Type.Name;
        }

        // Substitute in decorator type name
        var closedDecoratorName = SubstituteTypeArguments(decorator.Name, typeArgMap);
        var closedDecoratorNameWithoutGeneric = decorator.NameWithoutGeneric;

        // Substitute in constructor parameters
        var constructorParams = decorator.ConstructorParameters;
        ImmutableEquatableArray<ConstructorParameterData>? closedConstructorParams = null;
        if(constructorParams is not null && constructorParams.Length > 0)
        {
            var newParams = new List<ConstructorParameterData>(constructorParams.Length);
            foreach(var param in constructorParams)
            {
                var newParamTypeName = SubstituteTypeArguments(param.Type.Name, typeArgMap);
                var newParamType = param.Type with
                {
                    Name = newParamTypeName,
                    IsOpenGeneric = false
                };
                var isServiceParam = IsServiceTypeParameter(newParamType, serviceTypeNames);
                newParams.Add(param with { Type = newParamType, IsServiceParameter = isServiceParam });
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
            newTypeParams,
            closedConstructorParams);
    }
}
