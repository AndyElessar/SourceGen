using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ServiceContainerEntry CloneServiceEntryWithRegistration(
        ServiceContainerEntry entry,
        ServiceRegistrationModel registration)
    {
        return entry with { Registration = registration };
    }

    /// <summary>
    /// Creates a ServiceLookupEntry with pre-computed field and method names.
    /// Computes both names in a single pass to avoid redundant string operations.
    /// </summary>
    private static ServiceLookupEntry CreateServiceLookupEntry(
        ServiceRegistrationModel reg,
        EagerResolveOptions eagerResolveOptions,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        var (fieldName, methodName) = ComputeServiceNames(reg, names);
        return CreateServiceLookupEntry(reg, methodName, fieldName, eagerResolveOptions, reservedNames);
    }

    private static ServiceLookupEntry CreateServiceLookupEntry(
        ServiceRegistrationModel reg,
        string methodName,
        string fieldName,
        EagerResolveOptions eagerResolveOptions,
        ReservedMemberNames reservedNames)
    {
        // Avoid naming conflicts with user-declared and generated infrastructure members
        if(reservedNames.IsReservedResolverName(methodName))
        {
            methodName = $"{methodName}_Resolve";
        }

        if(reservedNames.IsReservedFieldName(fieldName))
        {
            fieldName = $"{fieldName}_Resolve";
        }

        // Determine if this registration should be eagerly resolved via EagerResolveOptions.
        // Instance registrations are inherently eager (no field caching needed).
        // Transient services are not supported for eager resolution.
        var isAsyncInit = reg.IsAsyncInit;
        var isEager = reg.Instance is null && reg.Lifetime switch
        {
            ServiceLifetime.Singleton => (eagerResolveOptions & EagerResolveOptions.Singleton) != 0,
            ServiceLifetime.Scoped => (eagerResolveOptions & EagerResolveOptions.Scoped) != 0,
            _ => false // Transient is never eager
        };

        return new ServiceLookupEntry(reg, methodName, fieldName, isAsyncInit, isEager);
    }

    /// <summary>
    /// Creates the entry resolving a decorated service type: it wraps the instance resolved by
    /// <paramref name="inner"/> (the undecorated implementation type entry) with the decorators.
    /// </summary>
    private static ServiceLookupEntry CreateDecoratedServiceLookupEntry(
        ServiceRegistrationModel reg,
        ServiceLookupEntry inner,
        EagerResolveOptions eagerResolveOptions,
        ReservedMemberNames reservedNames,
        DecoratedServiceNames decoratedNames)
    {
        var (methodName, fieldName) = decoratedNames.Get(inner, reg.ServiceType.Name);
        var entry = CreateServiceLookupEntry(reg, methodName, fieldName, eagerResolveOptions, reservedNames);

        // Transient async-init services only have a creation method (no cached resolver)
        var innerResolverMethodName = !inner.IsAsyncInit
            ? inner.ResolverMethodName
            : reg.Lifetime == ServiceLifetime.Transient
                ? GetAsyncCreateMethodName(inner.ResolverMethodName)
                : GetAsyncResolverMethodName(inner.ResolverMethodName);

        return entry with { InnerResolverMethodName = innerResolverMethodName };
    }

    private static ImmutableEquatableArray<ContainerEntry> CreateServiceContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> registrations,
        ThreadSafeStrategy threadSafeStrategy,
        ServiceLookup serviceLookup,
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        WrapperFieldLookup wrapperFields)
    {
        var entries = new List<ContainerEntry>(registrations.Length);

        foreach(var cached in registrations)
        {
            var reg = cached.Registration;

            // A decorated service entry takes the instance from its inner entry, which does the construction and injection
            var isDecoratedEntry = cached.InnerResolverMethodName is not null;
            var constructorParameters = isDecoratedEntry ? [] : ResolveConstructorParametersForContainerEntryModel(
                reg,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                allowServiceKeyAttribute: true);
            var injectionMembers = isDecoratedEntry ? [] : ResolveInjectionMembersForContainerEntryModel(
                reg.InjectionMembers,
                reg,
                serviceLookup,
                collectionRegistrations,
                wrapperFields,
                allowServiceKeyAttributeForMethods: true);
            var decorators = ResolveDecoratorsForContainerEntryModel(
                reg,
                serviceLookup,
                collectionRegistrations,
                wrapperFields);

            var entry = CreateServiceContainerEntryModel(
                cached,
                threadSafeStrategy,
                constructorParameters,
                injectionMembers,
                decorators);

            if(isDecoratedEntry && entry is ServiceContainerEntry serviceEntry)
            {
                entry = serviceEntry with { InnerResolverMethodName = cached.InnerResolverMethodName };
            }

            if(entry is AsyncContainerEntry asyncEntry
                && wrapperFields.ProjectedTasksByResolver.TryGetValue(asyncEntry.ResolverMethodName, out var projectedTasks))
            {
                entry = asyncEntry with { ProjectedTasks = projectedTasks };
            }

            entries.Add(entry);
        }

        return entries.ToImmutableEquatableArray();
    }

    private static ContainerEntry CreateServiceContainerEntryModel(
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
            if(reg.Lifetime is ServiceLifetime.Singleton or ServiceLifetime.Scoped)
            {
                return new AsyncContainerEntry(
                    reg,
                    cached.ResolverMethodName,
                    cached.FieldName!,
                    cached.IsEager,
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
            // Only eager singletons are always created in a constructor (the root's) before any other thread can
            // resolve them. Eager scoped services are created in scope constructors, but the root creates them on
            // demand, so they keep the synchronized shape.
            if(cached.IsEager && reg.Lifetime == ServiceLifetime.Singleton)
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
                cached.IsEager,
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

    private static ImmutableEquatableArray<ContainerEntry> CreateCollectionContainerEntries(
        IReadOnlyDictionary<string, ImmutableEquatableArray<ServiceLookupEntry>> collectionRegistrations,
        List<KeyedCollection> keyedCollections,
        ContainerNames names)
    {
        var entries = new List<ContainerEntry>(collectionRegistrations.Count + keyedCollections.Count);

        foreach(var kvp in collectionRegistrations)
        {
            var elementResolvers = CreateCollectionElementResolvers(kvp.Value);
            if(elementResolvers.Length == 0)
                continue;

            entries.Add(new CollectionContainerEntry(
                kvp.Key,
                names.ArrayResolver(kvp.Key),
                elementResolvers));
        }

        foreach(var keyedCollection in keyedCollections)
        {
            entries.Add(new CollectionContainerEntry(
                keyedCollection.ServiceType,
                keyedCollection.ArrayMethodName,
                CreateCollectionElementResolvers(keyedCollection.Registrations),
                keyedCollection.Key,
                AllKeys: keyedCollection.Key is null));
        }

        return entries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Gets the elements of a collection resolver in registration order, each unique registration once: its <c>Instance</c>
    /// expression, or a call of its resolver method.
    /// </summary>
    private static ImmutableEquatableArray<ResolvedDependency> CreateCollectionElementResolvers(IReadOnlyList<ServiceLookupEntry> registrations)
    {
        var elementResolvers = new List<ResolvedDependency>(registrations.Count);
        var uniqueKeys = new HashSet<(string? Key, string Resolver)>();

        foreach(var cached in registrations)
        {
            // The key keeps instances registered under several keys apart in KeyedService.AnyKey collections
            if(!uniqueKeys.Add((cached.Registration.Key, cached.Registration.Instance ?? cached.ResolverMethodName)))
                continue;

            elementResolvers.Add(CreateDirectDependency(cached));
        }

        return elementResolvers.ToImmutableEquatableArray();
    }

    private static void AddKeyedEntry(Dictionary<string, List<ServiceLookupEntry>> keyedEntriesByServiceType, string serviceType, ServiceLookupEntry cached)
    {
        if(!keyedEntriesByServiceType.TryGetValue(serviceType, out var entries))
        {
            entries = [];
            keyedEntriesByServiceType[serviceType] = entries;
        }

        entries.Add(cached);
    }

    /// <summary>
    /// Creates the keyed collection resolvers, without demand detection (like the non-keyed collections of several
    /// registrations): one for each key of a service type with two or more unique registrations under that key (a key with
    /// one registration is served by the single-service collection fallback), and one <c>KeyedService.AnyKey</c> collection
    /// of all keyed registrations of each keyed service type, in registration order, like <c>MS.DI</c>. Async-init
    /// registrations and registrations under <c>KeyedService.AnyKey</c> itself are excluded.
    /// </summary>
    private static List<KeyedCollection> CreateKeyedCollections(
        Dictionary<ServiceTypeKey, List<ServiceLookupEntry>> serviceLookup,
        Dictionary<string, List<ServiceLookupEntry>> keyedEntriesByServiceType,
        List<string> collectionServiceTypes,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        var keyedCollections = new List<KeyedCollection>();
        if(keyedEntriesByServiceType.Count == 0)
            return keyedCollections;

        // Array method names must not clash with the other generated methods
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach(var serviceType in collectionServiceTypes)
        {
            usedNames.Add(names.ArrayResolver(serviceType));
        }

        foreach(var registrations in serviceLookup.Values)
        {
            foreach(var cached in registrations)
            {
                usedNames.Add(cached.ResolverMethodName);
            }
        }

        foreach(var kvp in serviceLookup)
        {
            if(kvp.Key.Key is not { } key || IsAnyKeyExpression(key))
                continue;

            var registrations = kvp.Value.Where(static c => !c.IsAsyncInit).ToList();
            var uniqueResolvers = new HashSet<string>(StringComparer.Ordinal);
            foreach(var cached in registrations)
            {
                uniqueResolvers.Add(cached.Registration.Instance ?? cached.ResolverMethodName);
            }

            if(uniqueResolvers.Count < 2)
                continue;

            var methodName = GetUniqueKeyedArrayMethodName($"{names.ArrayResolver(kvp.Key.ServiceType)}_{GetSafeIdentifier(key)}", key, usedNames, reservedNames);
            keyedCollections.Add(new KeyedCollection(kvp.Key.ServiceType, key, methodName, registrations));
        }

        foreach(var kvp in keyedEntriesByServiceType)
        {
            var registrations = kvp.Value.Where(static c => !c.IsAsyncInit && !IsAnyKeyExpression(c.Registration.Key!)).ToList();
            if(registrations.Count == 0)
                continue;

            var methodName = GetUniqueKeyedArrayMethodName($"{names.ArrayResolver(kvp.Key)}_AnyKey", KeyedServiceAnyKey, usedNames, reservedNames);
            keyedCollections.Add(new KeyedCollection(kvp.Key, null, methodName, registrations));
        }

        return keyedCollections;
    }

    private static string GetUniqueKeyedArrayMethodName(string methodName, string key, HashSet<string> usedNames, ReservedMemberNames reservedNames)
    {
        // Different keys can have the same identifier ("a-b" and "a.b"): the hash of the key tells them apart
        if(usedNames.Contains(methodName) || reservedNames.IsReservedResolverName(methodName))
        {
            methodName = ContainerNames.AppendHash(methodName, key);
        }

        while(usedNames.Contains(methodName) || reservedNames.IsReservedResolverName(methodName))
        {
            methodName = $"{methodName}_Resolve";
        }

        usedNames.Add(methodName);
        return methodName;
    }

    private static bool IsAnyKeyExpression(string key) =>
        key.EndsWith("KeyedService.AnyKey", StringComparison.Ordinal);

    /// <summary>
    /// A keyed collection resolver of <paramref name="ServiceType"/>: the registrations under <paramref name="Key"/>, or all
    /// keyed registrations (<c>KeyedService.AnyKey</c>) when <paramref name="Key"/> is <see langword="null"/>.
    /// </summary>
    private sealed record class KeyedCollection(
        string ServiceType,
        string? Key,
        string ArrayMethodName,
        List<ServiceLookupEntry> Registrations);

    /// <param name="InnerResolverMethodName">
    /// For a decorated service entry, the method resolving the undecorated instance it wraps
    /// (<c>Task&lt;T&gt;</c> for async-init services); otherwise <see langword="null"/>.
    /// </param>
    private readonly record struct ServiceLookupEntry(
        ServiceRegistrationModel Registration,
        string ResolverMethodName,
        string? FieldName,
        bool IsAsyncInit,
        bool IsEager,
        string? InnerResolverMethodName = null);

    /// <summary>
    /// Registrations by (service type, key), with the last registration of each keyed service indexed by service type,
    /// so KeyValuePair and Dictionary dependencies find their keyed registrations without scanning every service.
    /// </summary>
    private sealed class ServiceLookup : Dictionary<ServiceTypeKey, ImmutableEquatableArray<ServiceLookupEntry>>
    {
        public ServiceLookup(Dictionary<ServiceTypeKey, List<ServiceLookupEntry>> registrations)
            : base(registrations.Count)
        {
            foreach(var kvp in registrations)
            {
                var entries = kvp.Value.ToImmutableEquatableArray();
                Add(kvp.Key, entries);

                if(kvp.Key.Key is null)
                    continue;

                if(!LastKeyedEntriesByServiceType.TryGetValue(kvp.Key.ServiceType, out var keyedEntries))
                {
                    keyedEntries = [];
                    LastKeyedEntriesByServiceType[kvp.Key.ServiceType] = keyedEntries;
                }
                keyedEntries.Add(entries[^1]);
            }
        }

        public Dictionary<string, List<ServiceLookupEntry>> LastKeyedEntriesByServiceType { get; } = new(TypeNameComparer.Instance);
    }
}
