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
        IocFeatures features,
        ImmutableEquatableArray<string> requestedCollectionServiceTypes)
    {
        var reservedNames = GetReservedNames(container);

        // Group registrations for code generation
        var groups = BuildContainerRegistrationGroups(registrations, features, container.ThreadSafeStrategy, container.EagerResolveOptions, reservedNames, requestedCollectionServiceTypes);

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
            var includeTags = new HashSet<string>(container.IncludeTags, StringComparer.Ordinal);
            var filtered = new List<ServiceRegistrationModel>();
            foreach(var r in allRegistrations)
            {
                foreach(var tag in r.Tags)
                {
                    if(includeTags.Contains(tag))
                    {
                        filtered.Add(r.Registration);
                        break;
                    }
                }
            }

            return filtered.ToImmutableEquatableArray();
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
        IocFeatures features,
        ImmutableEquatableArray<string> requestedCollectionServiceTypes)
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
            reservedNames,
            requestedCollectionServiceTypes);

        return new ContainerWithGroups(container, groups);
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
        ReservedMemberNames reservedNames,
        ImmutableEquatableArray<string> requestedCollectionServiceTypes)
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

        // Decorated service entries of split registrations, by resolver method name
        var decoratedSingletonMap = new Dictionary<string, ServiceLookupEntry>(StringComparer.Ordinal);
        var decoratedScopedMap = new Dictionary<string, ServiceLookupEntry>(StringComparer.Ordinal);
        var decoratedTransientMap = new Dictionary<string, ServiceLookupEntry>(StringComparer.Ordinal);

        var hasAllInjectionFeatures = IocFeaturesHelper.HasAllInjectionFeatures(features);
        var hasOpenGenerics = false;
        var hasKeyedServices = false;

        // Member names use short type names; collisions among the registered types are detected up front
        var typeNames = new List<string>(registrations.Length * 2);
        foreach(var reg in registrations)
        {
            if(!reg.IsOpenGeneric)
            {
                typeNames.Add(GetNamingTypeName(reg.ImplementationType));
                typeNames.Add(reg.ServiceType.Name);
            }
        }

        var names = new ContainerNames(typeNames);

        foreach(var reg in registrations)
        {
            var effectiveRegistration = hasAllInjectionFeatures ? reg : FilterRegistrationForFeatures(reg, features);

            if(effectiveRegistration.IsOpenGeneric)
            {
                hasOpenGenerics = true;
                // Skip open generics for most processing but track the flag
                continue;
            }

            // A decorated registration is split like the Register output: the implementation type resolves the
            // undecorated instance, and the service type wraps that same instance with the decorators.
            var splitDecorators = effectiveRegistration.Decorators.Length > 0
                && effectiveRegistration.Instance is null
                && effectiveRegistration.ImplementationType.Name != effectiveRegistration.ServiceType.Name;

            // Pre-compute field and method names once, including IsEager flag
            var cached = CreateServiceLookupEntry(
                splitDecorators ? effectiveRegistration with { Decorators = [] } : effectiveRegistration,
                eagerResolveOptions,
                reservedNames,
                names);
            var serviceCached = cached;

            if(splitDecorators)
            {
                serviceCached = CreateDecoratedServiceLookupEntry(effectiveRegistration, cached, eagerResolveOptions, reservedNames, names);

                var decoratedMap = effectiveRegistration.Lifetime switch
                {
                    ServiceLifetime.Singleton => decoratedSingletonMap,
                    ServiceLifetime.Scoped => decoratedScopedMap,
                    _ => decoratedTransientMap
                };
                if(!decoratedMap.ContainsKey(serviceCached.ResolverMethodName))
                {
                    decoratedMap[serviceCached.ResolverMethodName] = serviceCached;
                }
            }

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
            list.Add(serviceCached);
            lastWinsLookup[key] = serviceCached;

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
            var hasDecorators = cached.Registration.Decorators.Length > 0;

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

        // Convert maps to lists (preserving insertion order via Dictionary enumeration order in .NET).
        // Decorated entries come after the undecorated instances they wrap, so they are disposed first.
        var singletons = singletonMap.Values.Select(static v => v.Cached).Concat(decoratedSingletonMap.Values).ToImmutableEquatableArray();
        var scoped = scopedMap.Values.Select(static v => v.Cached).Concat(decoratedScopedMap.Values).ToImmutableEquatableArray();
        var transients = transientMap.Values.Select(static v => v.Cached).Concat(decoratedTransientMap.Values).ToImmutableEquatableArray();

        // Collect service types with multiple registrations for IEnumerable<T> resolution, plus single-registration
        // service types that something requests as a collection (dependencies or IServiceProvider invocations).
        // Async-init services are excluded from collection resolution (only Task<T> can access them).
        var collectionServiceTypes = new List<string>();
        var collectionRegistrations = new Dictionary<string, ImmutableEquatableArray<ServiceLookupEntry>>();
        var requestedCollectionTypes = CollectRequestedCollectionServiceTypes(registrations, requestedCollectionServiceTypes);

        foreach(var kvp in serviceLookup)
        {
            // Include non-keyed service types only; a single registration needs a collection only when one is requested
            if(kvp.Key.Key is null
                && (kvp.Value.Count > 1 || requestedCollectionTypes.Contains(kvp.Key.ServiceType)))
            {
                // Filter out async-init registrations — they cannot appear in IEnumerable<T> resolvers
                var effectiveRegistrations = kvp.Value.Where(static c => !c.IsAsyncInit).ToImmutableEquatableArray();

                // Deduplicate resolver method names to count unique implementations
                var uniqueResolvers = new HashSet<string>();
                foreach(var cached in effectiveRegistrations)
                {
                    uniqueResolvers.Add(cached.ResolverMethodName);
                }

                // Generate the collection for multiple unique resolvers, or for a single one that is requested as a collection
                if(uniqueResolvers.Count > 1
                    || (uniqueResolvers.Count == 1 && requestedCollectionTypes.Contains(kvp.Key.ServiceType)))
                {
                    collectionServiceTypes.Add(kvp.Key.ServiceType);
                    collectionRegistrations[kvp.Key.ServiceType] = effectiveRegistrations;
                }
            }
        }

        var serviceLookupEntries = new ServiceLookup(serviceLookup);

        var lazyWrapperEntries = CreateLazyWrapperContainerEntries(singletons, scoped, transients, serviceLookupEntries, names);
        var funcWrapperEntries = CreateFuncWrapperContainerEntries(singletons, scoped, transients, serviceLookupEntries, names);
        var kvpWrapperEntries = CreateKvpWrapperContainerEntries(singletons, scoped, transients, serviceLookupEntries, names);

        var lazyFieldByResolver = lazyWrapperEntries
            .Where(static e => e.IsShared)
            .ToDictionary(static e => e.InnerResolverMethodName, static e => e.FieldName, StringComparer.Ordinal);
        var funcFieldByResolver = funcWrapperEntries
            .ToDictionary(static e => e.InnerResolverMethodName, static e => e.FieldName, StringComparer.Ordinal);
        var wrapperFields = new WrapperFieldLookup(
            lazyFieldByResolver,
            funcFieldByResolver,
            new HashSet<string>(lazyWrapperEntries.Where(static e => e.EmitCollectionResolver).Select(static e => e.InnerServiceTypeName), StringComparer.Ordinal),
            new HashSet<string>(funcWrapperEntries.Where(static e => e.EmitCollectionResolver).Select(static e => e.InnerServiceTypeName), StringComparer.Ordinal),
            names);

        var singletonEntries = CreateServiceContainerEntries(
            singletons,
            threadSafeStrategy,
            serviceLookupEntries,
            collectionRegistrations,
            wrapperFields);
        var scopedEntries = CreateServiceContainerEntries(
            scoped,
            threadSafeStrategy,
            serviceLookupEntries,
            collectionRegistrations,
            wrapperFields);
        var transientEntries = CreateServiceContainerEntries(
            transients,
            threadSafeStrategy,
            serviceLookupEntries,
            collectionRegistrations,
            wrapperFields);
        var collectionEntries = CreateCollectionContainerEntries(collectionRegistrations, names);
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
            lazyWrapperEntries,
            funcWrapperEntries,
            kvpWrapperEntries,
            collectionEntries);
    }

    private static ImmutableEquatableDictionary<(string ServiceType, string? Key), ContainerEntry> CreateLastWinsByServiceType(
        Dictionary<(string ServiceType, string? Key), ServiceLookupEntry> lastWinsLookup,
        ImmutableEquatableArray<ContainerEntry> singletonEntries,
        ImmutableEquatableArray<ContainerEntry> scopedEntries,
        ImmutableEquatableArray<ContainerEntry> transientEntries)
    {
        var entryByResolverMethodName = new Dictionary<string, ContainerEntry>(StringComparer.Ordinal);
        var entryByResolverAndServiceType = new Dictionary<(string ResolverMethodName, string ServiceType, string? Key), ContainerEntry>();

        foreach(var entry in singletonEntries)
            AddEntriesByLookup(entryByResolverMethodName, entryByResolverAndServiceType, entry);
        foreach(var entry in scopedEntries)
            AddEntriesByLookup(entryByResolverMethodName, entryByResolverAndServiceType, entry);
        foreach(var entry in transientEntries)
            AddEntriesByLookup(entryByResolverMethodName, entryByResolverAndServiceType, entry);

        var lastWinsByServiceType = new Dictionary<(string ServiceType, string? Key), ContainerEntry>(lastWinsLookup.Count);

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
                entry = CloneServiceEntryWithRegistration(serviceEntry, cached.Registration);
            }

            lastWinsByServiceType[kvp.Key] = entry;
        }

        return lastWinsByServiceType.ToImmutableEquatableDictionary();
    }

    private static void AddEntriesByLookup(
        Dictionary<string, ContainerEntry> entryByResolverMethodName,
        Dictionary<(string ResolverMethodName, string ServiceType, string? Key), ContainerEntry> entryByResolverAndServiceType,
        ContainerEntry entry)
    {
        if(entry is not ServiceContainerEntry serviceEntry)
        {
            return;
        }

        if(!entryByResolverMethodName.ContainsKey(serviceEntry.ResolverMethodName))
        {
            entryByResolverMethodName[serviceEntry.ResolverMethodName] = entry;
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
}
