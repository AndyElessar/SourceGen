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
        var serviceLookup = new Dictionary<ServiceTypeKey, List<ServiceLookupEntry>>();
        var lastWinsLookup = new Dictionary<ServiceTypeKey, ServiceLookupEntry>();

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
        var featureFilteredRegistrations = new List<ServiceRegistrationModel>(registrations.Length);

        // The undecorated entries are created first, so the names derived for decorated entries can avoid all of them
        var closedEntries = new List<(ServiceRegistrationModel Registration, ServiceLookupEntry Cached)>(registrations.Length);
        var hasSplitDecorators = false;

        foreach(var reg in registrations)
        {
            var effectiveRegistration = hasAllInjectionFeatures ? reg : FilterRegistrationForFeatures(reg, features);
            featureFilteredRegistrations.Add(effectiveRegistration);

            if(effectiveRegistration.IsOpenGeneric)
            {
                hasOpenGenerics = true;
                // Skip open generics for most processing but track the flag
                continue;
            }

            // A decorated registration is split like the Register output: the implementation type resolves the
            // undecorated instance, and the service type wraps that same instance with the decorators.
            var splitDecorators = effectiveRegistration.AppliesDecorators;
            hasSplitDecorators |= splitDecorators;

            // Pre-compute field and method names once, including IsEager flag
            var cached = CreateServiceLookupEntry(
                splitDecorators ? effectiveRegistration with { Decorators = [] } : effectiveRegistration,
                eagerResolveOptions,
                reservedNames,
                names);
            closedEntries.Add((effectiveRegistration, cached));
        }

        var decoratedNames = hasSplitDecorators ? new DecoratedServiceNames(closedEntries, reservedNames, names) : null;

        foreach(var (effectiveRegistration, cached) in closedEntries)
        {
            var serviceCached = cached;

            if(effectiveRegistration.AppliesDecorators)
            {
                serviceCached = CreateDecoratedServiceLookupEntry(effectiveRegistration, cached, eagerResolveOptions, reservedNames, decoratedNames!);

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

            var key = new ServiceTypeKey(effectiveRegistration.ServiceType.Name, effectiveRegistration.Key);
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

            // Also add implementation type as a service type (for self-registration), unless the service type has an instance
            // of its own from a generic factory: the implementation type resolves its own registration
            if(effectiveRegistration.IsServiceTypeAlias && !effectiveRegistration.HasOwnFactoryInstance)
            {
                var implKey = new ServiceTypeKey(effectiveRegistration.ImplementationType.Name, effectiveRegistration.Key);
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
        var demand = WrapperDemand.Scan(featureFilteredRegistrations);
        var requestedCollectionTypes = demand.CollectionServiceTypes;
        requestedCollectionTypes.UnionWith(requestedCollectionServiceTypes);

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

        var lazyWrapperEntries = CreateLazyWrapperContainerEntries(demand.LazyServiceTypes, serviceLookupEntries, names);
        var funcWrapperEntries = CreateFuncWrapperContainerEntries(demand.GetParameterlessFuncServiceTypes(), serviceLookupEntries, names);
        var kvpWrapperEntries = CreateKvpWrapperContainerEntries(demand.KvpPairs, serviceLookupEntries, names);

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
            names,
            CreateProjectedTasks(lastWinsLookup, serviceLookup, lazyWrapperEntries, funcWrapperEntries, reservedNames, names));

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

    /// <summary>
    /// The member names generated for the container's services and Lazy/Func wrapper fields, so derived names can avoid them.
    /// Only the base names are stored; methods and fields derived from them (<c>Create…</c>, <c>…Async</c>, <c>Init…Async</c>,
    /// <c>…Lock</c>, …) are matched by stripping the derivation from the checked name, without building candidate names.
    /// </summary>
    private sealed class ServiceMemberNames
    {
        private static readonly string[] SynchronizationFieldSuffixes = ["Lock", "Semaphore", "SpinLock"];

        private readonly HashSet<string> resolverNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> fieldNames = new(StringComparer.Ordinal);

        public ServiceMemberNames()
        {
        }

        public ServiceMemberNames(
            Dictionary<ServiceTypeKey, List<ServiceLookupEntry>> serviceLookup,
            ImmutableEquatableArray<LazyWrapperContainerEntry> lazyWrapperEntries,
            ImmutableEquatableArray<FuncWrapperContainerEntry> funcWrapperEntries)
        {
            foreach(var registrations in serviceLookup.Values)
            {
                foreach(var cached in registrations)
                {
                    Add(cached);
                }
            }

            foreach(var entry in lazyWrapperEntries)
            {
                fieldNames.Add(entry.FieldName);
            }

            foreach(var entry in funcWrapperEntries)
            {
                fieldNames.Add(entry.FieldName);
            }
        }

        public void Add(ServiceLookupEntry cached)
        {
            resolverNames.Add(cached.ResolverMethodName);
            if(cached.FieldName is { } fieldName)
            {
                fieldNames.Add(fieldName);
            }
        }

        public void AddField(string fieldName) => fieldNames.Add(fieldName);

        public void AddMethod(string methodName) => resolverNames.Add(methodName);

        /// <summary>
        /// Gets whether <paramref name="fieldName"/> is a generated field or one of its synchronization fields.
        /// </summary>
        public bool ContainsField(string fieldName)
        {
            if(fieldNames.Contains(fieldName))
                return true;

            foreach(var suffix in SynchronizationFieldSuffixes)
            {
                if(fieldName.Length > suffix.Length
                    && fieldName.EndsWith(suffix, StringComparison.Ordinal)
                    && fieldNames.Contains(fieldName[..^suffix.Length]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets whether <paramref name="methodName"/> is a generated resolver or a method derived from one
        /// (see <see cref="GetCreateMethodName"/>, <see cref="GetAsyncResolverMethodName"/>,
        /// <see cref="GetAsyncCreateMethodName"/> and <see cref="GetAsyncInitMethodName"/>).
        /// </summary>
        public bool ContainsMethod(string methodName)
        {
            if(resolverNames.Contains(methodName))
                return true;

            // {M}Async
            var isAsync = methodName.Length > 5 && methodName.EndsWith("Async", StringComparison.Ordinal);
            var syncName = isAsync ? methodName[..^5] : methodName;
            if(isAsync && resolverNames.Contains(syncName))
                return true;

            // Create{X}[Async] and Init{X}Async derive from Get{X}
            if(syncName.Length > 6 && syncName.StartsWith("Create", StringComparison.Ordinal) && resolverNames.Contains("Get" + syncName[6..]))
                return true;
            if(isAsync && syncName.Length > 4 && syncName.StartsWith("Init", StringComparison.Ordinal) && resolverNames.Contains("Get" + syncName[4..]))
                return true;

            // {M}_Create[Async] and {M}_InitAsync derive from any other method M
            if(syncName.Length > 7 && syncName.EndsWith("_Create", StringComparison.Ordinal) && resolverNames.Contains(syncName[..^7]))
                return true;
            return isAsync && syncName.Length > 5 && syncName.EndsWith("_Init", StringComparison.Ordinal) && resolverNames.Contains(syncName[..^5]);
        }
    }

    /// <summary>
    /// Names of decorated service entries (<c>{inner}_As_{ServiceType}</c>), unique among the members generated for the
    /// undecorated entries and the other decorated entries. The same inner entry and service type always get the same names.
    /// </summary>
    private sealed class DecoratedServiceNames
    {
        private readonly ServiceMemberNames memberNames = new();
        private readonly Dictionary<(string InnerResolverMethodName, string ServiceType), (string MethodName, string FieldName)> assignedNames = [];
        private readonly ReservedMemberNames reservedNames;
        private readonly ContainerNames names;

        public DecoratedServiceNames(
            List<(ServiceRegistrationModel Registration, ServiceLookupEntry Cached)> undecoratedEntries,
            ReservedMemberNames reservedNames,
            ContainerNames names)
        {
            this.reservedNames = reservedNames;
            this.names = names;

            foreach(var (_, cached) in undecoratedEntries)
            {
                memberNames.Add(cached);
            }
        }

        public (string MethodName, string FieldName) Get(ServiceLookupEntry inner, string serviceType)
        {
            var key = (inner.ResolverMethodName, serviceType);
            if(assignedNames.TryGetValue(key, out var assigned))
                return assigned;

            var safeServiceType = names.Get(serviceType);
            var methodName = $"{inner.ResolverMethodName}_As_{safeServiceType}";
            var fieldName = $"{inner.FieldName}_As_{safeServiceType}";

            while(reservedNames.IsReservedResolverName(methodName) || memberNames.ContainsMethod(methodName))
            {
                methodName = $"{methodName}_Resolve";
            }

            while(reservedNames.IsReservedFieldName(fieldName) || memberNames.ContainsField(fieldName))
            {
                fieldName = $"{fieldName}_Resolve";
            }

            memberNames.AddMethod(methodName);
            memberNames.AddField(fieldName);
            assigned = (methodName, fieldName);
            assignedNames[key] = assigned;
            return assigned;
        }
    }

    /// <summary>
    /// Creates the cached <c>Task&lt;ServiceType&gt;</c> projections of singleton/scoped async-init services: one for each
    /// service type that resolves to the service but differs from its resolver's result type (<c>Task&lt;T&gt;</c> is invariant).
    /// </summary>
    private static Dictionary<string, ImmutableEquatableArray<ProjectedTask>> CreateProjectedTasks(
        Dictionary<ServiceTypeKey, ServiceLookupEntry> lastWinsLookup,
        Dictionary<ServiceTypeKey, List<ServiceLookupEntry>> serviceLookup,
        ImmutableEquatableArray<LazyWrapperContainerEntry> lazyWrapperEntries,
        ImmutableEquatableArray<FuncWrapperContainerEntry> funcWrapperEntries,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        var projectedTasksByResolver = new Dictionary<string, List<ProjectedTask>>(StringComparer.Ordinal);

        // Built only when a projection is needed: most containers have no async-init service aliases
        ServiceMemberNames? serviceMemberNames = null;

        foreach(var kvp in lastWinsLookup)
        {
            var cached = kvp.Value;
            var serviceType = kvp.Key.ServiceType;

            if(!cached.IsAsyncInit
                || cached.FieldName is null
                || cached.Registration.Lifetime == ServiceLifetime.Transient
                || serviceType == GetResolvedTypeName(cached.Registration))
            {
                continue;
            }

            if(!projectedTasksByResolver.TryGetValue(cached.ResolverMethodName, out var projectedTasks))
            {
                projectedTasks = [];
                projectedTasksByResolver[cached.ResolverMethodName] = projectedTasks;
            }

            if(ContainsProjectedTask(projectedTasks, serviceType))
            {
                continue;
            }

            var safeServiceType = names.Get(serviceType);
            var fieldName = $"{cached.FieldName}Task_As_{safeServiceType}";
            var methodName = $"{cached.ResolverMethodName}Async_As_{safeServiceType}";

            // Avoid user/infrastructure members and the names generated for other services (e.g. a type named FooTask_As_IFoo)
            serviceMemberNames ??= new ServiceMemberNames(serviceLookup, lazyWrapperEntries, funcWrapperEntries);

            while(reservedNames.IsReservedFieldName(fieldName) || serviceMemberNames.ContainsField(fieldName))
            {
                fieldName = $"{fieldName}_Resolve";
            }

            while(reservedNames.IsReservedResolverName(methodName) || serviceMemberNames.ContainsMethod(methodName))
            {
                methodName = $"{methodName}_Resolve";
            }

            serviceMemberNames.AddField(fieldName);
            serviceMemberNames.AddMethod(methodName);
            projectedTasks.Add(new ProjectedTask(serviceType, fieldName, methodName));
        }

        var result = new Dictionary<string, ImmutableEquatableArray<ProjectedTask>>(projectedTasksByResolver.Count, StringComparer.Ordinal);
        foreach(var kvp in projectedTasksByResolver)
        {
            result[kvp.Key] = kvp.Value.ToImmutableEquatableArray();
        }

        return result;

        static bool ContainsProjectedTask(List<ProjectedTask> projectedTasks, string serviceType)
        {
            foreach(var projected in projectedTasks)
            {
                if(projected.ServiceType == serviceType)
                    return true;
            }

            return false;
        }
    }

    private static ImmutableEquatableDictionary<ServiceTypeKey, ContainerEntry> CreateLastWinsByServiceType(
        Dictionary<ServiceTypeKey, ServiceLookupEntry> lastWinsLookup,
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

        var lastWinsByServiceType = new Dictionary<ServiceTypeKey, ContainerEntry>(lastWinsLookup.Count);

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
