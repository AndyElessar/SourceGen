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
        var pendingDependencies = new Queue<PendingDependency>();
        if(closedGenericDependencies is not null)
        {
            foreach(var dependency in closedGenericDependencies.Values)
            {
                pendingDependencies.Enqueue(new(dependency, Source: null));
            }
        }
        var processedDependencies = new HashSet<string>(StringComparer.Ordinal);

        // No dependencies to process - nothing to do
        if(pendingDependencies.Count == 0)
        {
            return;
        }

        var expandingDependencies = FindExpandingDependencies(openGenericIndex, closedGenericDependencies?.Values ?? Enumerable.Empty<ClosedGenericDependency>(), ct);

        while(pendingDependencies.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var (dependency, source) = pendingDependencies.Dequeue();
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

            // A dependency collected only from expanding dependencies, such as INode<List<string>> of Node<string>, would
            // start closings that never end. It is rejected for every implementation, so that the one closed for it does
            // not depend on whether another registration collects it first.
            var isExpanding = IsExpandingDependency(expandingDependencies, source, dependency);

            // Track registration count before generating
            var registrationCountBefore = registrations.Count;

            // Try each open generic registration to find one that matches the closed type structure
            ClosingSource? closingSource = null;
            OpenGenericRegistrationInfo? closedRegistration = null;
            foreach(var openGenericInfo in isExpanding ? [] : openGenericInfoList)
            {
                // Generate the closed generic factory registration
                // Returns true if successful, allowing us to break early
                if(TryGenerateClosedGenericRegistration(
                    dependency.ClosedType,
                    openGenericInfo,
                    registrations,
                    generatedClosedGenerics,
                    out closingSource))
                {
                    closedRegistration = openGenericInfo;
                    break; // Found a matching registration, stop searching
                }
            }

            // A closing rejected for this registration, such as an expanding one, may succeed for another, so the
            // dependency is processed again when another registration collects it. Each registration collects it once.
            if(registrations.Count == registrationCountBefore)
            {
                processedDependencies.Remove(closedTypeName);
            }

            // Collect new dependencies from newly generated registrations (including decorator parameters)
            for(var i = registrationCountBefore; i < registrations.Count; i++)
            {
                var newReg = registrations[i].Registration;

                // An instance is neither constructed nor decorated, so it has no dependencies
                if(newReg.Instance is not null)
                {
                    continue;
                }

                // Collect from decorator constructor parameters
                foreach(var decorator in newReg.Decorators)
                {
                    // Decorator dependencies are written with the decorator's type parameters
                    var decoratorSource = GetDecoratorClosingSource(expandingDependencies, closedRegistration, decorator);

                    // Skip the parameter that receives the decorated instance
                    foreach(var param in EnumerateDecoratorDependencyParameters(decorator, newReg.ServiceType))
                    {
                        CollectNewDependencyFromType(
                            param.Type,
                            pendingDependencies,
                            processedDependencies,
                            generatedClosedGenerics,
                            decoratorSource);
                    }

                    // Collect from decorator injection members (properties, fields, methods with [IocInject] attribute)
                    CollectNewDependenciesFromInjectionMembers(
                        decorator.InjectionMembers,
                        pendingDependencies,
                        processedDependencies,
                        generatedClosedGenerics,
                        decoratorSource);
                }

                // A factory creates the instance instead of the constructor, so only its parameters are dependencies; a
                // generic factory's are written with its own type parameters
                if(newReg.Factory is { } factory)
                {
                    // The source is named after the open factory, whose parameters use the implementation's type parameters
                    var factorySource = closedRegistration is null
                        ? closingSource
                        : GetFactoryClosingSource(closedRegistration.Factory ?? factory, closedRegistration, newReg.ServiceType, closingSource?.TypeArgumentMap);
                    foreach(var param in factory.AdditionalParameters)
                    {
                        CollectNewDependencyFromType(
                            param.Type,
                            pendingDependencies,
                            processedDependencies,
                            generatedClosedGenerics,
                            factorySource);
                    }

                    continue;
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
                            generatedClosedGenerics,
                            closingSource);
                    }
                }

                // Collect from the implementation's injection members
                CollectNewDependenciesFromInjectionMembers(
                    newReg.InjectionMembers,
                    pendingDependencies,
                    processedDependencies,
                    generatedClosedGenerics,
                    closingSource);
            }
        }
    }

    /// <summary>
    /// Collects new closed generic dependencies from injected property and field types and injected method parameters.
    /// </summary>
    private static void CollectNewDependenciesFromInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData>? injectionMembers,
        Queue<PendingDependency> pendingDependencies,
        HashSet<string> processedDependencies,
        HashSet<string> generatedClosedGenerics,
        ClosingSource? source)
    {
        foreach(var member in injectionMembers ?? [])
        {
            switch(member)
            {
                case PropertyOrFieldInjectionData propertyOrField:
                    CollectNewDependencyFromType(propertyOrField.Type, pendingDependencies, processedDependencies, generatedClosedGenerics, source);
                    break;

                case MethodInjectionData method:
                    foreach(var param in method.Parameters)
                    {
                        CollectNewDependencyFromType(param.Type, pendingDependencies, processedDependencies, generatedClosedGenerics, source);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Collects a new closed generic dependency from a type and adds it to the pending queue.
    /// </summary>
    private static void CollectNewDependencyFromType(
        TypeData paramType,
        Queue<PendingDependency> pendingDependencies,
        HashSet<string> processedDependencies,
        HashSet<string> generatedClosedGenerics,
        ClosingSource? source)
    {
        // Unwrap wrappers like direct dependencies do, such as IRepo<string> in Lazy<IRepo<string>> or IEnumerable<IRepo<string>>
        if(GetWrapperInnerType(paramType) is { } innerType)
        {
            CollectNewDependencyFromType(innerType, pendingDependencies, processedDependencies, generatedClosedGenerics, source);
        }

        if(GetEnumerableElementType(paramType) is { } elementType)
        {
            CollectNewDependencyFromType(elementType, pendingDependencies, processedDependencies, generatedClosedGenerics, source);
        }

        if(paramType is not GenericTypeData genericParamType || paramType.IsArrayType)
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
        pendingDependencies.Enqueue(new(dependency, source));
    }

    /// <summary>A closed generic dependency waiting to be closed, and the closed generic registration it was collected from.</summary>
    private readonly record struct PendingDependency(ClosedGenericDependency Dependency, ClosingSource? Source);

    /// <summary>
    /// Tries to generate a closed generic registration based on an open generic registration.
    /// The closedServiceType is the closed service type from the dependency (e.g., IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;).
    /// Returns true if registration was successfully generated, false if type structure is incompatible.
    /// </summary>
    /// <param name="closingSource">The closing the dependencies of the generated registrations are collected from.</param>
    private static bool TryGenerateClosedGenericRegistration(
        TypeData closedServiceType,
        OpenGenericRegistrationInfo openGenericInfo,
        List<ServiceRegistrationWithTags> registrations,
        HashSet<string> generatedClosedGenerics,
        out ClosingSource? closingSource)
    {
        closingSource = null;
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
        Dictionary<string, TypeData>? typeArguments = null;
        if(implementedForm is not null && !isFactoryOnlyRegistration)
        {
            typeArguments = MatchAllTypeArguments(openImplType, implementedForm, closedServiceGenericType);
            if(typeArguments is null)
            {
                return false; // The implementation cannot produce this closed service type
            }
            serviceTypeArgMap = ToTypeArgMap(typeArguments);

            // Closing with type arguments that violate the implementation's constraints would not compile (CS0453 etc.)
            if(!SatisfiesConstraints(openImplType.TypeParameters ?? [], typeArguments, serviceTypeArgMap))
            {
                return false;
            }
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
            // The registered type's type parameters may be named differently from those of the matched service type, such as
            // Base<U> of Node<T> : Base<T>, so the form it declares (Base<T>, or the type itself) is matched instead; a target of
            // default settings is its own service type
            var registeredTypeArguments = implementedForm is not null
                ? MatchAllTypeArguments(openImplType, implementedForm, closedServiceGenericType)
                : null;
            var factoryOnlyTypeArgMap = registeredTypeArguments is { Count: > 0 } ? ToTypeArgMap(registeredTypeArguments) : serviceTypeArgMap;

            // A type parameter the service type does not determine, such as V of Node<T, V> : Base<T>, cannot be closed, and,
            // like a closed generic registration, type arguments that violate the constraints would not compile (CS0453 etc.)
            if(!DeterminesAllTypeParameters(openImplType, factoryOnlyTypeArgMap)
                || (registeredTypeArguments is { Count: > 0 } && !SatisfiesConstraints(openImplType.TypeParameters ?? [], registeredTypeArguments, factoryOnlyTypeArgMap)))
            {
                return false;
            }

            closingSource = new ClosingSource(openImplType.Name, factoryOnlyTypeArgMap);
            return TryGenerateFactoryOnlyRegistration(
                closedServiceType,
                openGenericInfo,
                factoryOnlyTypeArgMap,
                registrations,
                generatedClosedGenerics);
        }

        // Build mapping from open implementation type params to closed type args
        // This requires mapping through the service type params
        var implTypeArgMap = BuildImplTypeArgMapFromServiceTypeMap(
            openImplType,
            matchingOpenServiceType,
            serviceTypeArgMap);
        // A type parameter the service type does not determine, such as V of Node<T, V> : Base<T>, cannot be closed: the
        // closed type, its Factory, Instance and Key would keep it (CS0246)
        if(implTypeArgMap is not { Count: > 0 } || !DeterminesAllTypeParameters(openImplType, implTypeArgMap))
        {
            return false;
        }

        // Like a registration, an implementation type that cannot be constructed without a Factory or Instance is not
        // registered (SGIOC001), such as an open entry of an imported module without an accessible constructor
        if(openGenericInfo is { Factory: null, Instance: null } && openImplType.Constructibility is Constructibility.NotConstructible)
        {
            return false;
        }

        // Build closed implementation type name
        var closedImplTypeName = SubstituteTypeArguments(openImplType.Name, implTypeArgMap);

        // A generic factory cannot create the closed service type when its mapping does not match it, so another registration may
        if(openGenericInfo.Factory is { TypeParameterCount: > 0 } genericFactory
            && closedServiceType.Name != closedImplTypeName
            && MapGenericFactoryTypeArguments(genericFactory, closedServiceType) is null)
        {
            return false;
        }

        // Build closed implementation type with substituted constructor parameters
        var closedImplType = BuildClosedImplTypeData(
            closedImplTypeName,
            openImplType,
            implTypeArgMap,
            typeArguments);

        // Build all closed service types from the open generic's service types
        var closedServiceTypes = BuildClosedServiceTypesFromServiceTypeMap(
            openGenericInfo.ServiceTypes,
            openImplType,
            openGenericInfo.AllInterfaces,
            serviceTypeArgMap,
            implTypeArgMap,
            typeArguments);

        // A factory whose path or signature uses the implementation's type parameters, such as Node<T>.Create(IRepo<T>), is
        // called on the closed type
        var implClosedFactory = openGenericInfo.Factory is { } factory ? CloseImplementationTypeParameters(factory, implTypeArgMap, typeArguments) : null;

        // A generic factory is called for the registered types its mapping matches, so the implementation type is constructed
        // unless the mapping matches it; a regular factory creates both the implementation and the service types
        var implFactory = implClosedFactory is not null ? CloseGenericFactory(implClosedFactory, closedImplType) : null;

        // An implementation type that cannot be constructed is not registered without a factory for it, such as an open
        // interface target whose generic factory's mapping does not match the closed type; its service types the factory
        // creates still are, like a closed registration's
        var skipImplementation = implFactory is null && openGenericInfo.Factory is not null && openImplType.Constructibility is Constructibility.NotConstructible;
        var registrationCountBefore = registrations.Count;

        // An instance whose path uses the implementation's type parameters, such as Node<T>.Default, is read from the closed type
        var closedInstance = openGenericInfo.Instance is { } instance ? SubstituteTypeArguments(instance, implTypeArgMap) : null;

        // So is a key, such as Node<T>.Name; it is a C# expression, so only the identifiers that can be type parameters are closed
        var closedKey = SubstituteTypeArgumentsInExpression(openGenericInfo.Key, implTypeArgMap);

        // Injection members are written with the implementation's type parameters, such as IRepo<T> for Outer<T>
        var closedInjectionMembers = SubstituteInjectionMembers(openGenericInfo.InjectionMembers, implTypeArgMap, typeArguments);

        // Create registration for the implementation type itself
        var implModel = new ServiceRegistrationModel(
            closedImplType,
            closedImplType,
            openGenericInfo.Lifetime,
            closedKey,
            openGenericInfo.KeyType,
            openGenericInfo.KeyValueType,
            IsOpenGeneric: false,
            [],
            closedInjectionMembers,
            Factory: implFactory,
            closedInstance);

        // Add registration with tags
        if(!skipImplementation)
        {
            registrations.Add(new ServiceRegistrationWithTags(implModel, openGenericInfo.Tags));
            generatedClosedGenerics.Add(closedImplType.Name);
        }

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

            // A generic factory cannot be called for a service type its mapping does not match, which is not registered
            var closedSvcTypeWithArguments = WithTypeArgumentsOf(closedSvcType, closedServiceGenericType);
            FactoryMethodData? serviceFactory = null;
            if(implClosedFactory is not null && (serviceFactory = CloseGenericFactory(implClosedFactory, closedSvcTypeWithArguments)) is null)
            {
                continue;
            }

            // Process decorators for this specific service type
            // Use service type's type parameters for decorator substitution
            var serviceTypeDecorators = ProcessDecoratorsForServiceType(
                openGenericInfo.Decorators,
                closedSvcTypeWithArguments);

            var serviceModel = new ServiceRegistrationModel(
                closedSvcType,
                closedImplType,
                openGenericInfo.Lifetime,
                closedKey,
                openGenericInfo.KeyType,
                openGenericInfo.KeyValueType,
                IsOpenGeneric: false,
                serviceTypeDecorators,
                closedInjectionMembers,
                serviceFactory,
                closedInstance);

            registrations.Add(new ServiceRegistrationWithTags(serviceModel, openGenericInfo.Tags));
            generatedClosedGenerics.Add(closedSvcType.Name);
        }

        if(registrations.Count == registrationCountBefore)
        {
            return false;
        }

        closingSource = new ClosingSource(openImplType.Name, implTypeArgMap);
        return true; // Successfully generated registration
    }

    /// <summary>Gets whether a type argument map gives a type argument to every type parameter of an open generic type.</summary>
    private static bool DeterminesAllTypeParameters(GenericTypeData openType, Dictionary<string, string> typeArgMap)
    {
        foreach(var typeParam in openType.TypeParameters ?? [])
        {
            if(!typeArgMap.ContainsKey(typeParam.ParameterName))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generates a factory-only closed generic registration.
    /// This is used when IocRegisterDefaults has a Factory but no explicit ImplementationType.
    /// Only generates service type registration using the factory method.
    /// </summary>
    /// <param name="typeArgMap">
    /// The type argument name of each type parameter of the registered type, such as <c>Node&lt;T&gt;</c> registered with a
    /// factory and without interfaces, which its Factory and Key are closed with.
    /// </param>
    private static bool TryGenerateFactoryOnlyRegistration(
        TypeData closedServiceType,
        OpenGenericRegistrationInfo openGenericInfo,
        Dictionary<string, string> typeArgMap,
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

        // A generic factory cannot be called for a closed type its mapping does not match, so another registration may close it
        // A factory whose path or signature uses the registered type's type parameters, such as Node<T>.Create(IRepo<T>), is
        // called on the closed type
        if(openGenericInfo.Factory is not { } openFactory
            || CloseGenericFactory(CloseImplementationTypeParameters(openFactory, typeArgMap, typeArguments: null), closedServiceType) is not { } factory)
        {
            return false;
        }

        // Process decorators for this specific service type
        var serviceTypeDecorators = ProcessDecoratorsForServiceType(
            openGenericInfo.Decorators,
            closedServiceType);

        // For factory-only registration, both ServiceType and ImplementationType are the same (the service type)
        // The factory method will create the actual instance, so the type is never constructed and its Constructibility
        // stays Unknown: it comes from a dependency, which is extracted without it
        var serviceModel = new ServiceRegistrationModel(
            closedServiceType,
            closedServiceType, // Use service type as implementation type since factory creates the instance
            openGenericInfo.Lifetime,
            SubstituteTypeArgumentsInExpression(openGenericInfo.Key, typeArgMap),
            openGenericInfo.KeyType,
            openGenericInfo.KeyValueType,
            IsOpenGeneric: false,
            serviceTypeDecorators,
            openGenericInfo.InjectionMembers,
            factory,
            openGenericInfo.Instance);

        registrations.Add(new ServiceRegistrationWithTags(serviceModel, openGenericInfo.Tags));
        generatedClosedGenerics.Add(closedServiceType.Name);

        return true; // Successfully generated registration
    }
}
