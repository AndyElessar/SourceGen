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
        ImmutableEquatableArray<ServiceRegistrationModel> registrations,
        IocFeatures features)
    {
        var reservedNames = GetReservedNames(container);

        // Group registrations for code generation
        var groups = BuildContainerRegistrationGroups(registrations, features, container.ThreadSafeStrategy, container.EagerResolveOptions, reservedNames);

        return new ContainerWithGroups(container, groups);
    }

    /// <summary>
    /// Filters registrations based on the container's IncludeTags settings.
    /// </summary>
    private static ImmutableEquatableArray<ServiceRegistrationModel> FilterRegistrationsForContainer(
        ContainerModel container,
        ImmutableEquatableArray<ServiceRegistrationWithTags> allRegistrations)
    {
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
    /// Groups explicit registrations for ExplicitOnly containers.
    /// </summary>
    private static ContainerWithGroups GroupExplicitOnlyRegistrations(
        ContainerModel container,
        IocFeatures features)
    {
        var registrations = new List<ServiceRegistrationModel>(container.ExplicitRegistrations.Length);

        foreach(var explicitReg in container.ExplicitRegistrations)
        {
            var processed = ProcessExplicitRegistrationForContainer(explicitReg);
            if(processed is not null)
            {
                registrations.Add(processed);
            }
        }

        var reservedNames = GetReservedNames(container);

        var groups = BuildContainerRegistrationGroups(
            registrations.ToImmutableEquatableArray(),
            features,
            container.ThreadSafeStrategy,
            container.EagerResolveOptions,
            reservedNames);

        return new ContainerWithGroups(container, groups);
    }

    /// <summary>
    /// Collect partial accessor method names to avoid naming conflicts
    /// </summary>
    private static HashSet<string> GetReservedNames(ContainerModel container)
    {
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var accessor in container.PartialAccessors)
        {
            if(accessor.Kind == PartialAccessorKind.Method)
            {
                reservedNames.Add(accessor.Name);
            }
        }
        return reservedNames;
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
        var serviceLookup = new Dictionary<(string ServiceType, string? Key), List<ServiceLookupEntry>>();
        var lastWinsLookup = new Dictionary<(string ServiceType, string? Key), ServiceLookupEntry>();

        // Track all unique service types for IsService checks
        var allServiceTypes = new HashSet<string>();

        // Track unique implementations per lifetime using Dictionary instead of List + index tracking.
        // Key: (ImplementationName, ServiceKey, InstanceOrFactory), Value: (ServiceLookupEntry, HasDecorators)
        var singletonMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (ServiceLookupEntry Cached, bool HasDecorators)>();
        var scopedMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (ServiceLookupEntry Cached, bool HasDecorators)>();
        var transientMap = new Dictionary<(string ImplName, string? Key, string? InstanceOrFactory), (ServiceLookupEntry Cached, bool HasDecorators)>();

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
            var cached = CreateServiceLookupEntry(effectiveRegistration, eagerResolveOptions, reservedNames);

            var key = (effectiveRegistration.ServiceType.Name, effectiveRegistration.Key);
            if(effectiveRegistration.Key is not null)
            {
                hasKeyedServices = true;
            }

            if(!serviceLookup.TryGetValue(key, out var list))
            {
                list = [];
                serviceLookup[key] = list;
            }
            list.Add(cached);
            lastWinsLookup[key] = cached;

            // Also add implementation type as a service type (for self-registration)
            if(effectiveRegistration.ImplementationType.Name != effectiveRegistration.ServiceType.Name)
            {
                var implKey = (effectiveRegistration.ImplementationType.Name, effectiveRegistration.Key);
                if(!serviceLookup.TryGetValue(implKey, out var implList))
                {
                    implList = [];
                    serviceLookup[implKey] = implList;
                }
                implList.Add(cached);
                lastWinsLookup[implKey] = cached;
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
        var singletons = singletonMap.Values.Select(static v => v.Cached).ToImmutableEquatableArray();
        var scoped = scopedMap.Values.Select(static v => v.Cached).ToImmutableEquatableArray();
        var transients = transientMap.Values.Select(static v => v.Cached).ToImmutableEquatableArray();

        // Collect service types with multiple registrations for IEnumerable<T> resolution
        // Async-init services are excluded from collection resolution (only Task<T> can access them).
        var collectionServiceTypes = new List<string>();
        var collectionRegistrations = new Dictionary<string, ImmutableEquatableArray<ServiceLookupEntry>>();

        foreach(var kvp in serviceLookup)
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

        var serviceLookupEntries = serviceLookup.ToDictionary(
            static kvp => kvp.Key,
            static kvp => kvp.Value.ToImmutableEquatableArray());

        var wrapperEntries = CreateWrapperContainerEntries(
            singletons,
            scoped,
            transients,
            serviceLookupEntries);

        var lazyFieldByResolver = wrapperEntries
            .OfType<LazyWrapperContainerEntry>()
            .ToDictionary(static e => e.InnerResolverMethodName, static e => e.FieldName, StringComparer.Ordinal);
        var funcFieldByResolver = wrapperEntries
            .OfType<FuncWrapperContainerEntry>()
            .ToDictionary(static e => e.InnerResolverMethodName, static e => e.FieldName, StringComparer.Ordinal);

        var singletonEntries = CreateServiceContainerEntries(
            singletons,
            threadSafeStrategy,
            serviceLookupEntries,
            collectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
        var scopedEntries = CreateServiceContainerEntries(
            scoped,
            threadSafeStrategy,
            serviceLookupEntries,
            collectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
        var transientEntries = CreateServiceContainerEntries(
            transients,
            threadSafeStrategy,
            serviceLookupEntries,
            collectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
        var collectionEntries = CreateCollectionContainerEntries(collectionRegistrations);
        var immutableLastWinsByServiceType = CreateLastWinsByServiceType(
            lastWinsLookup,
            singletonEntries,
            scopedEntries,
            transientEntries);

        return new ContainerRegistrationGroups(
            immutableLastWinsByServiceType,
            allServiceTypes.ToImmutableEquatableSet(),
            hasOpenGenerics,
            hasKeyedServices,
            collectionServiceTypes.ToImmutableEquatableArray(),
            singletonEntries,
            scopedEntries,
            transientEntries,
            wrapperEntries,
            collectionEntries);
    }

    private static ImmutableEquatableDictionary<(string ServiceType, string? Key), IocSourceGenerator.ContainerEntry> CreateLastWinsByServiceType(
        Dictionary<(string ServiceType, string? Key), ServiceLookupEntry> lastWinsLookup,
        ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> singletonEntries,
        ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> scopedEntries,
        ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> transientEntries)
    {
        var entryByResolverMethodName = new Dictionary<string, IocSourceGenerator.ContainerEntry>(StringComparer.Ordinal);
        var entryByResolverAndServiceType = new Dictionary<(string ResolverMethodName, string ServiceType, string? Key), IocSourceGenerator.ContainerEntry>();

        foreach(var entry in singletonEntries)
            AddEntriesByLookup(entryByResolverMethodName, entryByResolverAndServiceType, entry);
        foreach(var entry in scopedEntries)
            AddEntriesByLookup(entryByResolverMethodName, entryByResolverAndServiceType, entry);
        foreach(var entry in transientEntries)
            AddEntriesByLookup(entryByResolverMethodName, entryByResolverAndServiceType, entry);

        var lastWinsByServiceType = new Dictionary<(string ServiceType, string? Key), IocSourceGenerator.ContainerEntry>(lastWinsLookup.Count);

        foreach(var kvp in lastWinsLookup)
        {
            var cached = kvp.Value;

            if(!entryByResolverAndServiceType.TryGetValue((cached.ResolverMethodName, cached.Registration.ServiceType.Name, cached.Registration.Key), out var entry)
                && !entryByResolverMethodName.TryGetValue(cached.ResolverMethodName, out entry))
            {
                continue;
            }

            if(entry is ServiceContainerEntry serviceEntry
                && (!string.Equals(serviceEntry.Registration.ServiceType.Name, cached.Registration.ServiceType.Name, StringComparison.Ordinal)
                    || !string.Equals(serviceEntry.Registration.Key, cached.Registration.Key, StringComparison.Ordinal)))
            {
                entry = CloneServiceEntryWithRegistration(entry, cached.Registration);
            }

            lastWinsByServiceType[kvp.Key] = entry;
        }

        return lastWinsByServiceType.ToImmutableEquatableDictionary();
    }

    private static void AddEntriesByLookup(
        Dictionary<string, IocSourceGenerator.ContainerEntry> entryByResolverMethodName,
        Dictionary<(string ResolverMethodName, string ServiceType, string? Key), IocSourceGenerator.ContainerEntry> entryByResolverAndServiceType,
        IocSourceGenerator.ContainerEntry entry)
    {
        AddEntryByResolverMethodName(entryByResolverMethodName, entry);

        if(entry is not ServiceContainerEntry serviceEntry)
        {
            return;
        }

        var resolverKey = (
            serviceEntry.ResolverMethodName,
            serviceEntry.Registration.ServiceType.Name,
            serviceEntry.Registration.Key);

        if(!entryByResolverAndServiceType.ContainsKey(resolverKey))
        {
            entryByResolverAndServiceType[resolverKey] = entry;
        }
    }

    private static IocSourceGenerator.ContainerEntry CloneServiceEntryWithRegistration(
        IocSourceGenerator.ContainerEntry entry,
        ServiceRegistrationModel registration)
    {
        return entry switch
        {
            InstanceContainerEntry instance => instance with { Registration = registration },
            EagerContainerEntry eager => eager with { Registration = registration },
            LazyThreadSafeContainerEntry lazy => lazy with { Registration = registration },
            TransientContainerEntry transient => transient with { Registration = registration },
            AsyncContainerEntry asyncEntry => asyncEntry with { Registration = registration },
            AsyncTransientContainerEntry asyncTransient => asyncTransient with { Registration = registration },
            _ => entry
        };
    }

    /// <summary>
    /// Creates a ServiceLookupEntry with pre-computed field and method names.
    /// Computes both names in a single pass to avoid redundant string operations.
    /// </summary>
    private static ServiceLookupEntry CreateServiceLookupEntry(
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

        // Determine if this registration should be eagerly resolved via EagerResolveOptions.
        // Instance registrations are inherently eager (no field caching needed).
        // Transient services are not supported for eager resolution.
        // Async-init singleton/scoped services are pre-started separately by AsyncContainerEntry.WriteEagerInit,
        // regardless of EagerResolveOptions, so this flag only governs synchronous singleton/scoped services.
        var isAsyncInit = HasAsyncInitMembers(reg);
        var isEager = reg.Instance is null && !isAsyncInit && reg.Lifetime switch
        {
            ServiceLifetime.Singleton => (eagerResolveOptions & EagerResolveOptions.Singleton) != 0,
            ServiceLifetime.Scoped => (eagerResolveOptions & EagerResolveOptions.Scoped) != 0,
            _ => false // Transient is never eager
        };

        return new ServiceLookupEntry(reg, methodName, fieldName, isAsyncInit, isEager);
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
        ImmutableEquatableArray<ServiceLookupEntry> registrations,
        ThreadSafeStrategy threadSafeStrategy,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver)
    {
        var entries = new List<IocSourceGenerator.ContainerEntry>(registrations.Length);

        foreach(var cached in registrations)
        {
            var reg = cached.Registration;
            var constructorParameters = ResolveConstructorParametersForContainerEntryModel(
                reg,
                serviceLookup,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                allowServiceKeyAttribute: true);
            var injectionMembers = ResolveInjectionMembersForContainerEntryModel(
                reg.InjectionMembers,
                reg,
                serviceLookup,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                allowServiceKeyAttributeForMethods: true);
            var decorators = ResolveDecoratorsForContainerEntryModel(
                reg,
                serviceLookup,
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
        ServiceLookupEntry cached,
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
                constructorParameters,
                injectionMembers,
                decorators);
        }

        if(cached.IsAsyncInit)
        {
            // Async-init singleton/scoped services always use AsyncContainerEntry so the emitted
            // constructor/scope-constructor fire-and-forget path stays independent from IsEager.
            if(reg.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped)
            {
                return new AsyncContainerEntry(
                    reg,
                    cached.ResolverMethodName,
                    cached.FieldName!,
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
                    cached.FieldName!,
                    constructorParameters,
                    injectionMembers,
                    decorators);
            }

            return new LazyThreadSafeContainerEntry(
                reg,
                cached.ResolverMethodName,
                cached.FieldName!,
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
        ImmutableEquatableArray<ServiceLookupEntry> singletons,
        ImmutableEquatableArray<ServiceLookupEntry> scoped,
        ImmutableEquatableArray<ServiceLookupEntry> transients,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup)
    {
        var wrapperEntries = new List<IocSourceGenerator.ContainerEntry>();

        wrapperEntries.AddRange(CreateLazyWrapperContainerEntries(singletons, scoped, transients, serviceLookup));
        wrapperEntries.AddRange(CreateFuncWrapperContainerEntries(singletons, scoped, transients, serviceLookup));
        wrapperEntries.AddRange(CreateKvpWrapperContainerEntries(singletons, scoped, transients, serviceLookup));

        return wrapperEntries.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> CreateCollectionContainerEntries(
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations)
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
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
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
                serviceLookup,
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
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
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
                        serviceLookup,
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
                                serviceLookup,
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
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
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
                        serviceLookup,
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
                serviceLookup,
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
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
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
            serviceLookup,
            collectionRegistrations,
            lazyFieldByResolver,
            funcFieldByResolver);
    }

    private static ResolvedDependency ResolveServiceDependencyForContainerEntryModel(
        TypeData type,
        string? key,
        bool isOptional,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
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
                && HasKvpRegistrationsForContainerEntryModel(kvpElement.KeyType.Name, kvpElement.ValueType.Name, serviceLookup))
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
                serviceLookup,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                useResolverMethods: true);
        }

        if(serviceLookup.TryGetValue((type.Name, key), out var registrations))
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
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
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
                    if(serviceLookup.TryGetValue((innerType.Name, key), out var innerRegistrations))
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
                        serviceLookup,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver));
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;

                if(func.HasInputParameters)
                {
                    if(serviceLookup.TryGetValue((innerType.Name, key), out var innerRegistrations))
                    {
                        var targetRegistration = innerRegistrations[^1].Registration;
                        return new MultiParamFuncDependency(
                            innerType.Name,
                            CreateFuncInputParameters(func.InputTypes),
                            ResolveConstructorParametersForContainerEntryModel(
                                targetRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver,
                                allowServiceKeyAttribute: true),
                            ResolveInjectionMembersForContainerEntryModel(
                                targetRegistration.InjectionMembers,
                                targetRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver,
                                allowServiceKeyAttributeForMethods: true),
                            ResolveDecoratorsForContainerEntryModel(
                                targetRegistration,
                                serviceLookup,
                                collectionRegistrations,
                                lazyFieldByResolver,
                                funcFieldByResolver),
                            targetRegistration.ImplementationType.Name);
                    }

                    return new FallbackProviderDependency(type.Name, key, isOptional);
                }

                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(serviceLookup.TryGetValue((innerType.Name, key), out var innerRegistrations))
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
                        serviceLookup,
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
                        serviceLookup,
                        collectionRegistrations,
                        lazyFieldByResolver,
                        funcFieldByResolver));

            case DictionaryTypeData dictionary:
            {
                if(key is null && HasKvpRegistrationsForContainerEntryModel(dictionary.KeyType.Name, dictionary.ValueType.Name, serviceLookup))
                {
                    return new DictionaryResolverDependency(GetKvpDictionaryResolverMethodName(dictionary.KeyType.Name, dictionary.ValueType.Name));
                }

                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{dictionary.KeyType.Name}, {dictionary.ValueType.Name}>";
                return new DictionaryFallbackDependency(kvpTypeName, IsKeyed: key is not null, Key: key);
            }

            case TaskTypeData task:
            {
                if(serviceLookup.TryGetValue((task.InnerType.Name, key), out var innerRegistrations))
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
                    serviceLookup,
                    collectionRegistrations,
                    lazyFieldByResolver,
                    funcFieldByResolver);
        }
    }

    private static ResolvedDependency ResolveInnerDependencyForContainerEntryModel(
        TypeData innerType,
        string? key,
        bool isOptional,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        IReadOnlyDictionary<string, string> lazyFieldByResolver,
        IReadOnlyDictionary<string, string> funcFieldByResolver)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return ResolveWrapperDependencyForContainerEntryModel(
                innerType,
                key,
                isOptional,
                serviceLookup,
                collectionRegistrations,
                lazyFieldByResolver,
                funcFieldByResolver,
                useResolverMethods: false);
        }

        return ResolveServiceDependencyForContainerEntryModel(
            innerType,
            key,
            isOptional,
            serviceLookup,
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
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup)
    {
        foreach(var kvp in serviceLookup)
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

    private readonly record struct ServiceLookupEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string? FieldName,
        bool IsAsyncInit,
        bool IsEager);
}
