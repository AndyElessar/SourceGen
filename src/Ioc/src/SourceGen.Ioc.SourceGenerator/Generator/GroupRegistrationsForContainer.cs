using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Transforms container and registrations into grouped data for code generation.
    /// This step is separated from output generation to enable incremental generator caching.
    /// </summary>
    private static ContainerWithGroups GroupRegistrationsForContainer(
        ContainerModel container,
        ImmutableEquatableArray<ServiceRegistrationWithTags> allRegistrations,
        IocFeatures features)
    {
        // Filter registrations based on ExplicitOnly mode
        var registrations = FilterRegistrationsForContainer(container, allRegistrations);

        // Collect partial accessor method names to avoid naming conflicts
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var accessor in container.PartialAccessors)
        {
            if(accessor.Kind == PartialAccessorKind.Method)
            {
                reservedNames.Add(accessor.Name);
            }
        }

        // Group registrations for code generation
        var groups = BuildContainerRegistrationGroups(registrations, features, container.ThreadSafeStrategy, container.EagerResolveOptions, reservedNames);

        return new ContainerWithGroups(container, groups);
    }

    /// <summary>
    /// Filters registrations based on the container's ExplicitOnly and IncludeTags settings.
    /// Priority: ExplicitOnly > IncludeTags > All registrations.
    /// </summary>
    private static ImmutableEquatableArray<ServiceRegistrationModel> FilterRegistrationsForContainer(
        ContainerModel container,
        ImmutableEquatableArray<ServiceRegistrationWithTags> allRegistrations)
    {
        // ExplicitOnly takes precedence over IncludeTags
        if(container.ExplicitOnly)
        {
            // Only include explicit registrations from the container class
            var builder = new List<ServiceRegistrationModel>();

            foreach(var explicitReg in container.ExplicitRegistrations)
            {
                // Convert RegistrationData to ServiceRegistrationModel
                // For explicit registrations, we process them with default settings
                var processed = ProcessExplicitRegistrationForContainer(explicitReg);
                if(processed is not null)
                {
                    builder.Add(processed);
                }
            }

            return builder.ToImmutableEquatableArray();
        }

        // Apply IncludeTags filtering if specified
        if(container.IncludeTags.Length > 0)
        {
            // Only include services that have at least one matching tag
            return allRegistrations
                .Where(r => r.Tags.Length > 0 && r.Tags.Any(tag => container.IncludeTags.Contains(tag, StringComparer.Ordinal)))
                .Select(static r => r.Registration)
                .ToImmutableEquatableArray();
        }

        // Include all registrations from the assembly
        return allRegistrations
            .Select(static r => r.Registration)
            .ToImmutableEquatableArray();
    }

    /// <summary>
    /// Converts explicit RegistrationData to ServiceRegistrationModel for container generation.
    /// </summary>
    private static ServiceRegistrationModel? ProcessExplicitRegistrationForContainer(RegistrationData data)
    {
        // Get the first service type, or use implementation type
        var serviceType = data.ServiceTypes.Length > 0
            ? data.ServiceTypes[0]
            : data.ImplementationType;

        return new ServiceRegistrationModel(
            serviceType,
            data.ImplementationType,
            data.Lifetime,
            data.Key,
            data.KeyType,
            data.KeyValueType,
            data.ImplementationType is GenericTypeData { IsOpenGeneric: true },
            data.Decorators,
            data.InjectionMembers,
            data.Factory,
            data.Instance);
    }

    /// <summary>
    /// Groups registrations by service type and key for efficient lookup and collection resolution.
    /// Pre-computes field names, method names, and disposal lists to avoid redundant calculations.
    /// </summary>
    private static ContainerRegistrationGroups BuildContainerRegistrationGroups(
        ImmutableEquatableArray<ServiceRegistrationModel> registrations,
        IocFeatures features,
        ThreadSafeStrategy threadSafeStrategy,
        EagerResolveOptions eagerResolveOptions,
        HashSet<string> reservedNames)
    {
        // Group by (ServiceType.Name, Key) for efficient lookup
        var byServiceTypeAndKey = new Dictionary<(string ServiceType, string? Key), List<CachedRegistration>>();

        // Track all unique service types for IsService checks
        var allServiceTypes = new HashSet<string>();

        // Track unique implementations per lifetime using Dictionary instead of List + index tracking.
        // Key: (ImplementationName, ServiceKey, InstanceOrFactory), Value: (CachedRegistration, HasDecorators)
        var singletonMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (CachedRegistration Cached, bool HasDecorators)>();
        var scopedMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (CachedRegistration Cached, bool HasDecorators)>();
        var transientMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (CachedRegistration Cached, bool HasDecorators)>();

        var hasAllInjectionFeatures = IocFeaturesHelper.HasAllInjectionFeatures(features);
        var hasOpenGenerics = false;
        var hasKeyedServices = false;

        foreach(var reg in registrations)
        {
            var effectiveRegistration = hasAllInjectionFeatures ? reg : FilterRegistrationForFeatures(reg, features);

            if(effectiveRegistration.IsOpenGeneric)
            {
                hasOpenGenerics = true;
                // Skip open generics for most processing but track the flag
                continue;
            }

            // Pre-compute field and method names once, including IsEager flag
            var cached = CreateCachedRegistration(effectiveRegistration, eagerResolveOptions, reservedNames);

            var key = (effectiveRegistration.ServiceType.Name, effectiveRegistration.Key);
            if(effectiveRegistration.Key is not null)
            {
                hasKeyedServices = true;
            }

            if(!byServiceTypeAndKey.TryGetValue(key, out var list))
            {
                list = [];
                byServiceTypeAndKey[key] = list;
            }
            list.Add(cached);

            // Also add implementation type as a service type (for self-registration)
            if(effectiveRegistration.ImplementationType.Name != effectiveRegistration.ServiceType.Name)
            {
                var implKey = (effectiveRegistration.ImplementationType.Name, effectiveRegistration.Key);
                if(!byServiceTypeAndKey.TryGetValue(implKey, out var implList))
                {
                    implList = [];
                    byServiceTypeAndKey[implKey] = implList;
                }
                implList.Add(cached);
            }

            // Track closed types for IsService checks
            allServiceTypes.Add(effectiveRegistration.ServiceType.Name);
            allServiceTypes.Add(effectiveRegistration.ImplementationType.Name);

            // Group by lifetime - prefer registration with decorators for field generation
            // Include Instance or Factory in the key to distinguish multiple instance/factory registrations
            var instanceOrFactory = effectiveRegistration.Instance ?? effectiveRegistration.Factory?.Path;
            var lifetimeKey = (effectiveRegistration.ImplementationType.Name, effectiveRegistration.Key, instanceOrFactory);
            var hasDecorators = effectiveRegistration.Decorators.Length > 0;

            var targetMap = effectiveRegistration.Lifetime switch
            {
                ServiceLifetime.Singleton => singletonMap,
                ServiceLifetime.Scoped => scopedMap,
                _ => transientMap
            };

            if(targetMap.TryGetValue(lifetimeKey, out var existing))
            {
                // Skip if already seen with decorators, or if current doesn't have decorators
                if(existing.HasDecorators || !hasDecorators)
                {
                    continue;
                }
            }

            targetMap[lifetimeKey] = (cached, hasDecorators);
        }

        // Convert maps to lists (preserving insertion order via Dictionary enumeration order in .NET)
        var singletons = singletonMap.Values.Select(static v => v.Cached).ToList();
        var scoped = scopedMap.Values.Select(static v => v.Cached).ToList();
        var transients = transientMap.Values.Select(static v => v.Cached).ToList();

        // Collect service types with multiple registrations for IEnumerable<T> resolution
        // Async-init services are excluded from collection resolution (only Task<T> can access them).
        var collectionServiceTypes = new List<string>();
        var collectionRegistrations = new Dictionary<string, ImmutableEquatableArray<CachedRegistration>>();

        foreach(var kvp in byServiceTypeAndKey)
        {
            // Include non-keyed service types with multiple registrations
            if(kvp.Key.Key is null && kvp.Value.Count > 1)
            {
                // Filter out async-init registrations — they cannot appear in IEnumerable<T> resolvers
                var effectiveRegistrations = kvp.Value.Where(static c => !c.IsAsyncInit).ToImmutableEquatableArray();

                // Deduplicate resolver method names to count unique implementations
                var uniqueResolvers = new HashSet<string>();
                foreach(var cached in effectiveRegistrations)
                {
                    uniqueResolvers.Add(cached.ResolverMethodName);
                }

                // Only generate collection if there are multiple unique resolvers
                if(uniqueResolvers.Count > 1)
                {
                    collectionServiceTypes.Add(kvp.Key.ServiceType);
                    collectionRegistrations[kvp.Key.ServiceType] = effectiveRegistrations;
                }
            }
        }

        // Convert to immutable collections
        var immutableByServiceTypeAndKey = byServiceTypeAndKey
            .ToImmutableEquatableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToImmutableEquatableArray());

        var immutableSingletons = singletons.ToImmutableEquatableArray();
        var immutableScoped = scoped.ToImmutableEquatableArray();
        var immutableTransients = transients.ToImmutableEquatableArray();

        var lazyEntries = CollectContainerLazyEntries(
            immutableSingletons,
            immutableScoped,
            immutableTransients,
            immutableByServiceTypeAndKey);
        var funcEntries = CollectContainerFuncEntries(
            immutableSingletons,
            immutableScoped,
            immutableTransients,
            immutableByServiceTypeAndKey);
        var kvpEntries = CollectContainerKvpEntries(
            immutableSingletons,
            immutableScoped,
            immutableTransients,
            immutableByServiceTypeAndKey);

        var immutableCollectionRegistrations = collectionRegistrations.ToImmutableEquatableDictionary();

        var lazyFieldByResolver = lazyEntries.ToDictionary(static e => e.ResolverMethodName, static e => e.FieldName, StringComparer.Ordinal);
        var funcFieldByResolver = funcEntries.ToDictionary(static e => e.ResolverMethodName, static e => e.FieldName, StringComparer.Ordinal);

        var singletonEntries = CreateServiceContainerEntries(
            immutableSingletons,
            threadSafeStrategy,
            immutableByServiceTypeAndKey,
            immutableCollectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
        var scopedEntries = CreateServiceContainerEntries(
            immutableScoped,
            threadSafeStrategy,
            immutableByServiceTypeAndKey,
            immutableCollectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
        var transientEntries = CreateServiceContainerEntries(
            immutableTransients,
            threadSafeStrategy,
            immutableByServiceTypeAndKey,
            immutableCollectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
        var wrapperEntries = CreateWrapperContainerEntries(lazyEntries, funcEntries, kvpEntries, immutableByServiceTypeAndKey);
        var collectionEntries = CreateCollectionContainerEntries(immutableCollectionRegistrations);

        return new ContainerRegistrationGroups(
            immutableByServiceTypeAndKey,
            allServiceTypes.ToImmutableEquatableSet(),
            lazyEntries,
            funcEntries,
            kvpEntries,
            hasOpenGenerics,
            hasKeyedServices,
            collectionServiceTypes.ToImmutableEquatableArray(),
            singletonEntries,
            scopedEntries,
            transientEntries,
            wrapperEntries,
            collectionEntries);
    }

    /// <summary>
    /// Creates a CachedRegistration with pre-computed field and method names.
    /// Computes both names in a single pass to avoid redundant string operations.
    /// </summary>
    private static CachedRegistration CreateCachedRegistration(
        ServiceRegistrationModel reg,
        EagerResolveOptions eagerResolveOptions,
        HashSet<string> reservedNames)
    {
        var (fieldName, methodName) = ComputeServiceNames(reg);

        // Avoid naming conflicts with user-declared partial accessor methods
        if(reservedNames.Contains(methodName))
        {
            methodName = $"{methodName}_Resolve";
        }

        // Determine if this registration should be eagerly resolved
        // Instance registrations are inherently eager (no field caching needed)
        // Transient services are not supported for eager resolution
        // Async-init services must always be lazy (cannot be started in constructor)
        var isAsyncInit = HasAsyncInitMembers(reg);
        var isEager = reg.Instance is null && !isAsyncInit && reg.Lifetime switch
        {
            ServiceLifetime.Singleton => (eagerResolveOptions & EagerResolveOptions.Singleton) != 0,
            ServiceLifetime.Scoped => (eagerResolveOptions & EagerResolveOptions.Scoped) != 0,
            _ => false // Transient is never eager
        };

        return new CachedRegistration(reg, fieldName, methodName, isEager, isAsyncInit);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the registration has at least one
    /// <see cref="InjectionMemberType.AsyncMethod"/> member, making it an async-init service.
    /// </summary>
    private static bool HasAsyncInitMembers(ServiceRegistrationModel reg)
    {
        foreach(var m in reg.InjectionMembers)
        {
            if(m.MemberType == InjectionMemberType.AsyncMethod)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes both field name and resolver method name for a service in a single pass.
    /// This avoids redundant GetSafeIdentifier calls and string operations.
    /// </summary>
    /// <returns>A tuple containing (FieldName, ResolverMethodName).</returns>
    private static (string FieldName, string ResolverMethodName) ComputeServiceNames(ServiceRegistrationModel reg)
    {
        var implType = reg.ImplementationType;
        var typeName = implType switch
        {
            GenericTypeData { IsOpenGeneric: false } genericTypeData when genericTypeData.Name != genericTypeData.NameWithoutGeneric => genericTypeData.Name,
            GenericTypeData genericTypeData => genericTypeData.NameWithoutGeneric,
            _ => implType.Name,
        };
        var baseName = GetSafeIdentifier(typeName);
        var lowerFirstChar = char.ToLowerInvariant(baseName[0]);
        var restOfName = baseName[1..];

        // Handle keyed services
        if(reg.Key is not null)
        {
            var safeKey = GetSafeIdentifier(reg.Key);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeKey}",
                $"Get{baseName}_{safeKey}"
            );
        }

        // Handle instance registrations - include instance name in the method name
        if(reg.Instance is not null)
        {
            var safeInstance = GetSafeIdentifier(reg.Instance);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeInstance}",
                $"Get{baseName}_{safeInstance}"
            );
        }

        // Handle factory registrations - include factory path in the method name
        if(reg.Factory is not null)
        {
            var safeFactory = GetSafeIdentifier(reg.Factory.Path);
            return (
                $"_{lowerFirstChar}{restOfName}_{safeFactory}",
                $"Get{baseName}_{safeFactory}"
            );
        }

        return (
            $"_{lowerFirstChar}{restOfName}",
            $"Get{baseName}"
        );
    }

    private static ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> CreateServiceContainerEntries(
        ImmutableEquatableArray<CachedRegistration> registrations,
        ThreadSafeStrategy threadSafeStrategy,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver)
    {
        var entries = new List<IocSourceGenerator.ContainerEntry>(registrations.Length);

        foreach(var cached in registrations)
        {
            var reg = cached.Registration;
            var constructorParameters = ResolveConstructorParametersForContainerEntryModel(
                reg,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                allowServiceKeyAttribute: true);
            var injectionMembers = ResolveInjectionMembersForContainerEntryModel(
                reg.InjectionMembers,
                reg,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                allowServiceKeyAttributeForMethods: true);
            var decorators = ResolveDecoratorsForContainerEntryModel(
                reg,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver);

            entries.Add(CreateServiceContainerEntryModel(
                cached,
                threadSafeStrategy,
                constructorParameters,
                injectionMembers,
                decorators));
        }

        return entries.ToImmutableEquatableArray();
    }

    private static IocSourceGenerator.ContainerEntry CreateServiceContainerEntryModel(
        CachedRegistration cached,
        ThreadSafeStrategy threadSafeStrategy,
        ImmutableEquatableArray<ResolvedConstructorParameter> constructorParameters,
        ImmutableEquatableArray<ResolvedInjectionMember> injectionMembers,
        ImmutableEquatableArray<ResolvedDecorator> decorators)
    {
        var reg = cached.Registration;

        if(reg.Instance is not null)
        {
            return new InstanceContainerEntry(
                reg,
                cached.ResolverMethodName,
                cached.FieldName,
                constructorParameters,
                injectionMembers,
                decorators);
        }

        if(cached.IsAsyncInit)
        {
            if(reg.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped)
            {
                return new AsyncContainerEntry(
                    reg,
                    cached.ResolverMethodName,
                    cached.FieldName,
                    GetEffectiveThreadSafeStrategy(threadSafeStrategy, true),
                    constructorParameters,
                    injectionMembers,
                    decorators);
            }

            return new AsyncTransientContainerEntry(
                reg,
                cached.ResolverMethodName,
                constructorParameters,
                injectionMembers,
                decorators);
        }

        if(reg.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped)
        {
            if(cached.IsEager)
            {
                return new EagerContainerEntry(
                    reg,
                    cached.ResolverMethodName,
                    cached.FieldName,
                    constructorParameters,
                    injectionMembers,
                    decorators);
            }

            return new LazyThreadSafeContainerEntry(
                reg,
                cached.ResolverMethodName,
                cached.FieldName,
                threadSafeStrategy,
                constructorParameters,
                injectionMembers,
                decorators);
        }

        return new TransientContainerEntry(
            reg,
            cached.ResolverMethodName,
            constructorParameters,
            injectionMembers,
            decorators);
    }

    private static ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> CreateWrapperContainerEntries(
        ImmutableEquatableArray<ContainerLazyEntry> lazyEntries,
        ImmutableEquatableArray<ContainerFuncEntry> funcEntries,
        ImmutableEquatableArray<ContainerKvpEntry> kvpEntries,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey)
    {
        var wrapperEntries = new List<IocSourceGenerator.ContainerEntry>(lazyEntries.Length + funcEntries.Length + kvpEntries.Length);

        wrapperEntries.AddRange(CreateLazyWrapperContainerEntries(lazyEntries, byServiceTypeAndKey));
        wrapperEntries.AddRange(CreateFuncWrapperContainerEntries(funcEntries, byServiceTypeAndKey));
        wrapperEntries.AddRange(CreateKvpWrapperContainerEntries(kvpEntries));

        return wrapperEntries.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> CreateCollectionContainerEntries(
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations)
    {
        var entries = new List<IocSourceGenerator.ContainerEntry>(collectionRegistrations.Count);

        foreach(var kvp in collectionRegistrations)
        {
            var elementResolvers = new List<ResolvedDependency>(kvp.Value.Length);
            var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach(var cached in kvp.Value)
            {
                var uniqueKey = cached.Registration.Instance ?? cached.ResolverMethodName;
                if(!uniqueKeys.Add(uniqueKey))
                    continue;

                if(cached.Registration.Instance is not null)
                {
                    elementResolvers.Add(new InstanceExpressionDependency(cached.Registration.Instance));
                }
                else
                {
                    elementResolvers.Add(new DirectServiceDependency(cached.ResolverMethodName));
                }
            }

            if(elementResolvers.Count < 2)
                continue;

            entries.Add(new CollectionContainerEntry(
                kvp.Key,
                GetArrayResolverMethodName(kvp.Key),
                elementResolvers.ToImmutableEquatableArray()));
        }

        return entries.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<ResolvedConstructorParameter> ResolveConstructorParametersForContainerEntryModel(
        ServiceRegistrationModel registration,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver,
        bool allowServiceKeyAttribute)
    {
        var constructorParameters = registration.Factory?.AdditionalParameters ?? registration.ImplementationType.ConstructorParameters;
        if(constructorParameters is null or { Length: 0 })
            return [];

        var resolved = new List<ResolvedConstructorParameter>(constructorParameters.Length);

        foreach(var parameter in constructorParameters)
        {
            var dependency = ResolveParameterDependencyForContainerEntryModel(
                parameter,
                registration,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                allowServiceKeyAttribute);

            resolved.Add(new ResolvedConstructorParameter(parameter, dependency, parameter.IsOptional));
        }

        return resolved.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<ResolvedInjectionMember> ResolveInjectionMembersForContainerEntryModel(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers,
        ServiceRegistrationModel ownerRegistration,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver,
        bool allowServiceKeyAttributeForMethods)
    {
        if(injectionMembers.Length == 0)
            return [];

        var resolvedMembers = new List<ResolvedInjectionMember>(injectionMembers.Length);

        foreach(var member in injectionMembers)
        {
            ResolvedDependency? dependency = null;
            ImmutableEquatableArray<ResolvedDependency> parameterDependencies = [];

            switch(member.MemberType)
            {
                case InjectionMemberType.Property or InjectionMemberType.Field when member.Type is not null:
                    dependency = ResolveServiceDependencyForContainerEntryModel(
                        member.Type,
                        member.Key,
                        member.IsNullable,
                        byServiceTypeAndKey,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver);
                    break;

                case InjectionMemberType.Method or InjectionMemberType.AsyncMethod:
                {
                    var methodParameters = member.Parameters;
                    if(methodParameters is { Length: > 0 })
                    {
                        var resolvedParameters = new List<ResolvedDependency>(methodParameters.Length);

                        foreach(var parameter in methodParameters)
                        {
                            resolvedParameters.Add(ResolveParameterDependencyForContainerEntryModel(
                                parameter,
                                ownerRegistration,
                                byServiceTypeAndKey,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver,
                                allowServiceKeyAttributeForMethods));
                        }

                        parameterDependencies = resolvedParameters.ToImmutableEquatableArray();
                        if(parameterDependencies.Length == 1)
                        {
                            dependency = parameterDependencies[0];
                        }
                    }

                    break;
                }
            }

            resolvedMembers.Add(new ResolvedInjectionMember(member, dependency, parameterDependencies));
        }

        return resolvedMembers.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<ResolvedDecorator> ResolveDecoratorsForContainerEntryModel(
        ServiceRegistrationModel registration,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver)
    {
        if(registration.Decorators.Length == 0)
            return [];

        var decorators = new List<ResolvedDecorator>(registration.Decorators.Length);

        foreach(var decoratorType in registration.Decorators)
        {
            var constructorParameters = decoratorType.ConstructorParameters;
            var resolvedParameters = new List<ResolvedConstructorParameter>(constructorParameters?.Length ?? 0);

            if(constructorParameters is { Length: > 1 })
            {
                for(var i = 1; i < constructorParameters.Length; i++)
                {
                    var parameter = constructorParameters[i];
                    var dependency = ResolveServiceDependencyForContainerEntryModel(
                        parameter.Type,
                        parameter.ServiceKey,
                        parameter.IsOptional,
                        byServiceTypeAndKey,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver);

                    resolvedParameters.Add(new ResolvedConstructorParameter(parameter, dependency, parameter.IsOptional));
                }
            }

            var decoratorInjectionMembers = decoratorType.InjectionMembers ?? [];
            var resolvedInjectionMembers = ResolveInjectionMembersForContainerEntryModel(
                decoratorInjectionMembers,
                registration,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                allowServiceKeyAttributeForMethods: false);

            var decoratorRegistration = new ServiceRegistrationModel(
                registration.ServiceType,
                decoratorType,
                registration.Lifetime,
                registration.Key,
                registration.KeyType,
                registration.KeyValueType,
                decoratorType is GenericTypeData { IsOpenGeneric: true },
                [],
                decoratorInjectionMembers,
                Factory: null,
                Instance: null);

            decorators.Add(new ResolvedDecorator(
                decoratorRegistration,
                resolvedParameters.ToImmutableEquatableArray(),
                resolvedInjectionMembers));
        }

        return decorators.ToImmutableEquatableArray();
    }

    private static ResolvedDependency ResolveParameterDependencyForContainerEntryModel(
        ParameterData parameter,
        ServiceRegistrationModel ownerRegistration,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver,
        bool allowServiceKeyAttribute)
    {
        if(allowServiceKeyAttribute && parameter.HasServiceKeyAttribute)
        {
            return new ServiceKeyLiteralDependency(parameter.Type.Name, ownerRegistration.Key ?? "null");
        }

        if(parameter.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
        {
            return new ServiceProviderSelfDependency();
        }

        return ResolveServiceDependencyForContainerEntryModel(
            parameter.Type,
            parameter.ServiceKey,
            parameter.IsOptional,
            byServiceTypeAndKey,
            collectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
    }

    private static ResolvedDependency ResolveServiceDependencyForContainerEntryModel(
        TypeData type,
        string? key,
        bool isOptional,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver)
    {
        if(type is CollectionWrapperTypeData collectionType)
        {
            var elementTypeName = collectionType.ElementType.Name;

            if(key is not null)
            {
                return new CollectionFallbackDependency(elementTypeName, IsKeyed: true, Key: key);
            }

            if(collectionType.ElementType is KeyValuePairTypeData kvpElement
                && HasKvpRegistrationsForContainerEntryModel(kvpElement.KeyType.Name, kvpElement.ValueType.Name, byServiceTypeAndKey))
            {
                var isArrayType = collectionType.WrapperKind is WrapperKind.ReadOnlyList or WrapperKind.List or WrapperKind.Array;
                return isArrayType
                    ? new KvpResolverDependency(GetKvpArrayResolverMethodName(kvpElement.KeyType.Name, kvpElement.ValueType.Name))
                    : new DictionaryResolverDependency(GetKvpDictionaryResolverMethodName(kvpElement.KeyType.Name, kvpElement.ValueType.Name));
            }

            if(collectionRegistrations.ContainsKey(elementTypeName))
            {
                return new CollectionDependency(GetArrayResolverMethodName(elementTypeName));
            }

            return new CollectionFallbackDependency(elementTypeName, IsKeyed: false, Key: null);
        }

        if(type is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return ResolveWrapperDependencyForContainerEntryModel(
                type,
                key,
                isOptional,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                useResolverMethods: true);
        }

        if(byServiceTypeAndKey.TryGetValue((type.Name, key), out var registrations))
        {
            var cached = registrations[^1];
            if(cached.IsAsyncInit)
            {
                return cached.Registration.Lifetime == ServiceLifetime.Transient
                    ? new DirectServiceDependency(GetAsyncCreateMethodName(cached.ResolverMethodName))
                    : new DirectServiceDependency(GetAsyncResolverMethodName(cached.ResolverMethodName));
            }

            return new DirectServiceDependency(cached.ResolverMethodName);
        }

        return new FallbackProviderDependency(type.Name, key, isOptional);
    }

    private static ResolvedDependency ResolveWrapperDependencyForContainerEntryModel(
        TypeData type,
        string? key,
        bool isOptional,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver,
        bool useResolverMethods)
    {
        switch(type)
        {
            case LazyTypeData lazy:
            {
                var innerType = lazy.InstanceType;

                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(byServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegistrations))
                    {
                        var resolverMethodName = innerRegistrations[^1].ResolverMethodName;
                        if(lazyFieldByResolver.TryGetValue(resolverMethodName, out var fieldName))
                        {
                            return new LazyFieldReferenceDependency(fieldName);
                        }
                    }

                    return new LazyInlineDependency(
                        innerType.Name,
                        new FallbackProviderDependency(innerType.Name, key, isOptional));
                }

                return new LazyInlineDependency(
                    innerType.Name,
                    ResolveInnerDependencyForContainerEntryModel(
                        innerType,
                        key,
                        isOptional,
                        byServiceTypeAndKey,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver));
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;

                if(func.HasInputParameters)
                {
                    if(byServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegistrations))
                    {
                        var targetRegistration = innerRegistrations[^1].Registration;
                        return new MultiParamFuncDependency(
                            innerType.Name,
                            CreateFuncInputParameters(func.InputTypes),
                            ResolveConstructorParametersForContainerEntryModel(
                                targetRegistration,
                                byServiceTypeAndKey,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver,
                                allowServiceKeyAttribute: true),
                            ResolveInjectionMembersForContainerEntryModel(
                                targetRegistration.InjectionMembers,
                                targetRegistration,
                                byServiceTypeAndKey,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver,
                                allowServiceKeyAttributeForMethods: true),
                            ResolveDecoratorsForContainerEntryModel(
                                targetRegistration,
                                byServiceTypeAndKey,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver),
                            targetRegistration.ImplementationType.Name);
                    }

                    return new FallbackProviderDependency(type.Name, key, isOptional);
                }

                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(byServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegistrations))
                    {
                        var resolverMethodName = innerRegistrations[^1].ResolverMethodName;
                        if(funcFieldByResolver.TryGetValue(resolverMethodName, out var fieldName))
                        {
                            return new FuncFieldReferenceDependency(fieldName);
                        }
                    }

                    return new FuncInlineDependency(
                        innerType.Name,
                        new FallbackProviderDependency(innerType.Name, key, isOptional));
                }

                return new FuncInlineDependency(
                    innerType.Name,
                    ResolveInnerDependencyForContainerEntryModel(
                        innerType,
                        key,
                        isOptional,
                        byServiceTypeAndKey,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver));
            }

            case KeyValuePairTypeData kvp:
                return new KvpInlineDependency(
                    kvp.KeyType.Name,
                    kvp.ValueType.Name,
                    key ?? "default",
                    ResolveInnerDependencyForContainerEntryModel(
                        kvp.ValueType,
                        key,
                        isOptional,
                        byServiceTypeAndKey,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver));

            case DictionaryTypeData dictionary:
            {
                if(key is null && HasKvpRegistrationsForContainerEntryModel(dictionary.KeyType.Name, dictionary.ValueType.Name, byServiceTypeAndKey))
                {
                    return new DictionaryResolverDependency(GetKvpDictionaryResolverMethodName(dictionary.KeyType.Name, dictionary.ValueType.Name));
                }

                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{dictionary.KeyType.Name}, {dictionary.ValueType.Name}>";
                return new DictionaryFallbackDependency(kvpTypeName, IsKeyed: key is not null, Key: key);
            }

            case TaskTypeData task:
            {
                if(byServiceTypeAndKey.TryGetValue((task.InnerType.Name, key), out var innerRegistrations))
                {
                    var cached = innerRegistrations[^1];
                    if(cached.IsAsyncInit)
                    {
                        return new TaskAsyncDependency(GetAsyncResolverMethodName(cached.ResolverMethodName), task.InnerType.Name);
                    }

                    return new TaskFromResultDependency(new DirectServiceDependency(cached.ResolverMethodName), task.InnerType.Name);
                }

                return new FallbackProviderDependency(type.Name, key, isOptional);
            }

            default:
                return ResolveServiceDependencyForContainerEntryModel(
                    type,
                    key,
                    isOptional,
                    byServiceTypeAndKey,
                    collectionRegistrations,
                    lazyFieldByResolver,
                    funcFieldByResolver);
        }
    }

    private static ResolvedDependency ResolveInnerDependencyForContainerEntryModel(
        TypeData innerType,
        string? key,
        bool isOptional,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey,
        ImmutableEquatableDictionary<string, ImmutableEquatableArray<CachedRegistration>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return ResolveWrapperDependencyForContainerEntryModel(
                innerType,
                key,
                isOptional,
                byServiceTypeAndKey,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                useResolverMethods: false);
        }

        return ResolveServiceDependencyForContainerEntryModel(
            innerType,
            key,
            isOptional,
            byServiceTypeAndKey,
            collectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
    }

    private static ImmutableEquatableArray<ParameterData> CreateFuncInputParameters(ImmutableEquatableArray<TypeParameter> inputTypes)
    {
        if(inputTypes.Length == 0)
            return [];

        var parameters = new List<ParameterData>(inputTypes.Length);
        for(var i = 0; i < inputTypes.Length; i++)
        {
            parameters.Add(new ParameterData($"arg{i}", inputTypes[i].Type));
        }

        return parameters.ToImmutableEquatableArray();
    }

    private static bool HasKvpRegistrationsForContainerEntryModel(
        string keyTypeName,
        string valueTypeName,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey)
    {
        foreach(var kvp in byServiceTypeAndKey)
        {
            if(kvp.Key.Key is null)
                continue;

            if(!string.Equals(kvp.Key.ServiceType, valueTypeName, StringComparison.Ordinal))
                continue;

            var cached = kvp.Value[^1];
            if(IsKeyTypeCompatible(keyTypeName, cached.Registration.KeyValueType))
                return true;
        }

        return false;
    }
}
