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

        var demand = WrapperDemand.Scan(registrations.Select(static r => r.Registration));
        var lazyByTagKey = GroupLazyEntriesByTagKey(CollectLazyEntries(registrations, demand.LazyServiceTypes), canonicalTagsCache);
        var funcByTagKey = GroupFuncEntriesByTagKey(CollectFuncEntries(registrations, demand.FuncTypes, features), canonicalTagsCache);
        var kvpByTagKey = GroupKvpEntriesByTagKey(CollectKeyValuePairEntries(registrations, demand.KvpPairs), canonicalTagsCache);

        var asyncInitServiceTypeSet = new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in groupedRegistrations.Values)
        {
            foreach(var entry in group)
            {
                if(!entry.Registration.IsAsyncInit)
                    continue;

                asyncInitServiceTypeSet.Add(entry.Registration.ServiceType.Name);

                // Async decorators make only the decorated service type async-init, not the implementation type
                if(entry.Registration.HasAsyncInjection)
                    asyncInitServiceTypeSet.Add(entry.Registration.ImplementationType.Name);
            }
        }

        var asyncInitServiceTypes = asyncInitServiceTypeSet.Count > 0
            ? asyncInitServiceTypeSet.ToImmutableEquatableSet()
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
                    kvpEntries is null ? [] : kvpEntries.ToImmutableEquatableArray());
            })
            .ToImmutableEquatableArray();

        return new RegisterOutputModel(
            methodBaseName,
            rootNamespace,
            assemblyName,
            tagGroups,
            asyncInitServiceTypes);
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
        if(registration.IsOpenGeneric)
        {
            return new SimpleRegisterEntry(registration);
        }

        if(registration.Factory is not null && !SharesFactoryInstance(registration))
        {
            return new FactoryRegisterEntry(registration);
        }

        if(registration.Instance is not null)
        {
            return registration.Lifetime == ServiceLifetime.Singleton ? new InstanceRegisterEntry(registration) : null;
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