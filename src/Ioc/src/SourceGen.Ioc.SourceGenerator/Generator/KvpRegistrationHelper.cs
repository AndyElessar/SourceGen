using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a KeyValuePair service registration entry to be generated.
    /// Used when consumers depend on <c>KeyValuePair&lt;K, V&gt;</c>, <c>IDictionary&lt;K, V&gt;</c>,
    /// or <c>IEnumerable&lt;KeyValuePair&lt;K, V&gt;&gt;</c>.
    /// </summary>
    /// <param name="KeyTypeName">The fully-qualified key type name (e.g., <c>string</c>).</param>
    /// <param name="ValueTypeName">The fully-qualified value type name (e.g., <c>global::TestNamespace.IService</c>).</param>
    /// <param name="KeyExpr">The key literal expression (e.g., <c>"Key1"</c>).</param>
    /// <param name="Lifetime">The service lifetime matching the keyed value service.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly record struct KvpRegistrationEntry(
        string KeyTypeName,
        string ValueTypeName,
        string KeyExpr,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<string> Tags)
    {
        public void WriteRegistration(SourceWriter writer)
        {
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{KeyTypeName}, {ValueTypeName}>";
            var lifetime = Lifetime.Name;
            var resolveCall = $"sp.{GetRequiredKeyedService}<{ValueTypeName}>({KeyExpr})";

            // KeyValuePair<K,V> is a struct, so we cannot use AddSingleton<TService> (class constraint).
            // Use ServiceDescriptor directly with a factory that boxes the struct.
            writer.WriteLine(
                $"services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(" +
                $"typeof({kvpTypeName}), " +
                $"({IServiceProviderGlobalTypeName} sp) => (object)new {kvpTypeName}({KeyExpr}, {resolveCall}), " +
                $"global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime}));");
        }
    }

    /// <summary>
    /// Collects KeyValuePair registration entries needed by consumer dependencies.
    /// Scans all registrations for KVP/Dictionary/IEnumerable&lt;KVP&gt; dependencies,
    /// then finds matching keyed services to generate explicit KVP registrations for.
    /// </summary>
    private static List<KvpRegistrationEntry> CollectKeyValuePairEntries(
        ImmutableEquatableArray<ServiceRegistrationWithTags> registrations)
    {
        // Step 1: Scan for (KeyTypeName, ValueTypeName) pairs needed by consumers
        var neededPairs = new HashSet<(string KeyTypeName, string ValueTypeName)>();
        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            ScanParamsForKvpNeeds(reg.ImplementationType.ConstructorParameters, neededPairs);
            ScanInjectionMembersForKvpNeeds(reg.InjectionMembers, neededPairs);
        }

        if(neededPairs.Count == 0)
            return [];

        // Step 2: Find keyed registrations matching those value types
        var entries = new List<KvpRegistrationEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            if(reg.Key is null)
                continue;

            foreach(var (keyTypeName, valueTypeName) in neededPairs)
            {
                if(!string.Equals(reg.ServiceType.Name, valueTypeName, StringComparison.Ordinal))
                    continue;

                // Filter by key value type compatibility:
                // - "object" key type accepts all key value types
                // - Otherwise, the registration's key value type must match exactly
                if(!IsKeyTypeCompatible(keyTypeName, reg.KeyValueType))
                    continue;

                var entryKey = $"{keyTypeName}|{valueTypeName}|{reg.Key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                entries.Add(new KvpRegistrationEntry(keyTypeName, valueTypeName, reg.Key, reg.Lifetime, regWithTags.Tags));
            }
        }

        return entries;
    }

    /// <summary>
    /// Scans constructor parameters for KVP/Dictionary/IEnumerable&lt;KVP&gt; dependencies
    /// and adds needed (KeyTypeName, ValueTypeName) pairs.
    /// </summary>
    private static void ScanParamsForKvpNeeds(
        ImmutableEquatableArray<ParameterData>? parameters,
        HashSet<(string KeyTypeName, string ValueTypeName)> neededPairs)
    {
        if(parameters is null)
            return;

        foreach(var param in parameters)
        {
            ScanTypeForKvpNeeds(param.Type, neededPairs);
        }
    }

    /// <summary>
    /// Scans injection members for KVP/Dictionary dependencies and adds needed pairs.
    /// </summary>
    private static void ScanInjectionMembersForKvpNeeds(
        ImmutableEquatableArray<InjectionMemberData>? members,
        HashSet<(string KeyTypeName, string ValueTypeName)> neededPairs)
    {
        if(members is null or { Length: 0 })
            return;

        foreach(var member in members)
        {
            if(member.Type is not null)
                ScanTypeForKvpNeeds(member.Type, neededPairs);

            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                    ScanTypeForKvpNeeds(param.Type, neededPairs);
            }
        }
    }

    /// <summary>
    /// Checks if a type is or contains a KeyValuePair/Dictionary dependency
    /// and adds the (KeyTypeName, ValueTypeName) pair if found.
    /// </summary>
    private static void ScanTypeForKvpNeeds(
        TypeData type,
        HashSet<(string KeyTypeName, string ValueTypeName)> neededPairs)
    {
        switch(type)
        {
            case KeyValuePairTypeData kvp:
                neededPairs.Add((kvp.KeyType.Name, kvp.ValueType.Name));
                break;
            case DictionaryTypeData dict:
                neededPairs.Add((dict.KeyType.Name, dict.ValueType.Name));
                break;
            case CollectionWrapperTypeData { ElementType: KeyValuePairTypeData kvp }:
                neededPairs.Add((kvp.KeyType.Name, kvp.ValueType.Name));
                break;
        }
    }

    /// <summary>
    /// Writes explicit <c>KeyValuePair&lt;K, V&gt;</c> service registrations to the <see cref="SourceWriter"/>.
    /// Each keyed service matching a consumer's KVP dependency gets a non-keyed registration
    /// so that <c>GetServices&lt;KeyValuePair&lt;K, V&gt;&gt;()</c> can collect all entries.
    /// </summary>
    private static void WriteKvpRegistrations(
        SourceWriter writer,
        ImmutableEquatableArray<KvpRegistrationEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// KeyValuePair registrations for keyed services");

        foreach(var entry in entries)
        {
            entry.WriteRegistration(writer);
        }
    }

    /// <summary>
    /// Gets the array resolver method name for a KVP type pair.
    /// </summary>
    private static string GetKvpArrayResolverMethodName(string keyTypeName, string valueTypeName)
    {
        var safeKeyType = GetSafeIdentifier(keyTypeName);
        var safeValueType = GetSafeIdentifier(valueTypeName);
        return $"GetAllKvp_{safeKeyType}_{safeValueType}_Array";
    }

    /// <summary>
    /// Gets the dictionary resolver method name for a KVP type pair.
    /// </summary>
    private static string GetKvpDictionaryResolverMethodName(string keyTypeName, string valueTypeName)
    {
        var safeKeyType = GetSafeIdentifier(keyTypeName);
        var safeValueType = GetSafeIdentifier(valueTypeName);
        return $"GetAllKvp_{safeKeyType}_{safeValueType}_Dictionary";
    }

    /// <summary>
    /// Checks if a registration's key value type is compatible with the requested KVP key type.
    /// </summary>
    /// <param name="requestedKeyTypeName">The key type name requested by the consumer (e.g., "string", "object").</param>
    /// <param name="registrationKeyValueType">The actual key value type of the registration, or null if unknown.</param>
    /// <returns>True if the key types are compatible.</returns>
    private static bool IsKeyTypeCompatible(string requestedKeyTypeName, TypeData? registrationKeyValueType)
    {
        // "object" key type accepts all key value types
        if(string.Equals(requestedKeyTypeName, "object", StringComparison.Ordinal))
            return true;

        // If the registration's key value type is unknown (null), treat as object — only compatible with object
        if(registrationKeyValueType is null)
            return false;

        return string.Equals(registrationKeyValueType.Name, requestedKeyTypeName, StringComparison.Ordinal);
    }

    private static ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> CreateKvpWrapperContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> singletons,
        ImmutableEquatableArray<ServiceLookupEntry> scoped,
        ImmutableEquatableArray<ServiceLookupEntry> transients,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup)
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

        var wrapperEntries = new List<IocSourceGenerator.ContainerEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in serviceLookup)
        {
            var key = kvp.Key.Key;
            if(key is null)
                continue;

            var serviceType = kvp.Key.ServiceType;

            foreach(var (keyTypeName, valueTypeName) in neededPairs)
            {
                if(!string.Equals(serviceType, valueTypeName, StringComparison.Ordinal))
                    continue;

                var cached = kvp.Value[^1];
                if(!IsKeyTypeCompatible(keyTypeName, cached.Registration.KeyValueType))
                    continue;

                var entryKey = $"{keyTypeName}|{valueTypeName}|{key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var safeKey = GetSafeIdentifier(key);
                var safeKeyType = GetSafeIdentifier(keyTypeName);
                var safeValueType = GetSafeIdentifier(valueTypeName);
                var kvpResolverName = $"GetKvp_{safeKeyType}_{safeValueType}_{safeKey}";

                wrapperEntries.Add(new KvpWrapperContainerEntry(
                    keyTypeName,
                    valueTypeName,
                    key,
                    cached.ResolverMethodName,
                    kvpResolverName));
            }
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }
}
