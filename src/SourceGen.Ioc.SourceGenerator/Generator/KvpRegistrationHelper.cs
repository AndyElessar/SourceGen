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
        ImmutableEquatableArray<string> Tags);

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
        List<KvpRegistrationEntry>? entries)
    {
        if(entries is null or { Count: 0 })
            return;

        writer.WriteLine();
        writer.WriteLine("// KeyValuePair registrations for keyed services");

        foreach(var entry in entries)
        {
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{entry.KeyTypeName}, {entry.ValueTypeName}>";
            var lifetime = entry.Lifetime.Name;
            var resolveCall = $"sp.{GetRequiredKeyedService}<{entry.ValueTypeName}>({entry.KeyExpr})";

            // KeyValuePair<K,V> is a struct, so we cannot use AddSingleton<TService> (class constraint).
            // Use ServiceDescriptor directly with a factory that boxes the struct.
            writer.WriteLine(
                $"services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(" +
                $"typeof({kvpTypeName}), " +
                $"({IServiceProviderGlobalTypeName} sp) => (object)new {kvpTypeName}({entry.KeyExpr}, {resolveCall}), " +
                $"global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{lifetime}));");
        }
    }

    /// <summary>
    /// Represents a KeyValuePair resolver entry for container code generation.
    /// </summary>
    /// <param name="KeyTypeName">The fully-qualified key type name.</param>
    /// <param name="ValueTypeName">The fully-qualified value type name.</param>
    /// <param name="KeyExpr">The key literal expression.</param>
    /// <param name="ResolverMethodName">The method name of the value service resolver.</param>
    /// <param name="KvpResolverMethodName">The method name for this KVP resolver.</param>
    private readonly record struct ContainerKvpEntry(
        string KeyTypeName,
        string ValueTypeName,
        string KeyExpr,
        string ResolverMethodName,
        string KvpResolverMethodName);

    /// <summary>
    /// Collects KeyValuePair resolver entries for container code generation.
    /// Scans all container registrations for KVP dependencies and finds matching keyed services.
    /// </summary>
    private static List<ContainerKvpEntry> CollectContainerKvpEntries(
        ContainerRegistrationGroups groups)
    {
        // Step 1: Scan all registrations for KVP needs
        var neededPairs = new HashSet<(string KeyTypeName, string ValueTypeName)>();

        foreach(var lifetime in new[] { groups.Singletons, groups.Scoped, groups.Transients })
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

        // Step 2: Find keyed registrations matching those value types
        var entries = new List<ContainerKvpEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in groups.ByServiceTypeAndKey)
        {
            var key = kvp.Key.Key;
            if(key is null)
                continue;

            var serviceType = kvp.Key.ServiceType;

            foreach(var (keyTypeName, valueTypeName) in neededPairs)
            {
                if(!string.Equals(serviceType, valueTypeName, StringComparison.Ordinal))
                    continue;

                var cached = kvp.Value[^1]; // Last wins

                // Filter by key value type compatibility:
                // - "object" key type accepts all key value types
                // - Otherwise, the registration's key value type must match exactly
                if(!IsKeyTypeCompatible(keyTypeName, cached.Registration.KeyValueType))
                    continue;

                var entryKey = $"{keyTypeName}|{valueTypeName}|{key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var safeKey = GetSafeIdentifier(key);
                var safeKeyType = GetSafeIdentifier(keyTypeName);
                var safeValueType = GetSafeIdentifier(valueTypeName);
                var kvpResolverName = $"GetKvp_{safeKeyType}_{safeValueType}_{safeKey}";

                entries.Add(new ContainerKvpEntry(
                    keyTypeName,
                    valueTypeName,
                    key,
                    cached.ResolverMethodName,
                    kvpResolverName));
            }
        }

        return entries;
    }

    /// <summary>
    /// Writes KVP resolver methods and the array resolver for container generation.
    /// Individual methods return a single <c>KeyValuePair&lt;K, V&gt;</c> entry.
    /// The array resolver collects all KVP entries for <c>GetServices&lt;KVP&lt;K, V&gt;&gt;</c>.
    /// </summary>
    private static void WriteContainerKvpResolverMethods(
        SourceWriter writer,
        List<ContainerKvpEntry> entries)
    {
        if(entries.Count == 0)
            return;

        writer.WriteLine("// KeyValuePair resolver methods");
        writer.WriteLine();

        // Write individual KVP resolver methods
        foreach(var entry in entries)
        {
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{entry.KeyTypeName}, {entry.ValueTypeName}>";
            writer.WriteLine($"private {kvpTypeName} {entry.KvpResolverMethodName}() => new {kvpTypeName}({entry.KeyExpr}, {entry.ResolverMethodName}());");
            writer.WriteLine();
        }

        // Write array resolver methods grouped by (KeyTypeName, ValueTypeName)
        var grouped = entries
            .GroupBy(static e => (e.KeyTypeName, e.ValueTypeName))
            .ToList();

        foreach(var group in grouped)
        {
            var (keyTypeName, valueTypeName) = group.Key;
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyTypeName}, {valueTypeName}>";
            var arrayMethodName = GetKvpArrayResolverMethodName(keyTypeName, valueTypeName);

            writer.WriteLine($"private {kvpTypeName}[] {arrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"{entry.KvpResolverMethodName}(),");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
            writer.WriteLine();
        }

        // Write dictionary resolver methods grouped by (KeyTypeName, ValueTypeName)
        foreach(var group in grouped)
        {
            var (keyTypeName, valueTypeName) = group.Key;
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyTypeName}, {valueTypeName}>";
            var dictionaryMethodName = GetKvpDictionaryResolverMethodName(keyTypeName, valueTypeName);

            writer.WriteLine($"private global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}> {dictionaryMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine($"new global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}>()");
            writer.WriteLine("{");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"[{entry.KeyExpr}] = {entry.ResolverMethodName}(),");
            }

            writer.Indentation--;
            writer.WriteLine("};");
            writer.Indentation--;
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes <c>_localResolvers</c> entries for KVP services so that
    /// <c>GetServices&lt;KeyValuePair&lt;K, V&gt;&gt;</c> can collect them.
    /// </summary>
    private static void WriteContainerKvpLocalResolverEntries(
        SourceWriter writer,
        string containerTypeName,
        List<ContainerKvpEntry> entries)
    {
        if(entries.Count == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// KeyValuePair resolvers");

        // Write IEnumerable<KVP<K,V>> entries grouped by (KeyTypeName, ValueTypeName)
        var grouped = entries
            .GroupBy(static e => (e.KeyTypeName, e.ValueTypeName))
            .ToList();

        foreach(var group in grouped)
        {
            var (keyTypeName, valueTypeName) = group.Key;
            var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyTypeName}, {valueTypeName}>";
            var arrayMethodName = GetKvpArrayResolverMethodName(keyTypeName, valueTypeName);
            var dictionaryMethodName = GetKvpDictionaryResolverMethodName(keyTypeName, valueTypeName);

            // IEnumerable, IReadOnlyCollection, ICollection → Dictionary
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<{kvpTypeName}>), {KeyedServiceAnyKey}), static c => c.{dictionaryMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyCollection<{kvpTypeName}>), {KeyedServiceAnyKey}), static c => c.{dictionaryMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.ICollection<{kvpTypeName}>), {KeyedServiceAnyKey}), static c => c.{dictionaryMethodName}()),");

            // IReadOnlyList, IList, T[] → Array
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyList<{kvpTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IList<{kvpTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof({kvpTypeName}[]), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");

            // IReadOnlyDictionary, IDictionary, Dictionary → Dictionary
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyDictionary<{keyTypeName}, {valueTypeName}>), {KeyedServiceAnyKey}), static c => c.{dictionaryMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IDictionary<{keyTypeName}, {valueTypeName}>), {KeyedServiceAnyKey}), static c => c.{dictionaryMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.Dictionary<{keyTypeName}, {valueTypeName}>), {KeyedServiceAnyKey}), static c => c.{dictionaryMethodName}()),");
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
    /// Checks if any keyed registrations exist for the given KVP key/value type pair.
    /// Used to determine whether a KVP resolver method will be generated.
    /// </summary>
    private static bool HasKvpRegistrations(string keyTypeName, string valueTypeName, ContainerRegistrationGroups groups)
    {
        foreach(var kvp in groups.ByServiceTypeAndKey)
        {
            if(kvp.Key.Key is null)
                continue;

            if(!string.Equals(kvp.Key.ServiceType, valueTypeName, StringComparison.Ordinal))
                continue;

            var cached = kvp.Value[^1];
            if(IsKeyTypeCompatible(keyTypeName, cached.Registration.KeyValueType))
                return true;
        }

        return false;
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
}
