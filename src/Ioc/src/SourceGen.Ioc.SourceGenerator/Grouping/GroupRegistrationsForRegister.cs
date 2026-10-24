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

        var lazyByTagKey = GroupLazyEntriesByTagKey(CollectLazyEntries(registrations), canonicalTagsCache);
        var funcByTagKey = GroupFuncEntriesByTagKey(CollectFuncEntries(registrations), canonicalTagsCache);
        var kvpByTagKey = GroupKvpEntriesByTagKey(CollectKeyValuePairEntries(registrations), canonicalTagsCache);

        var asyncInitServiceTypeSet = new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in groupedRegistrations.Values)
        {
            foreach(var entry in group)
            {
                if(!entry.Registration.InjectionMembers.Any(static m => m.MemberType == InjectionMemberType.AsyncMethod))
                    continue;

                asyncInitServiceTypeSet.Add(entry.Registration.ServiceType.Name);
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
        var serviceTypeName = registration.ServiceType.Name;
        var implTypeName = registration.ImplementationType.Name;

        bool hasFactory = registration.Factory is not null && !registration.IsOpenGeneric;
        bool hasInstance = registration.Instance is not null && !registration.IsOpenGeneric;
        bool isServiceTypeRegistration = serviceTypeName != implTypeName;
        bool hasClosedDecorators = registration.Decorators.Length > 0 && isServiceTypeRegistration && !registration.IsOpenGeneric;

        bool hasInjectionMembers = registration.InjectionMembers.Length > 0;
        bool hasInjectConstructor = registration.ImplementationType.HasInjectConstructor;
        var constructorParams = registration.ImplementationType.ConstructorParameters;
        bool hasSpecialConstructorParams = constructorParams?.Any(static p =>
            p.HasInjectAttribute ||
            p.Type.NeedsWrapperResolution ||
            (p.IsNullable && p.Type is LazyTypeData { InstanceType: not WrapperTypeData } or FuncTypeData { ReturnType: not WrapperTypeData }) ||
            p.HasDefaultValue) == true;
        bool needsFactoryConstruction = hasInjectionMembers || hasInjectConstructor || hasSpecialConstructorParams;
        bool hasAsyncInjectionMembers = registration.InjectionMembers.Any(static m => m.MemberType == InjectionMemberType.AsyncMethod);
        bool shouldForwardServiceType = !registration.IsOpenGeneric && isServiceTypeRegistration;

        if(hasFactory)
        {
            return new FactoryRegisterEntry(registration);
        }

        if(hasInstance)
        {
            if(registration.Lifetime == ServiceLifetime.Singleton)
            {
                return new InstanceRegisterEntry(registration);
            }

            return null;
        }

        if(hasClosedDecorators)
        {
            return new DecoratorRegisterEntry(registration);
        }

        if(shouldForwardServiceType)
        {
            return new ForwardingRegisterEntry(registration);
        }

        if(needsFactoryConstruction && !registration.IsOpenGeneric)
        {
            if(hasAsyncInjectionMembers)
            {
                return new AsyncInjectionRegisterEntry(registration);
            }
            else
            {
                return new InjectionRegisterEntry(registration);
            }
        }

        return new SimpleRegisterEntry(registration);
    }

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