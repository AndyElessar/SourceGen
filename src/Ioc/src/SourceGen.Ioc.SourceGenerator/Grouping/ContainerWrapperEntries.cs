namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static ImmutableEquatableArray<LazyWrapperContainerEntry> CreateLazyWrapperContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> singletons,
        ImmutableEquatableArray<ServiceLookupEntry> scoped,
        ImmutableEquatableArray<ServiceLookupEntry> transients,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        ContainerNames names)
    {
        var neededTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach(var lifetime in new[] { singletons, scoped, transients })
        {
            foreach(var cached in lifetime)
            {
                var reg = cached.Registration;
                ScanParamsForLazyNeeds(reg.ImplementationType.ConstructorParameters, neededTypes);
                ScanInjectionMembersForLazyNeeds(reg.InjectionMembers, neededTypes);
            }
        }

        if(neededTypes.Count == 0)
            return [];

        var entries = new List<(string InnerServiceTypeName, string InnerImplTypeName, string FieldName, string ResolverMethodName, string? Key, bool IsShared)>();
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

                entries.Add((serviceType, reg.ImplementationType.Name, fieldName, cached.ResolverMethodName, reg.Key, reg.Lifetime != ServiceLifetime.Transient));
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
                        : LazyWrapperContainerEntry.FormatNewLazy(e.InnerServiceTypeName, $"{e.ResolverMethodName}()"))
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
                entry.Key,
                entry.IsShared,
                emitCollectionResolver,
                emitCollectionResolver ? names.LazyArrayResolver(entry.InnerServiceTypeName) : null,
                collectionElements));
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Scans constructor parameters for single-parameter Func dependencies.
    /// </summary>
    private static void ScanParamsForContainerFuncNeeds(
        ImmutableEquatableArray<ParameterData>? parameters,
        HashSet<string> neededTypes)
    {
        if(parameters is null)
            return;

        foreach(var param in parameters)
        {
            ScanTypeForContainerFuncNeeds(param.Type, neededTypes);
        }
    }

    /// <summary>
    /// Scans injection members for single-parameter Func dependencies.
    /// </summary>
    private static void ScanInjectionMembersForContainerFuncNeeds(
        ImmutableEquatableArray<InjectionMemberData>? members,
        HashSet<string> neededTypes)
    {
        if(members is null or { Length: 0 })
            return;

        foreach(var member in members)
        {
            if(member.Type is not null)
                ScanTypeForContainerFuncNeeds(member.Type, neededTypes);

            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                    ScanTypeForContainerFuncNeeds(param.Type, neededTypes);
            }
        }
    }

    /// <summary>
    /// Tracks Func&lt;T&gt; wrapper needs for container field-backed wrapper resolvers.
    /// </summary>
    private static void ScanTypeForContainerFuncNeeds(TypeData type, HashSet<string> neededTypes)
    {
        switch(type)
        {
            // Nested wrappers are intentionally excluded from standalone field generation. This is a
            // deliberate pragmatic choice: direct container resolution of nested wrappers (for example,
            // container.GetService<Lazy<Func<T>>>()) is extremely rare, and constructor injection is
            // already fully covered by inline construction. Keeping nested wrappers inline avoids
            // expanding field scanning, naming conventions, and scoped-container infrastructure for all
            // nested shapes. Nested wrappers also usually do not require cross-consumer instance
            // sharing, so per-consumer inline instances are semantically correct.
            case FuncTypeData func when !func.HasInputParameters && func.ReturnType is not WrapperTypeData:
                neededTypes.Add(func.ReturnType.Name);
                break;

            case CollectionWrapperTypeData { ElementType: FuncTypeData func } when !func.HasInputParameters && func.ReturnType is not WrapperTypeData:
                neededTypes.Add(func.ReturnType.Name);
                break;
        }
    }

    private static ImmutableEquatableArray<FuncWrapperContainerEntry> CreateFuncWrapperContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> singletons,
        ImmutableEquatableArray<ServiceLookupEntry> scoped,
        ImmutableEquatableArray<ServiceLookupEntry> transients,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        ContainerNames names)
    {
        var neededTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach(var lifetime in new[] { singletons, scoped, transients })
        {
            foreach(var cached in lifetime)
            {
                var reg = cached.Registration;
                ScanParamsForContainerFuncNeeds(reg.ImplementationType.ConstructorParameters, neededTypes);
                ScanInjectionMembersForContainerFuncNeeds(reg.InjectionMembers, neededTypes);
            }
        }

        if(neededTypes.Count == 0)
            return [];

        var entries = new List<(string InnerServiceTypeName, string InnerImplTypeName, string FieldName, string ResolverMethodName, string? Key)>();
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

                entries.Add((serviceType, reg.ImplementationType.Name, fieldName, cached.ResolverMethodName, reg.Key));
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
                entry.Key,
                emitCollectionResolver,
                emitCollectionResolver ? names.FuncArrayResolver(entry.InnerServiceTypeName) : null,
                collectionFieldNames));
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }

    private static ImmutableEquatableArray<KvpWrapperContainerEntry> CreateKvpWrapperContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> singletons,
        ImmutableEquatableArray<ServiceLookupEntry> scoped,
        ImmutableEquatableArray<ServiceLookupEntry> transients,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup,
        ContainerNames names)
    {
        var neededPairs = new HashSet<(string KeyTypeName, string ValueTypeName)>();

        foreach(var lifetime in new[] { singletons, scoped, transients })
        {
            foreach(var cached in lifetime)
            {
                var reg = cached.Registration;
                ScanParamsForKvpNeeds(reg.ImplementationType.ConstructorParameters, neededPairs);
                ScanInjectionMembersForKvpNeeds(reg.InjectionMembers, neededPairs);
            }
        }

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
                    kvpResolverName,
                    names.KvpArrayResolver(keyTypeName, valueTypeName),
                    names.KvpDictionaryResolver(keyTypeName, valueTypeName)));
            }
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }
}
