namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static RegisterOutputModel? GroupRegistrationsForRegister(
        ImmutableEquatableArray<ServiceRegistrationWithTags> registrations,
        string rootNamespace,
        string assemblyName,
        string? customIocName,
        IocFeatures features)
    {
        if((features & IocFeatures.Register) == 0 || registrations.Length == 0)
            return null;

        var methodBaseName = !string.IsNullOrWhiteSpace(customIocName)
            ? GetSafeIdentifier(customIocName!)
            : GetSafeIdentifier(assemblyName);

        var canonicalTagsCache = new Dictionary<ImmutableEquatableArray<string>, ImmutableEquatableArray<string>>();
        foreach(var regWithTags in registrations)
        {
            var tags = regWithTags.Tags;
            if(canonicalTagsCache.ContainsKey(tags))
            {
                continue;
            }

            if(tags.Length > 0)
            {
                var sortedTags = tags.OrderBy(static t => t, StringComparer.Ordinal).ToImmutableEquatableArray();
                canonicalTagsCache[tags] = sortedTags;
            }
            else
            {
                canonicalTagsCache[tags] = [];
            }
        }

        var shouldFilterInjection = !IocFeaturesHelper.HasAllInjectionFeatures(features);
        var groupedRegistrations = new Dictionary<ImmutableEquatableArray<string>, List<RegisterEntry>>();

        foreach(var regWithTags in registrations)
        {
            var registration = shouldFilterInjection
                ? FilterRegistrationForFeatures(regWithTags.Registration, features)
                : regWithTags.Registration;
            var tagKey = canonicalTagsCache[regWithTags.Tags];

            if(!groupedRegistrations.TryGetValue(tagKey, out var group))
            {
                group = [];
                groupedRegistrations[tagKey] = group;
            }

            var entry = CreateRegisterEntry(registration);
            if(entry is null)
            {
                continue;
            }

            group.Add(entry);
        }

        var explicitWrappers = CollectExplicitWrapperRegistrations(groupedRegistrations);

        var demand = WrapperDemand.Scan(registrations.Select(static r => r.Registration));
        var lazyEntriesToRegister = CollectLazyEntries(registrations, demand.LazyServiceTypes);
        var funcEntriesToRegister = CollectFuncEntries(registrations, demand.FuncTypes, features);
        if(explicitWrappers is not null)
        {
            // Like MS.DI (and the Container output), an explicit Lazy<T> / Func<T> registration replaces the generated wrappers of
            // T's registrations it is always registered with: written after it, a generated wrapper would win (last wins)
            lazyEntriesToRegister.RemoveAll(entry => IsCoveredByExplicitRegistration(
                explicitWrappers, new($"global::System.Lazy<{entry.InnerServiceTypeName}>", entry.Key), canonicalTagsCache[entry.Tags]));
            funcEntriesToRegister.RemoveAll(entry => IsCoveredByExplicitRegistration(
                explicitWrappers, new(entry.FuncServiceTypeName, entry.Registration.Key), canonicalTagsCache[entry.Tags]));
        }

        var lazyByTagKey = GroupLazyEntriesByTagKey(lazyEntriesToRegister, canonicalTagsCache);
        var funcByTagKey = GroupFuncEntriesByTagKey(funcEntriesToRegister, canonicalTagsCache);
        var kvpByTagKey = GroupKvpEntriesByTagKey(CollectKeyValuePairEntries(registrations, demand.KvpPairs), canonicalTagsCache);

        // Keyed by (type, key): the same service type can be async-init under one key and synchronous under another
        var asyncInitServiceSet = new HashSet<AsyncInitService>();
        foreach(var group in groupedRegistrations.Values)
        {
            foreach(var entry in group)
            {
                var registration = entry.Registration;
                if(!registration.IsAsyncInit)
                    continue;

                asyncInitServiceSet.Add(new(registration.ServiceType.Name, registration.Key));

                // Async decorators make only the decorated service type async-init, not the implementation type
                if(registration.HasAsyncInjection)
                    asyncInitServiceSet.Add(new(registration.ImplementationType.Name, registration.Key));
            }
        }

        var asyncInitServices = asyncInitServiceSet.Count > 0
            ? asyncInitServiceSet.ToImmutableEquatableSet()
            : null;

        var tagGroups = groupedRegistrations
            .OrderBy(static kvp => kvp.Key, TagArrayComparer.Instance)
            .Select(kvp =>
            {
                var tags = kvp.Key;

                lazyByTagKey.TryGetValue(tags, out var lazyEntries);
                funcByTagKey.TryGetValue(tags, out var funcEntries);
                kvpByTagKey.TryGetValue(tags, out var kvpEntries);

                return new RegisterTagGroup(
                    tags,
                    kvp.Value.ToImmutableEquatableArray(),
                    lazyEntries is null ? [] : lazyEntries.ToImmutableEquatableArray(),
                    funcEntries is null ? [] : funcEntries.ToImmutableEquatableArray(),
                    kvpEntries is null ? [] : kvpEntries.ToImmutableEquatableArray(),
                    GetExplicitWrapperServices(explicitWrappers, tags));
            })
            .ToImmutableEquatableArray();

        return new RegisterOutputModel(
            methodBaseName,
            rootNamespace,
            assemblyName,
            tagGroups,
            asyncInitServices);
    }

    /// <summary>
    /// Collects the registrations of wrapper and collection service types (<c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c>,
    /// <c>KeyValuePair&lt;K, V&gt;</c>, the dictionary types, and the collection types other than <c>IEnumerable&lt;T&gt;</c>)
    /// with the canonical tags of each, or returns <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// <c>IEnumerable&lt;T&gt;</c> is left out: <c>GetServices&lt;T&gt;()</c> already resolves its explicit registration in
    /// <c>MS.DI</c>. <c>Task&lt;T&gt;</c> keeps its async-init resolution. An async-init registration (registered as
    /// <c>Task&lt;T&gt;</c> only), an open generic registration and a registration under <c>KeyedService.AnyKey</c> (which
    /// <c>MS.DI</c> does not resolve as a single service) do not count.
    /// </remarks>
    private static List<(ServiceTypeKey Service, ImmutableEquatableArray<string> Tags)>? CollectExplicitWrapperRegistrations(
        Dictionary<ImmutableEquatableArray<string>, List<RegisterEntry>> groupedRegistrations)
    {
        List<(ServiceTypeKey Service, ImmutableEquatableArray<string> Tags)>? explicitWrappers = null;
        foreach(var group in groupedRegistrations)
        {
            foreach(var entry in group.Value)
            {
                var registration = entry.Registration;
                if(registration.ServiceType is not WrapperTypeData or TaskTypeData or EnumerableTypeData
                    || registration.IsOpenGeneric
                    || registration.IsAsyncInit
                    || entry is FactoryRegisterEntry { CanGenerate: false }
                    || (registration.Key is { } key && IsAnyKeyExpression(key)))
                {
                    continue;
                }

                (explicitWrappers ??= []).Add((new(registration.ServiceType.Name, registration.Key), group.Key));
            }
        }

        return explicitWrappers;
    }

    /// <summary>
    /// Gets whether <paramref name="service"/> is registered explicitly whenever a registration with
    /// <paramref name="tags"/> is (see <see cref="IsAlwaysRegisteredWith"/>).
    /// </summary>
    private static bool IsCoveredByExplicitRegistration(
        List<(ServiceTypeKey Service, ImmutableEquatableArray<string> Tags)> explicitWrappers,
        ServiceTypeKey service,
        ImmutableEquatableArray<string> tags)
    {
        foreach(var explicitWrapper in explicitWrappers)
        {
            if(explicitWrapper.Service.Equals(service) && IsAlwaysRegisteredWith(explicitWrapper.Tags, tags))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the explicitly registered wrapper and collection types that the dependencies of the tag group with
    /// <paramref name="tags"/> resolve: those registered whenever the group is.
    /// </summary>
    private static ImmutableEquatableSet<ServiceTypeKey>? GetExplicitWrapperServices(
        List<(ServiceTypeKey Service, ImmutableEquatableArray<string> Tags)>? explicitWrappers,
        ImmutableEquatableArray<string> tags)
    {
        if(explicitWrappers is null)
            return null;

        HashSet<ServiceTypeKey>? services = null;
        foreach(var explicitWrapper in explicitWrappers)
        {
            if(IsAlwaysRegisteredWith(explicitWrapper.Tags, tags))
                (services ??= []).Add(explicitWrapper.Service);
        }

        return services?.ToImmutableEquatableSet();
    }

    /// <summary>
    /// Gets whether a registration with <paramref name="registrationTags"/> is registered whenever one with
    /// <paramref name="otherTags"/> is: both untagged (untagged registrations are registered when no tags are passed), or
    /// tagged with every tag of <paramref name="otherTags"/> (a tagged registration is registered when any of its tags is passed).
    /// </summary>
    private static bool IsAlwaysRegisteredWith(ImmutableEquatableArray<string> registrationTags, ImmutableEquatableArray<string> otherTags)
    {
        if(otherTags.Length == 0)
            return registrationTags.Length == 0;

        foreach(var tag in otherTags)
        {
            if(!registrationTags.Contains(tag))
                return false;
        }

        return true;
    }

    private static Dictionary<ImmutableEquatableArray<string>, List<LazyRegistrationEntry>> GroupLazyEntriesByTagKey(
        List<LazyRegistrationEntry> entries,
        Dictionary<ImmutableEquatableArray<string>, ImmutableEquatableArray<string>> canonicalTagsCache)
    {
        var grouped = new Dictionary<ImmutableEquatableArray<string>, List<LazyRegistrationEntry>>();
        foreach(var entry in entries)
        {
            var tagKey = canonicalTagsCache[entry.Tags];
            if(!grouped.TryGetValue(tagKey, out var list))
            {
                list = [];
                grouped[tagKey] = list;
            }

            list.Add(entry);
        }

        return grouped;
    }

    private static Dictionary<ImmutableEquatableArray<string>, List<FuncRegistrationEntry>> GroupFuncEntriesByTagKey(
        List<FuncRegistrationEntry> entries,
        Dictionary<ImmutableEquatableArray<string>, ImmutableEquatableArray<string>> canonicalTagsCache)
    {
        var grouped = new Dictionary<ImmutableEquatableArray<string>, List<FuncRegistrationEntry>>();
        foreach(var entry in entries)
        {
            var tagKey = canonicalTagsCache[entry.Tags];
            if(!grouped.TryGetValue(tagKey, out var list))
            {
                list = [];
                grouped[tagKey] = list;
            }

            list.Add(entry);
        }

        return grouped;
    }

    private static Dictionary<ImmutableEquatableArray<string>, List<KvpRegistrationEntry>> GroupKvpEntriesByTagKey(
        List<KvpRegistrationEntry> entries,
        Dictionary<ImmutableEquatableArray<string>, ImmutableEquatableArray<string>> canonicalTagsCache)
    {
        var grouped = new Dictionary<ImmutableEquatableArray<string>, List<KvpRegistrationEntry>>();
        foreach(var entry in entries)
        {
            var tagKey = canonicalTagsCache[entry.Tags];
            if(!grouped.TryGetValue(tagKey, out var list))
            {
                list = [];
                grouped[tagKey] = list;
            }

            list.Add(entry);
        }

        return grouped;
    }

    private static RegisterEntry? CreateRegisterEntry(ServiceRegistrationModel registration)
    {
        // MS.DI constructs the implementation of an open generic registration with a public constructor, so neither an instance,
        // a type whose closed registrations call the factory (SGIOC042), nor an implementation type it cannot activate is
        // registered open; only their closed registrations are
        if(registration.IsOpenGeneric)
        {
            return registration.Instance is null
                && !IsCreatedByFactory(registration)
                && registration.ImplementationType.Constructibility is Constructibility.Activatable
                ? new SimpleRegisterEntry(registration)
                : null;
        }

        // An instance takes precedence over any Factory, like the container output
        if(registration.Instance is not null)
        {
            return registration.Lifetime == ServiceLifetime.Singleton ? new InstanceRegisterEntry(registration) : null;
        }

        if(registration.Factory is not null && !SharesFactoryInstance(registration))
        {
            return new FactoryRegisterEntry(registration);
        }

        if(registration.AppliesDecorators)
        {
            return new DecoratorRegisterEntry(registration);
        }

        if(registration.IsServiceTypeAlias)
        {
            return new ForwardingRegisterEntry(registration);
        }

        if(NeedsInjectionConstruction(registration))
        {
            return registration.HasAsyncInjection
                ? new AsyncInjectionRegisterEntry(registration)
                : new InjectionRegisterEntry(registration);
        }

        return new SimpleRegisterEntry(registration);
    }

    /// <summary>
    /// Gets whether the closed registrations of an open generic registration call its factory, which MS.DI would bypass for
    /// the closed types the open registration creates (see <see cref="IsOpenRegistrationCreatedByFactory"/>).
    /// </summary>
    private static bool IsCreatedByFactory(ServiceRegistrationModel registration) =>
        registration.Factory is { } factory
        && IsOpenRegistrationCreatedByFactory(factory, registration.ServiceType, registration.ImplementationType);

    /// <summary>
    /// Gets whether a factory registration's service type shares the instance that the factory creates for the implementation
    /// type's registration (forwarded, or wrapped with decorators) instead of calling the factory again. A generic factory
    /// (<c>HasOwnFactoryInstance</c>) and an async-init registration call the factory for the service type itself.
    /// </summary>
    private static bool SharesFactoryInstance(ServiceRegistrationModel registration) =>
        registration.IsServiceTypeAlias
        && !registration.HasOwnFactoryInstance
        && !registration.HasAsyncInjection;

    /// <summary>
    /// Gets whether a service type alias registers an instance of its own instead of forwarding to the implementation type:
    /// <c>HasOwnServiceInstance</c>, or an async-init factory registration, which the Register output cannot forward (the
    /// implementation type's factory registration is not a <c>Task&lt;T&gt;</c>). The instance is registered under the hidden
    /// key (see <c>GetDecoratedServiceKey</c>), or the registration's own key, which <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c>
    /// wrappers resolve.
    /// </summary>
    private static bool RegistersOwnServiceInstance(ServiceRegistrationModel registration) =>
        registration.HasOwnServiceInstance
        || (registration.Factory is not null && !registration.IsOpenGeneric && registration.IsServiceTypeAlias && registration.HasAsyncInjection);

    /// <summary>
    /// Gets whether the implementation type is constructed in a lambda instead of by <c>services.Add{Lifetime}&lt;TService, TImpl&gt;()</c>:
    /// injection members, an <c>[IocInject]</c> constructor, or constructor parameters that MS.DI cannot resolve by itself.
    /// </summary>
    private static bool NeedsInjectionConstruction(ServiceRegistrationModel registration) =>
        registration.InjectionMembers.Length > 0
        // MS.DI calls public constructors only, so a type whose selected constructor is not public is constructed by generated code
        || registration.ImplementationType.Constructibility is Constructibility.GeneratedCodeOnly
        || registration.ImplementationType.HasInjectConstructor
        || registration.ImplementationType.ConstructorParameters?.Any(static p =>
            p.HasInjectAttribute ||
            p.Type.NeedsWrapperResolution ||
            (p.IsNullable && p.Type is LazyTypeData { InstanceType: not WrapperTypeData } or FuncTypeData { ReturnType: not WrapperTypeData }) ||
            p.HasDefaultValue) == true;

    private sealed class TagArrayComparer : IComparer<ImmutableEquatableArray<string>>
    {
        public static readonly TagArrayComparer Instance = new();

        public int Compare(ImmutableEquatableArray<string> a, ImmutableEquatableArray<string> b)
        {
            var minLength = Math.Min(a.Length, b.Length);
            for(var i = 0; i < minLength; i++)
            {
                var cmp = StringComparer.Ordinal.Compare(a[i], b[i]);
                if(cmp != 0)
                    return cmp;
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}