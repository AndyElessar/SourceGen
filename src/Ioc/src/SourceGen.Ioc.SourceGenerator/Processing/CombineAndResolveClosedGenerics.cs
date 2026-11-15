namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ImmutableEquatableArray<ClosedGenericDependency> CollectUniqueClosedGenericDependencies(
        ImmutableArray<ClosedGenericDependency> invocations,
        ImmutableArray<ClosedGenericDependency> discoveries,
        CancellationToken ct)
    {
        var seen = new HashSet<(string TypeName, string? Key, bool AnyKey)>();
        var dependencies = new List<ClosedGenericDependency>();
        Add(invocations);
        Add(discoveries);
        return dependencies.ToImmutableEquatableArray();

        void Add(ImmutableArray<ClosedGenericDependency> candidates)
        {
            foreach(var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if(seen.Add(candidate.Identity))
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
        Dictionary<(string TypeName, string? Key, bool AnyKey), ClosedGenericDependency>? closedGenericDependencies = null;

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
                closedGenericDependencies ??= new(result.ClosedGenericDependencies.Length);
                foreach(var dep in result.ClosedGenericDependencies)
                {
                    if(!closedGenericDependencies.ContainsKey(dep.Identity))
                    {
                        closedGenericDependencies[dep.Identity] = dep;
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
            closedGenericDependencies ??= new(serviceProviderInvocations.Length);
            foreach(var dep in serviceProviderInvocations)
            {
                if(!closedGenericDependencies.ContainsKey(dep.Identity))
                {
                    closedGenericDependencies[dep.Identity] = dep;
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
        Dictionary<(string TypeName, string? Key, bool AnyKey), ClosedGenericDependency>? closedGenericDependencies,
        List<ServiceRegistrationWithTags> registrations,
        CancellationToken ct)
    {
        // No open generics to close - nothing to do
        if(openGenericIndex.Count == 0)
        {
            return;
        }

        // Track already generated closed generic registrations, by type as written and key, to avoid duplicates. A
        // registration only blocks closing for its own key: another key resolves another service.
        var generatedClosedGenerics = new HashSet<(string TypeName, string? Key)>();

        // Types registered as written are not closed again for the same key in any spelling of their tuple element names
        var registeredTypes = new HashSet<ServiceTypeKey>();

        // Each open generic registration is closed once for each runtime type and set of decorators. Dependencies that
        // differ only in tuple element names are still processed for each spelling: a constraint may be satisfied by one
        // spelling only (see SatisfiesConstraints), so each spelling selects the first open generic registration it
        // matches, as for different runtime types, which closes it unless it is already closed for that runtime type
        // with the same decorators (decorator constraints compare tuple element names too).
        var closedRegistrations = new HashSet<(OpenGenericRegistrationInfo Registration, string ClosingKey)>(ClosedRegistrationComparer.Instance);

        // First, collect all existing registrations to avoid duplicates
        // This includes both implementation types and service types
        foreach(var regWithTags in registrations)
        {
            var model = regWithTags.Registration;
            generatedClosedGenerics.Add((model.ImplementationType.Name, model.Key));
            generatedClosedGenerics.Add((model.ServiceType.Name, model.Key));
            registeredTypes.Add(new(model.ImplementationType.Name, model.Key));
            registeredTypes.Add(new(model.ServiceType.Name, model.Key));
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
        var processedDependencies = new HashSet<(string TypeName, string? Key, bool AnyKey)>();

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
            if(!processedDependencies.Add(dependency.Identity))
            {
                continue;
            }

            // Check if the open generic version is registered
            if(!openGenericIndex.TryGetValue(dependency.OpenGenericKey, out var openGenericInfoList))
            {
                continue;
            }

            // Skip if this closed generic is already registered with the dependency's key; a dependency with any key is
            // checked against the key of the registration it selects
            if(!dependency.AnyKey && IsRegisteredWithKey(closedTypeName, dependency.Key, generatedClosedGenerics, registeredTypes))
            {
                continue;
            }

            // A dependency collected only from expanding dependencies, such as INode<List<string>> of Node<string>, would
            // start closings that never end. It is rejected for every implementation, so that the one closed for it does
            // not depend on whether another registration collects it first.
            var isExpanding = IsExpandingDependency(expandingDependencies, source, dependency);

            // Track registration count before generating
            var registrationCountBefore = registrations.Count;

            // Try each open generic registration to find one that matches the closed type structure and the dependency's key.
            // Keys are compared as written, so a keyed dependency that no registration matches with its key, such as one
            // resolved by a key given as C# code (Key = nameof(Node<T>.Name)), then selects the first keyed registration.
            ClosingSource? closingSource = null;
            OpenGenericRegistrationInfo? closedRegistration = null;
            var anyKeyedRegistration = false;
            do
            {
                foreach(var openGenericInfo in isExpanding ? [] : openGenericInfoList)
                {
                    // Generate the closed generic factory registration
                    // Returns true if successful, allowing us to break early
                    if(TryGenerateClosedGenericRegistration(
                        dependency,
                        anyKeyedRegistration,
                        openGenericInfo,
                        registrations,
                        generatedClosedGenerics,
                        registeredTypes,
                        closedRegistrations,
                        out closingSource))
                    {
                        closedRegistration = openGenericInfo;
                        break; // Found a matching registration, stop searching
                    }
                }

                anyKeyedRegistration = !anyKeyedRegistration && closedRegistration is null && dependency is { AnyKey: false, Key: not null };
            }
            while(anyKeyedRegistration);

            // A closing rejected for this registration, such as an expanding one, may succeed for another, so the
            // dependency is processed again when another registration collects it. Each registration collects it once.
            if(registrations.Count == registrationCountBefore)
            {
                processedDependencies.Remove(dependency.Identity);
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
                            param.ServiceKey,
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
                            param.ServiceKey,
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
                            param.ServiceKey,
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
        HashSet<(string TypeName, string? Key, bool AnyKey)> processedDependencies,
        HashSet<(string TypeName, string? Key)> generatedClosedGenerics,
        ClosingSource? source)
    {
        foreach(var member in injectionMembers ?? [])
        {
            switch(member)
            {
                case PropertyOrFieldInjectionData propertyOrField:
                    CollectNewDependencyFromType(propertyOrField.Type, propertyOrField.Key, pendingDependencies, processedDependencies, generatedClosedGenerics, source);
                    break;

                case MethodInjectionData method:
                    foreach(var param in method.Parameters)
                    {
                        CollectNewDependencyFromType(param.Type, param.ServiceKey ?? method.Key, pendingDependencies, processedDependencies, generatedClosedGenerics, source);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Collects a new closed generic dependency from a type and adds it to the pending queue.
    /// </summary>
    /// <param name="key">
    /// The key the type is resolved with, or <see langword="null"/>; wrappers resolve their inner service with the same key,
    /// except dictionaries and key value pairs, which resolve the keyed registrations of their value type with any key.
    /// </param>
    /// <param name="anyKey">Whether the type is resolved with any key (see <see cref="ClosedGenericDependency.AnyKey"/>).</param>
    private static void CollectNewDependencyFromType(
        TypeData paramType,
        string? key,
        Queue<PendingDependency> pendingDependencies,
        HashSet<(string TypeName, string? Key, bool AnyKey)> processedDependencies,
        HashSet<(string TypeName, string? Key)> generatedClosedGenerics,
        ClosingSource? source,
        bool anyKey = false)
    {
        // Unwrap wrappers like direct dependencies do, such as IRepo<string> in Lazy<IRepo<string>> or IEnumerable<IRepo<string>>
        if(GetWrapperInnerType(paramType) is { } innerType)
        {
            CollectNewDependencyFromType(innerType, key, pendingDependencies, processedDependencies, generatedClosedGenerics, source, anyKey || IsKeyedValueWrapper(paramType));
        }

        if(GetEnumerableElementType(paramType) is { } elementType)
        {
            CollectNewDependencyFromType(elementType, key, pendingDependencies, processedDependencies, generatedClosedGenerics, source, anyKey);
        }

        if(anyKey)
        {
            key = null;
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

        // Skip if already processed or registered with the same key; a dependency with any key is checked against the key of
        // the registration it selects
        if(processedDependencies.Contains((paramType.Name, key, anyKey)) || (!anyKey && generatedClosedGenerics.Contains((paramType.Name, key))))
        {
            return;
        }

        // Add as a new dependency to process
        var dependency = new ClosedGenericDependency(
            paramType.Name,
            paramType,
            genericParamType.NameWithoutGeneric,
            key,
            anyKey);
        pendingDependencies.Enqueue(new(dependency, source));
    }

    /// <summary>A closed generic dependency waiting to be closed, and the closed generic registration it was collected from.</summary>
    private readonly record struct PendingDependency(ClosedGenericDependency Dependency, ClosingSource? Source);

    /// <summary>
    /// Tries to generate a closed generic registration based on an open generic registration.
    /// The closed service type is the type of the dependency (e.g., IRequestHandler&lt;GenericRequest&lt;Entity&gt;, List&lt;Entity&gt;&gt;).
    /// Returns true if registration was successfully generated, or the registration matches a runtime type it is already
    /// closed for with the same decorators, or the closed service type is already registered with the registration's key
    /// (nothing is generated then); false if type structure is incompatible or the registration has another key than the
    /// dependency.
    /// </summary>
    /// <param name="dependency">The closed generic dependency, whose key the closed registration must have.</param>
    /// <param name="anyKeyedRegistration">
    /// Whether a registration with any key resolves the keyed dependency, after no registration matched with its key.
    /// </param>
    /// <param name="registeredTypes">The types registered as written, with their keys, compared as runtime types.</param>
    /// <param name="closedRegistrations">
    /// The open generic registrations and the keys of their closings (see <see cref="GetClosedRegistrationKey"/>).
    /// </param>
    /// <param name="closingSource">The closing the dependencies of the generated registrations are collected from.</param>
    private static bool TryGenerateClosedGenericRegistration(
        in ClosedGenericDependency dependency,
        bool anyKeyedRegistration,
        OpenGenericRegistrationInfo openGenericInfo,
        List<ServiceRegistrationWithTags> registrations,
        HashSet<(string TypeName, string? Key)> generatedClosedGenerics,
        HashSet<ServiceTypeKey> registeredTypes,
        HashSet<(OpenGenericRegistrationInfo Registration, string ClosingKey)> closedRegistrations,
        out ClosingSource? closingSource)
    {
        closingSource = null;
        var closedServiceType = dependency.ClosedType;
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
                dependency,
                anyKeyedRegistration,
                openGenericInfo,
                factoryOnlyTypeArgMap,
                registrations,
                generatedClosedGenerics,
                registeredTypes,
                closedRegistrations);
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

        // The closed registration has the registration's key, such as Node<string>.Name for Node<T>.Name; it is a C#
        // expression, so only the identifiers that can be type parameters are closed. A registration with another key than
        // the dependency's does not resolve it, so another registration may.
        var closedKey = SubstituteTypeArgumentsInExpression(openGenericInfo.Key, implTypeArgMap);
        if(!MatchesDependencyKey(dependency, closedKey, anyKeyedRegistration))
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

        // The selected registration is not closed for a type already registered with its key, such as for a dependency
        // with any key
        if(IsRegisteredWithKey(closedServiceType.Name, closedKey, generatedClosedGenerics, registeredTypes))
        {
            return true;
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

        // The service types that can be registered, with their factories and decorators: a generic factory cannot be called
        // for a service type its mapping does not match, and decorators are closed for each service type
        var closedServices = new List<(TypeData ServiceType, FactoryMethodData? Factory, ImmutableEquatableArray<TypeData> Decorators)>(closedServiceTypes.Count);
        foreach(var closedSvcType in closedServiceTypes)
        {
            // Skip the implementation type and nested open generic service types
            if(closedSvcType.Name == closedImplTypeName
                || closedSvcType is GenericTypeData { IsNestedOpenGeneric: true } or GenericTypeData { IsOpenGeneric: true })
            {
                continue;
            }

            var closedSvcTypeWithArguments = WithTypeArgumentsOf(closedSvcType, closedServiceGenericType);
            FactoryMethodData? serviceFactory = null;
            if(implClosedFactory is not null && (serviceFactory = CloseGenericFactory(implClosedFactory, closedSvcTypeWithArguments)) is null)
            {
                continue;
            }

            // Use service type's type parameters for decorator substitution
            closedServices.Add((closedSvcType, serviceFactory, ProcessDecoratorsForServiceType(openGenericInfo.Decorators, closedSvcTypeWithArguments)));
        }

        // Another spelling of the tuple element names of this runtime type already closed this registration with the
        // same decorators; decorator constraints compare tuple element names, so other decorators close it again
        var closedRegistrationKey = GetClosedRegistrationKey(closedImplTypeName, closedServices);
        if(closedRegistrations.Contains((openGenericInfo, closedRegistrationKey)))
        {
            return true;
        }

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
            generatedClosedGenerics.Add((closedImplType.Name, closedKey));
        }

        // Create registrations for each closed service type
        foreach(var (closedSvcType, serviceFactory, serviceTypeDecorators) in closedServices)
        {
            // Skip if already registered with the same key
            if(IsRegisteredWithKey(closedSvcType.Name, closedKey, generatedClosedGenerics, registeredTypes))
            {
                continue;
            }

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
            generatedClosedGenerics.Add((closedSvcType.Name, closedKey));
        }

        if(registrations.Count == registrationCountBefore)
        {
            return false;
        }

        closedRegistrations.Add((openGenericInfo, closedRegistrationKey));
        closingSource = new ClosingSource(openImplType.Name, implTypeArgMap);
        return true; // Successfully generated registration
    }

    /// <summary>
    /// Gets whether a closed registration with a key resolves a dependency: a dependency with any key is resolved with the key
    /// of the registration it selects, and any other dependency with its own key (or without a key for a non-keyed one),
    /// compared as written, or, when <paramref name="anyKeyedRegistration"/> is set, a keyed dependency with any key.
    /// </summary>
    private static bool MatchesDependencyKey(in ClosedGenericDependency dependency, string? closedKey, bool anyKeyedRegistration)
        => dependency.AnyKey
            || string.Equals(dependency.Key, closedKey, StringComparison.Ordinal)
            || (anyKeyedRegistration && dependency.Key is not null && closedKey is not null);

    /// <summary>
    /// Gets whether a type is already registered with a key: generated as written, or registered in any spelling of its tuple
    /// element names. A registration with another key does not block closing, since it resolves another service.
    /// </summary>
    private static bool IsRegisteredWithKey(
        string typeName,
        string? key,
        HashSet<(string TypeName, string? Key)> generatedClosedGenerics,
        HashSet<ServiceTypeKey> registeredTypes)
        => generatedClosedGenerics.Contains((typeName, key)) || registeredTypes.Contains(new(typeName, key));

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
        in ClosedGenericDependency dependency,
        bool anyKeyedRegistration,
        OpenGenericRegistrationInfo openGenericInfo,
        Dictionary<string, string> typeArgMap,
        List<ServiceRegistrationWithTags> registrations,
        HashSet<(string TypeName, string? Key)> generatedClosedGenerics,
        HashSet<ServiceTypeKey> registeredTypes,
        HashSet<(OpenGenericRegistrationInfo Registration, string ClosingKey)> closedRegistrations)
    {
        var closedServiceType = dependency.ClosedType;
        if(closedServiceType is not GenericTypeData { IsNestedOpenGeneric: false, IsOpenGeneric: false })
        {
            return false;
        }

        // A registration with another key than the dependency's does not resolve it, so another registration may
        var closedKey = SubstituteTypeArgumentsInExpression(openGenericInfo.Key, typeArgMap);
        if(!MatchesDependencyKey(dependency, closedKey, anyKeyedRegistration))
        {
            return false;
        }

        // Skip if already registered with the same key
        if(IsRegisteredWithKey(closedServiceType.Name, closedKey, generatedClosedGenerics, registeredTypes))
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

        // Another spelling of the tuple element names of this runtime type already closed this registration with the same decorators
        if(!closedRegistrations.Add((openGenericInfo, GetClosedRegistrationKey(closedServiceType.Name, [(closedServiceType, factory, serviceTypeDecorators)]))))
        {
            return true;
        }

        // For factory-only registration, both ServiceType and ImplementationType are the same (the service type)
        // The factory method will create the actual instance, so the type is never constructed and its Constructibility
        // stays Unknown: it comes from a dependency, which is extracted without it
        var serviceModel = new ServiceRegistrationModel(
            closedServiceType,
            closedServiceType, // Use service type as implementation type since factory creates the instance
            openGenericInfo.Lifetime,
            closedKey,
            openGenericInfo.KeyType,
            openGenericInfo.KeyValueType,
            IsOpenGeneric: false,
            serviceTypeDecorators,
            openGenericInfo.InjectionMembers,
            factory,
            openGenericInfo.Instance);

        registrations.Add(new ServiceRegistrationWithTags(serviceModel, openGenericInfo.Tags));
        generatedClosedGenerics.Add((closedServiceType.Name, closedKey));

        return true; // Successfully generated registration
    }

    /// <summary>
    /// Gets the key a closing of an open generic registration is compared by: its implementation type, service types and
    /// their decorators as runtime types (without tuple element names, see <see cref="TypeNameComparer"/>).
    /// </summary>
    private static string GetClosedRegistrationKey(
        string closedImplTypeName,
        List<(TypeData ServiceType, FactoryMethodData? Factory, ImmutableEquatableArray<TypeData> Decorators)> closedServices)
    {
        var builder = new StringBuilder(TypeNameComparer.WithoutTupleElementNames(closedImplTypeName));
        foreach(var (serviceType, _, decorators) in closedServices)
        {
            builder.Append('|').Append(TypeNameComparer.WithoutTupleElementNames(serviceType.Name));
            foreach(var decorator in decorators)
            {
                builder.Append(';').Append(TypeNameComparer.WithoutTupleElementNames(decorator.Name));
            }
        }

        return builder.ToString();
    }

    /// <summary>Compares an open generic registration by reference and the key of its closing ordinally.</summary>
    private sealed class ClosedRegistrationComparer : IEqualityComparer<(OpenGenericRegistrationInfo Registration, string ClosingKey)>
    {
        public static ClosedRegistrationComparer Instance { get; } = new();

        public bool Equals((OpenGenericRegistrationInfo Registration, string ClosingKey) x, (OpenGenericRegistrationInfo Registration, string ClosingKey) y)
            => ReferenceEquals(x.Registration, y.Registration) && string.Equals(x.ClosingKey, y.ClosingKey, StringComparison.Ordinal);

        public int GetHashCode((OpenGenericRegistrationInfo Registration, string ClosingKey) obj)
            => unchecked((RuntimeHelpers.GetHashCode(obj.Registration) * 397) ^ StringComparer.Ordinal.GetHashCode(obj.ClosingKey));
    }
}
