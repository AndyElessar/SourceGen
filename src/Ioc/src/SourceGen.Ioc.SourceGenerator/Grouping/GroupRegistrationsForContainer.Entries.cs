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
        List<KeyedCollection>? keyedCollections,
        Dictionary<ServiceTypeKey, CollectionTypes>? explicitCollectionTypes,
        ContainerNames names)
    {
        var entries = new List<ContainerEntry>(collectionRegistrations.Count + (keyedCollections?.Count ?? 0));

        foreach(var kvp in collectionRegistrations)
        {
            var elementResolvers = CreateCollectionElementResolvers(kvp.Value);
            if(elementResolvers.Length == 0)
                continue;

            entries.Add(new CollectionContainerEntry(
                kvp.Key,
                names.ArrayResolver(kvp.Key),
                elementResolvers,
                ExplicitTypes: GetExplicitCollectionTypes(explicitCollectionTypes, kvp.Key, null)));
        }

        if(keyedCollections is not null)
        {
            foreach(var keyedCollection in keyedCollections)
            {
                entries.Add(new CollectionContainerEntry(
                    keyedCollection.ServiceType,
                    keyedCollection.ArrayMethodName,
                    CreateCollectionElementResolvers(keyedCollection.Registrations),
                    keyedCollection.Key,
                    AllKeys: keyedCollection.Key is null,
                    ExplicitTypes: GetExplicitCollectionTypes(explicitCollectionTypes, keyedCollection.ServiceType, keyedCollection.Key ?? KeyedServiceAnyKey)));
            }
        }

        return entries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Gets the elements of a collection resolver in registration order, each unique registration once: its <c>Instance</c>
    /// expression, or a call of its resolver method.
    /// </summary>
    private static ImmutableEquatableArray<ResolvedDependency> CreateCollectionElementResolvers(IReadOnlyList<ServiceLookupEntry> registrations)
    {
        if(registrations.Count == 1)
            return [CreateDirectDependency(registrations[0])];

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

    private static void AddKeyedEntry(ref Dictionary<string, List<ServiceLookupEntry>>? keyedEntriesByServiceType, string serviceType, ServiceLookupEntry cached)
    {
        keyedEntriesByServiceType ??= new(TypeNameComparer.Instance);
        if(!keyedEntriesByServiceType.TryGetValue(serviceType, out var entries))
        {
            entries = [];
            keyedEntriesByServiceType[serviceType] = entries;
        }

        entries.Add(cached);
    }

    /// <summary>
    /// Records a collection type (<c>IEnumerable&lt;T&gt;</c>, <c>T[]</c>, …) registered explicitly as a service type, so the
    /// generated collection of <c>T</c> under the same key does not register that type: like <c>MS.DI</c>, which takes the
    /// exact registration of <c>IEnumerable&lt;T&gt;</c> before it builds a collection, the explicit registration wins.
    /// </summary>
    private static void AddExplicitCollectionType(ref Dictionary<ServiceTypeKey, CollectionTypes>? explicitCollectionTypes, CollectionWrapperTypeData collectionType, string? key)
    {
        var type = collectionType.WrapperKind switch
        {
            WrapperKind.Enumerable => CollectionTypes.Enumerable,
            WrapperKind.ReadOnlyCollection => CollectionTypes.ReadOnlyCollection,
            WrapperKind.Collection => CollectionTypes.Collection,
            WrapperKind.ReadOnlyList => CollectionTypes.ReadOnlyList,
            WrapperKind.List => CollectionTypes.List,
            WrapperKind.Array => CollectionTypes.Array,
            _ => CollectionTypes.None,
        };

        if(type == CollectionTypes.None)
            return;

        // Every expression of KeyedService.AnyKey is the same key
        var lookupKey = new ServiceTypeKey(collectionType.ElementType.Name, key is not null && IsAnyKeyExpression(key) ? KeyedServiceAnyKey : key);
        explicitCollectionTypes ??= [];
        explicitCollectionTypes[lookupKey] = explicitCollectionTypes.TryGetValue(lookupKey, out var types) ? types | type : type;
    }

    /// <param name="key">The collection's key expression, <c>KeyedServiceAnyKey</c> for the collection of all keyed services, or <see langword="null"/> for a non-keyed collection.</param>
    private static CollectionTypes GetExplicitCollectionTypes(Dictionary<ServiceTypeKey, CollectionTypes>? explicitCollectionTypes, string serviceType, string? key) =>
        explicitCollectionTypes is not null && explicitCollectionTypes.TryGetValue(new(serviceType, key), out var types) ? types : CollectionTypes.None;

    /// <summary>
    /// Creates the keyed collection resolvers, without demand detection (like the non-keyed collections of several
    /// registrations): one for each key of a service type with two or more unique registrations under that key (a key with
    /// one registration is served by the single-service collection fallback), and one <c>KeyedService.AnyKey</c> collection
    /// of all keyed registrations of each keyed service type, in registration order, like <c>MS.DI</c>. Async-init
    /// registrations and registrations under <c>KeyedService.AnyKey</c> itself are excluded. An <c>AnyKey</c> collection
    /// whose <c>IEnumerable&lt;T&gt;</c> is registered explicitly under <c>KeyedService.AnyKey</c> is not generated.
    /// </summary>
    private static List<KeyedCollection> CreateKeyedCollections(
        Dictionary<ServiceTypeKey, List<ServiceLookupEntry>> serviceLookup,
        Dictionary<string, List<ServiceLookupEntry>> keyedEntriesByServiceType,
        Dictionary<ServiceTypeKey, CollectionTypes>? explicitCollectionTypes,
        List<string> collectionServiceTypes,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        var keyedCollections = new List<KeyedCollection>(keyedEntriesByServiceType.Count);
        KeyedArrayMethodNames? methodNames = null;

        foreach(var kvp in serviceLookup)
        {
            // Most keys have a single registration: checked before anything is allocated
            if(kvp.Value.Count < 2 || kvp.Key.Key is not { } key || IsAnyKeyExpression(key))
                continue;

            var registrations = WithoutAsyncInit(kvp.Value);
            if(!HasTwoUniqueRegistrations(registrations))
                continue;

            methodNames ??= new(serviceLookup, collectionServiceTypes, reservedNames, names);
            var methodName = methodNames.GetUnique(names.KeyedArrayResolver(kvp.Key.ServiceType, GetSafeIdentifier(key)), key);
            keyedCollections.Add(new KeyedCollection(kvp.Key.ServiceType, key, methodName, registrations));
        }

        foreach(var kvp in keyedEntriesByServiceType)
        {
            var registrations = kvp.Value;
            foreach(var cached in registrations)
            {
                if(cached.IsAsyncInit || IsAnyKeyExpression(cached.Registration.Key!))
                {
                    registrations = [.. registrations.Where(static c => !c.IsAsyncInit && !IsAnyKeyExpression(c.Registration.Key!))];
                    break;
                }
            }

            if(registrations.Count == 0
                || (GetExplicitCollectionTypes(explicitCollectionTypes, kvp.Key, KeyedServiceAnyKey) & CollectionTypes.Enumerable) != 0)
            {
                continue;
            }

            // A single registration is written inline, without array method (see CollectionContainerEntry)
            var methodName = HasTwoUniqueRegistrations(registrations)
                ? (methodNames ??= new(serviceLookup, collectionServiceTypes, reservedNames, names)).GetUnique(names.KeyedArrayResolver(kvp.Key, "AnyKey"), KeyedServiceAnyKey)
                : null;
            keyedCollections.Add(new KeyedCollection(kvp.Key, null, methodName, registrations));
        }

        return keyedCollections;
    }

    private static List<ServiceLookupEntry> WithoutAsyncInit(List<ServiceLookupEntry> registrations)
    {
        foreach(var cached in registrations)
        {
            if(cached.IsAsyncInit)
                return [.. registrations.Where(static c => !c.IsAsyncInit)];
        }

        return registrations;
    }

    /// <summary>Gets whether the registrations contain two unique ones (see <see cref="CreateCollectionElementResolvers"/>).</summary>
    private static bool HasTwoUniqueRegistrations(List<ServiceLookupEntry> registrations)
    {
        if(registrations.Count < 2)
            return false;

        var first = registrations[0];
        var firstUnique = first.Registration.Instance ?? first.ResolverMethodName;
        for(var i = 1; i < registrations.Count; i++)
        {
            var cached = registrations[i];
            if(!string.Equals(cached.Registration.Key, first.Registration.Key, StringComparison.Ordinal)
                || !string.Equals(cached.Registration.Instance ?? cached.ResolverMethodName, firstUnique, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Unique names of keyed array methods (<c>GetAll{T}Array_{key}</c>). A name can only clash with another keyed array
    /// method, a member name of the user, a resolver of a type named <c>All…</c>, or a non-keyed array method when the key's
    /// identifier ends with <c>Array</c>; the sets for the last two are built only when such a name occurs.
    /// </summary>
    private sealed class KeyedArrayMethodNames(
        Dictionary<ServiceTypeKey, List<ServiceLookupEntry>> serviceLookup,
        List<string> collectionServiceTypes,
        ReservedMemberNames reservedNames,
        ContainerNames names)
    {
        private HashSet<string>? generatedNames;
        private HashSet<string>? resolverNames;
        private HashSet<string>? arrayNames;

        public string GetUnique(string methodName, string key)
        {
            // Different keys can have the same identifier ("a-b" and "a.b"): the hash of the key tells them apart
            if(IsTaken(methodName))
            {
                methodName = ContainerNames.AppendHash(methodName, key);
            }

            while(IsTaken(methodName))
            {
                methodName = $"{methodName}_Resolve";
            }

            (generatedNames ??= new(StringComparer.Ordinal)).Add(methodName);
            return methodName;
        }

        private bool IsTaken(string methodName)
        {
            if(generatedNames?.Contains(methodName) == true || reservedNames.IsReservedResolverName(methodName))
                return true;

            if(resolverNames is null)
            {
                resolverNames = new HashSet<string>(StringComparer.Ordinal);
                foreach(var registrations in serviceLookup.Values)
                {
                    foreach(var cached in registrations)
                    {
                        if(cached.ResolverMethodName.StartsWith("GetAll", StringComparison.Ordinal))
                            resolverNames.Add(cached.ResolverMethodName);
                    }
                }
            }

            if(resolverNames.Contains(methodName))
                return true;

            if(!methodName.EndsWith("Array", StringComparison.Ordinal))
                return false;

            if(arrayNames is null)
            {
                arrayNames = new HashSet<string>(StringComparer.Ordinal);
                foreach(var serviceType in collectionServiceTypes)
                {
                    arrayNames.Add(names.ArrayResolver(serviceType));
                }
            }

            return arrayNames.Contains(methodName);
        }
    }

    private static bool IsAnyKeyExpression(string key) =>
        key.EndsWith("KeyedService.AnyKey", StringComparison.Ordinal);

    /// <summary>
    /// A keyed collection resolver of <paramref name="ServiceType"/>: the registrations under <paramref name="Key"/>, or all
    /// keyed registrations (<c>KeyedService.AnyKey</c>) when <paramref name="Key"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="ArrayMethodName">The array method, or <see langword="null"/> for an <c>AnyKey</c> collection of a single registration, which is written inline.</param>
    private sealed record class KeyedCollection(
        string ServiceType,
        string? Key,
        string? ArrayMethodName,
        List<ServiceLookupEntry> Registrations);

    /// <summary>The collection types a collection resolver registers.</summary>
    [Flags]
    private enum CollectionTypes
    {
        None = 0,
        Enumerable = 1,
        ReadOnlyCollection = 2,
        Collection = 4,
        ReadOnlyList = 8,
        List = 16,
        Array = 32,
    }
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
