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
        var asyncInitCoverage = CollectAsyncInitServices(groupedRegistrations);

        var demand = WrapperDemand.Scan(registrations.Select(static r => r.Registration));
        var lazyEntriesToRegister = CollectLazyEntries(registrations, demand.LazyServiceTypes);
        var funcEntriesToRegister = CollectFuncEntries(registrations, demand.FuncTypes, features);
        if(explicitWrappers is not null)
        {
            // Like MS.DI (and the Container output), an explicit Lazy<T> / Func<T> registration replaces the generated wrappers of
            // T's registrations: written after it, a generated wrapper would win (last wins)
            ApplyExplicitWrapperSuppression(
                lazyEntriesToRegister,
                explicitWrappers,
                canonicalTagsCache,
                static entry => new($"global::System.Lazy<{entry.InnerServiceTypeName}>", entry.Key),
                static entry => entry.Tags,
                static (entry, suppressingTags) => entry with { SuppressingTags = suppressingTags });
            ApplyExplicitWrapperSuppression(
                funcEntriesToRegister,
                explicitWrappers,
                canonicalTagsCache,
                static entry => new(entry.FuncServiceTypeName, entry.Registration.Key),
                static entry => entry.Tags,
                static (entry, suppressingTags) => entry with { SuppressingTags = suppressingTags });
        }

        var lazyByTagKey = GroupLazyEntriesByTagKey(lazyEntriesToRegister, canonicalTagsCache);
        var funcByTagKey = GroupFuncEntriesByTagKey(funcEntriesToRegister, canonicalTagsCache);
        var kvpByTagKey = GroupKvpEntriesByTagKey(CollectKeyValuePairEntries(registrations, demand.KvpPairs), canonicalTagsCache);

        // Only the header needs every async-init service, for the experimental AsyncInitHolder pragma
        var asyncInitServices = asyncInitCoverage is not null
            ? asyncInitCoverage.Keys.ToImmutableEquatableSet()
            : null;

        var tagGroups = groupedRegistrations
            .OrderBy(static kvp => kvp.Key, TagArrayComparer.Instance)
            .Select(kvp =>
            {
                var tags = kvp.Key;

                lazyByTagKey.TryGetValue(tags, out var lazyEntries);
                funcByTagKey.TryGetValue(tags, out var funcEntries);
                kvpByTagKey.TryGetValue(tags, out var kvpEntries);

                var (asyncInit, asyncInitFallback) = ClassifyByTags(asyncInitCoverage, tags);
                var (explicitWrapperServices, explicitWrapperFallback) = ClassifyByTags(explicitWrappers, tags);

                return new RegisterTagGroup(
                    tags,
                    kvp.Value.ToImmutableEquatableArray(),
                    lazyEntries is null ? [] : lazyEntries.ToImmutableEquatableArray(),
                    funcEntries is null ? [] : funcEntries.ToImmutableEquatableArray(),
                    kvpEntries is null ? [] : kvpEntries.ToImmutableEquatableArray(),
                    asyncInit,
                    asyncInitFallback,
                    explicitWrapperServices,
                    explicitWrapperFallback);
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
    /// The tags of the registrations of a service: whether one is untagged, and every tag of the tagged ones.
    /// </summary>
    /// <remarks>
    /// An untagged registration is registered only when no tags are passed, and a tagged one when any of its tags is passed
    /// (see <c>Register.Tags.spec.md</c>), so one call can register several tagged groups, but never an untagged and a
    /// tagged one together.
    /// </remarks>
    private sealed class TagCoverage
    {
        public bool HasUntagged { get; private set; }

        /// <summary>Gets the tags of the tagged registrations, or <see langword="null"/> for none.</summary>
        public HashSet<string>? Tags { get; private set; }

        public void Add(ImmutableEquatableArray<string> tags)
        {
            if(tags.Length == 0)
            {
                HasUntagged = true;
                return;
            }

            Tags ??= new(StringComparer.Ordinal);
            foreach(var tag in tags)
                Tags.Add(tag);
        }

        /// <summary>
        /// Gets whether one of the registrations is registered whenever a registration with <paramref name="tags"/> is: an
        /// untagged one with an untagged registration, or tagged ones that together have every tag of <paramref name="tags"/>.
        /// </summary>
        public bool IsAlwaysRegisteredWith(ImmutableEquatableArray<string> tags)
        {
            if(tags.Length == 0)
                return HasUntagged;

            if(Tags is null)
                return false;

            foreach(var tag in tags)
            {
                if(!Tags.Contains(tag))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets whether one of the registrations can be registered together with a registration with <paramref name="tags"/>,
        /// depending on the tags passed: a tagged registration with any tagged one.
        /// </summary>
        public bool MayBeRegisteredWith(ImmutableEquatableArray<string> tags) =>
            tags.Length > 0 && Tags is not null;
    }

    /// <summary>
    /// Collects, by (type, key), the tags of the registrations of wrapper and collection service types (<c>Lazy&lt;T&gt;</c>,
    /// <c>Func&lt;T&gt;</c>, <c>KeyValuePair&lt;K, V&gt;</c>, the dictionary types, and the collection types other than
    /// <c>IEnumerable&lt;T&gt;</c>), or returns <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// <c>IEnumerable&lt;T&gt;</c> is left out: <c>GetServices&lt;T&gt;()</c> already resolves its explicit registration in
    /// <c>MS.DI</c>. <c>Task&lt;T&gt;</c> keeps its async-init resolution. An async-init registration (registered as
    /// <c>Task&lt;T&gt;</c> only), an open generic registration and a registration under <c>KeyedService.AnyKey</c> (which
    /// <c>MS.DI</c> does not resolve as a single service) do not count.
    /// </remarks>
    private static Dictionary<ServiceTypeKey, TagCoverage>? CollectExplicitWrapperRegistrations(
        Dictionary<ImmutableEquatableArray<string>, List<RegisterEntry>> groupedRegistrations)
    {
        Dictionary<ServiceTypeKey, TagCoverage>? explicitWrappers = null;
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

                AddCoverage(explicitWrappers ??= [], new ServiceTypeKey(registration.ServiceType.Name, registration.Key), group.Key);
            }
        }

        return explicitWrappers;
    }

    /// <summary>
    /// Collects, by (type, key), the tags of the async-init registrations, which register <c>Task&lt;T&gt;</c> only, or
    /// returns <see langword="null"/> when there are none. Keyed by (type, key): the same service type can be async-init
    /// under one key and synchronous under another.
    /// </summary>
    private static Dictionary<AsyncInitService, TagCoverage>? CollectAsyncInitServices(
        Dictionary<ImmutableEquatableArray<string>, List<RegisterEntry>> groupedRegistrations)
    {
        Dictionary<AsyncInitService, TagCoverage>? asyncInitServices = null;
        foreach(var group in groupedRegistrations)
        {
            foreach(var entry in group.Value)
            {
                var registration = entry.Registration;
                if(!registration.IsAsyncInit)
                    continue;

                AddCoverage(asyncInitServices ??= [], new AsyncInitService(registration.ServiceType.Name, registration.Key), group.Key);

                // Async decorators make only the decorated service type async-init, not the implementation type
                if(registration.HasAsyncInjection)
                    AddCoverage(asyncInitServices, new AsyncInitService(registration.ImplementationType.Name, registration.Key), group.Key);
            }
        }

        return asyncInitServices;
    }

    private static void AddCoverage<TKey>(Dictionary<TKey, TagCoverage> coverage, TKey key, ImmutableEquatableArray<string> tags)
        where TKey : notnull
    {
        if(!coverage.TryGetValue(key, out var tagCoverage))
        {
            tagCoverage = new TagCoverage();
            coverage[key] = tagCoverage;
        }

        tagCoverage.Add(tags);
    }

    /// <summary>
    /// Splits the services of <paramref name="coverage"/> for the tag group with <paramref name="tags"/> into those registered
    /// whenever the group is, and those registered with it only for some passed tags (resolved with a runtime fallback).
    /// </summary>
    private static (ImmutableEquatableSet<TKey>? Always, ImmutableEquatableSet<TKey>? Sometimes) ClassifyByTags<TKey>(
        Dictionary<TKey, TagCoverage>? coverage,
        ImmutableEquatableArray<string> tags)
        where TKey : IEquatable<TKey>
    {
        if(coverage is null)
            return (null, null);

        HashSet<TKey>? always = null;
        HashSet<TKey>? sometimes = null;
        foreach(var kvp in coverage)
        {
            if(kvp.Value.IsAlwaysRegisteredWith(tags))
                (always ??= []).Add(kvp.Key);
            else if(kvp.Value.MayBeRegisteredWith(tags))
                (sometimes ??= []).Add(kvp.Key);
        }

        return (always?.ToImmutableEquatableSet(), sometimes?.ToImmutableEquatableSet());
    }

    /// <summary>
    /// Removes the generated wrapper registrations that an explicit registration of the wrapper type always replaces, and
    /// gives the others that it replaces for some passed tags the tags that suppress them (written as
    /// <c>if (!tags.Contains(...))</c>). One call can register several tag groups, written in tag order, so a generated
    /// wrapper registered after the explicit one would win in <c>MS.DI</c> (last wins).
    /// </summary>
    private static void ApplyExplicitWrapperSuppression<TEntry>(
        List<TEntry> entries,
        Dictionary<ServiceTypeKey, TagCoverage> explicitWrappers,
        Dictionary<ImmutableEquatableArray<string>, ImmutableEquatableArray<string>> canonicalTagsCache,
        Func<TEntry, ServiceTypeKey> getWrapper,
        Func<TEntry, ImmutableEquatableArray<string>> getTags,
        Func<TEntry, ImmutableEquatableArray<string>, TEntry> withSuppressingTags)
    {
        for(var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if(!explicitWrappers.TryGetValue(getWrapper(entry), out var coverage))
                continue;

            var tags = canonicalTagsCache[getTags(entry)];
            if(coverage.IsAlwaysRegisteredWith(tags))
            {
                entries.RemoveAt(i);
            }
            else if(coverage.MayBeRegisteredWith(tags))
            {
                entries[i] = withSuppressingTags(entry, coverage.Tags!.OrderBy(static t => t, StringComparer.Ordinal).ToImmutableEquatableArray());
            }
        }
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

    // The order of the tag groups decides which registration of a (service type, key) is the last one for a set of tags; the
    // RegisterAnalyzer mirrors it (DependencyIndex.TagGroupComparer), so keep both in sync
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