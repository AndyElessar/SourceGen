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

        var tagKeyCache = new Dictionary<ImmutableEquatableArray<string>, string>();
        var sortedTagsCache = new Dictionary<string, ImmutableEquatableArray<string>>(StringComparer.Ordinal);
        foreach(var regWithTags in registrations)
        {
            var tags = regWithTags.Tags;
            if(tagKeyCache.ContainsKey(tags))
            {
                continue;
            }

            if(tags.Length > 0)
            {
                var sortedTags = tags.OrderBy(static t => t, StringComparer.Ordinal).ToImmutableEquatableArray();
                var key = string.Join(",", sortedTags);
                tagKeyCache[tags] = key;
                sortedTagsCache[key] = sortedTags;
            }
            else
            {
                tagKeyCache[tags] = string.Empty;
                sortedTagsCache[string.Empty] = [];
            }
        }

        var shouldFilterInjection = !IocFeaturesHelper.HasAllInjectionFeatures(features);
        var groupedRegistrations = new Dictionary<string, List<RegisterEntry>>(StringComparer.Ordinal);

        foreach(var regWithTags in registrations)
        {
            var registration = shouldFilterInjection
                ? FilterRegistrationForFeatures(regWithTags.Registration, features)
                : regWithTags.Registration;
            var tagKey = tagKeyCache[regWithTags.Tags];

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

        var lazyByTagKey = GroupLazyEntriesByTagKey(CollectLazyEntries(registrations), tagKeyCache);
        var funcByTagKey = GroupFuncEntriesByTagKey(CollectFuncEntries(registrations), tagKeyCache);
        var kvpByTagKey = GroupKvpEntriesByTagKey(CollectKeyValuePairEntries(registrations), tagKeyCache);

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
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .Select(kvp =>
            {
                var tags = sortedTagsCache[kvp.Key];

                lazyByTagKey.TryGetValue(kvp.Key, out var lazyEntries);
                funcByTagKey.TryGetValue(kvp.Key, out var funcEntries);
                kvpByTagKey.TryGetValue(kvp.Key, out var kvpEntries);

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

    private static Dictionary<string, List<LazyRegistrationEntry>> GroupLazyEntriesByTagKey(
        List<LazyRegistrationEntry> entries,
        Dictionary<ImmutableEquatableArray<string>, string> tagKeyCache)
    {
        var grouped = new Dictionary<string, List<LazyRegistrationEntry>>(StringComparer.Ordinal);
        foreach(var entry in entries)
        {
            var tagKey = tagKeyCache[entry.Tags];
            if(!grouped.TryGetValue(tagKey, out var list))
            {
                list = [];
                grouped[tagKey] = list;
            }

            list.Add(entry);
        }

        return grouped;
    }

    private static Dictionary<string, List<FuncRegistrationEntry>> GroupFuncEntriesByTagKey(
        List<FuncRegistrationEntry> entries,
        Dictionary<ImmutableEquatableArray<string>, string> tagKeyCache)
    {
        var grouped = new Dictionary<string, List<FuncRegistrationEntry>>(StringComparer.Ordinal);
        foreach(var entry in entries)
        {
            var tagKey = tagKeyCache[entry.Tags];
            if(!grouped.TryGetValue(tagKey, out var list))
            {
                list = [];
                grouped[tagKey] = list;
            }

            list.Add(entry);
        }

        return grouped;
    }

    private static Dictionary<string, List<KvpRegistrationEntry>> GroupKvpEntriesByTagKey(
        List<KvpRegistrationEntry> entries,
        Dictionary<ImmutableEquatableArray<string>, string> tagKeyCache)
    {
        var grouped = new Dictionary<string, List<KvpRegistrationEntry>>(StringComparer.Ordinal);
        foreach(var entry in entries)
        {
            var tagKey = tagKeyCache[entry.Tags];
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

        bool isSimplePattern = !hasFactory
            && !hasInstance
            && !hasClosedDecorators
            && !shouldForwardServiceType
            && (registration.IsOpenGeneric || !needsFactoryConstruction);

        if(isSimplePattern)
        {
            return new SimpleRegisterEntry(registration);
        }

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

        if(shouldForwardServiceType && !hasFactory && !hasClosedDecorators)
        {
            return new ForwardingRegisterEntry(registration);
        }

        if(needsFactoryConstruction && hasAsyncInjectionMembers && !registration.IsOpenGeneric)
        {
            return new AsyncInjectionRegisterEntry(registration);
        }

        if(needsFactoryConstruction && !hasAsyncInjectionMembers && !registration.IsOpenGeneric)
        {
            return new InjectionRegisterEntry(registration);
        }

        return new SimpleRegisterEntry(registration);
    }
}