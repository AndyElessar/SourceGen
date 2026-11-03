namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ImmutableEquatableArray<LazyWrapperContainerEntry> CreateLazyWrapperContainerEntries(
        HashSet<string> neededTypes,
        IReadOnlyDictionary<ServiceTypeKey, ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        ContainerNames names)
    {
        if(neededTypes.Count == 0)
            return [];

        var entries = new List<(string InnerServiceTypeName, string InnerImplTypeName, string FieldName, string ResolverMethodName, string? Instance, string? Key, bool IsShared)>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in serviceLookup)
        {
            var serviceType = kvp.Key.ServiceType;
            if(!neededTypes.Contains(serviceType))
                continue;

            foreach(var cached in kvp.Value)
            {
                var reg = cached.Registration;
                if(reg.IsOpenGeneric || cached.IsAsyncInit)
                    continue;

                // One wrapper per resolver: keyed, factory and instance registrations of one implementation have their own resolvers
                var entryKey = $"{serviceType}|{cached.ResolverMethodName}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var fieldName = $"_lazy_{names.Get(serviceType)}{cached.FieldName}";

                entries.Add((serviceType, reg.ImplementationType.Name, fieldName, cached.ResolverMethodName, reg.Instance, reg.Key, reg.Lifetime != ServiceLifetime.Transient));
            }
        }

        if(entries.Count == 0)
            return [];

        // Collections of wrappers contain the non-keyed registrations only
        var collectionElementsByServiceType = entries
            .Where(static e => e.Key is null)
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToDictionary(
                static g => g.Key,
                static g => g
                    .Select(static e => e.IsShared
                        ? e.FieldName
                        : LazyWrapperContainerEntry.FormatNewLazy(e.InnerServiceTypeName, FormatWrappedResolveCall(e.Instance, e.ResolverMethodName, "")))
                    .ToImmutableEquatableArray(),
                StringComparer.Ordinal);

        var collectionEmitterFieldByServiceType = entries
            .Where(static e => e.Key is null)
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToDictionary(
                static g => g.Key,
                static g => g.Last().FieldName,
                StringComparer.Ordinal);

        var wrapperEntries = new List<LazyWrapperContainerEntry>(entries.Count);

        foreach(var entry in entries)
        {
            var emitCollectionResolver = collectionEmitterFieldByServiceType.TryGetValue(entry.InnerServiceTypeName, out var emitterFieldName)
                && string.Equals(entry.FieldName, emitterFieldName, StringComparison.Ordinal);
            var collectionElements = emitCollectionResolver
                ? collectionElementsByServiceType[entry.InnerServiceTypeName]
                : [];

            wrapperEntries.Add(new LazyWrapperContainerEntry(
                entry.InnerServiceTypeName,
                entry.InnerImplTypeName,
                entry.FieldName,
                entry.ResolverMethodName,
                entry.Instance,
                entry.Key,
                entry.IsShared,
                emitCollectionResolver,
                emitCollectionResolver ? names.LazyArrayResolver(entry.InnerServiceTypeName) : null,
                collectionElements));
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<FuncWrapperContainerEntry> CreateFuncWrapperContainerEntries(
        HashSet<string> neededTypes,
        IReadOnlyDictionary<ServiceTypeKey, ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        ContainerNames names)
    {
        if(neededTypes.Count == 0)
            return [];

        var entries = new List<(string InnerServiceTypeName, string InnerImplTypeName, string FieldName, string ResolverMethodName, string? Instance, string? Key)>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in serviceLookup)
        {
            var serviceType = kvp.Key.ServiceType;
            if(!neededTypes.Contains(serviceType))
                continue;

            foreach(var cached in kvp.Value)
            {
                var reg = cached.Registration;
                if(reg.IsOpenGeneric || cached.IsAsyncInit)
                    continue;

                // One wrapper per resolver: keyed, factory and instance registrations of one implementation have their own resolvers
                var entryKey = $"{serviceType}|{cached.ResolverMethodName}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var fieldName = $"_func_{names.Get(serviceType)}{cached.FieldName}";

                entries.Add((serviceType, reg.ImplementationType.Name, fieldName, cached.ResolverMethodName, reg.Instance, reg.Key));
            }
        }

        if(entries.Count == 0)
            return [];

        // Collections of wrappers contain the non-keyed registrations only
        var collectionFieldsByServiceType = entries
            .Where(static e => e.Key is null)
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToDictionary(
                static g => g.Key,
                static g => g.Select(static e => e.FieldName).ToImmutableEquatableArray(),
                StringComparer.Ordinal);

        var collectionEmitterFieldByServiceType = entries
            .Where(static e => e.Key is null)
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToDictionary(
                static g => g.Key,
                static g => g.Last().FieldName,
                StringComparer.Ordinal);

        var wrapperEntries = new List<FuncWrapperContainerEntry>(entries.Count);

        foreach(var entry in entries)
        {
            var emitCollectionResolver = collectionEmitterFieldByServiceType.TryGetValue(entry.InnerServiceTypeName, out var emitterFieldName)
                && string.Equals(entry.FieldName, emitterFieldName, StringComparison.Ordinal);
            var collectionFieldNames = emitCollectionResolver
                ? collectionFieldsByServiceType[entry.InnerServiceTypeName]
                : [];

            wrapperEntries.Add(new FuncWrapperContainerEntry(
                entry.InnerServiceTypeName,
                entry.InnerImplTypeName,
                entry.FieldName,
                entry.ResolverMethodName,
                entry.Instance,
                entry.Key,
                emitCollectionResolver,
                emitCollectionResolver ? names.FuncArrayResolver(entry.InnerServiceTypeName) : null,
                collectionFieldNames));
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<KvpWrapperContainerEntry> CreateKvpWrapperContainerEntries(
        HashSet<(string KeyTypeName, string ValueTypeName)> neededPairs,
        IReadOnlyDictionary<ServiceTypeKey, ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        ContainerNames names)
    {
        if(neededPairs.Count == 0)
            return [];

        var wrapperEntries = new List<KvpWrapperContainerEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);
        var neededByValueType = neededPairs.ToLookup(static pair => pair.ValueTypeName, StringComparer.Ordinal);

        foreach(var kvp in serviceLookup)
        {
            var key = kvp.Key.Key;
            if(key is null)
                continue;

            var serviceType = kvp.Key.ServiceType;

            foreach(var (keyTypeName, valueTypeName) in neededByValueType[serviceType])
            {
                var cached = kvp.Value[^1];
                if(!IsKeyTypeCompatible(keyTypeName, cached.Registration.KeyValueType))
                    continue;

                var entryKey = $"{keyTypeName}|{valueTypeName}|{key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var kvpResolverName = $"GetKvp_{names.Get(keyTypeName)}_{names.Get(valueTypeName)}_{GetSafeIdentifier(key)}";

                wrapperEntries.Add(new KvpWrapperContainerEntry(
                    keyTypeName,
                    valueTypeName,
                    key,
                    cached.ResolverMethodName,
                    cached.Registration.Instance,
                    kvpResolverName,
                    names.KvpArrayResolver(keyTypeName, valueTypeName),
                    names.KvpDictionaryResolver(keyTypeName, valueTypeName)));
            }
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }
}
