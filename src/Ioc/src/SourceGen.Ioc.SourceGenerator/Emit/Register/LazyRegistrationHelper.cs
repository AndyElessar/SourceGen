using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a Lazy standalone service registration entry to be generated.
    /// </summary>
    /// <param name="InnerServiceTypeName">The fully-qualified service type name.</param>
    /// <param name="ImplementationTypeName">The fully-qualified implementation type name.</param>
    /// <param name="Lifetime">The service lifetime matching the inner service.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly partial record struct LazyRegistrationEntry
    {
        public void WriteRegistration(SourceWriter writer)
        {
            var wrapperTypeName = $"global::System.Lazy<{InnerServiceTypeName}>";
            var lifetime = Lifetime.Name;
            var resolveCall = $"sp.{GetRequiredService}<{ImplementationTypeName}>()";
            writer.WriteLine(
                $"services.Add{lifetime}<{wrapperTypeName}>(({IServiceProviderGlobalTypeName} sp) => " +
                $"new {wrapperTypeName}(() => {resolveCall}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));");
        }
    }

    /// <summary>
    /// Collects Lazy standalone registration entries needed by consumer dependencies.
    /// </summary>
    private static List<LazyRegistrationEntry> CollectLazyEntries(
        ImmutableEquatableArray<ServiceRegistrationWithTags> registrations)
    {
        var neededTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            ScanParamsForLazyNeeds(reg.ImplementationType.ConstructorParameters, neededTypes);
            ScanInjectionMembersForLazyNeeds(reg.InjectionMembers, neededTypes);
        }

        if(neededTypes.Count == 0)
            return [];

        var entries = new List<LazyRegistrationEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            if(!neededTypes.Contains(reg.ServiceType.Name) || reg.IsOpenGeneric)
                continue;

            var entryKey = $"{reg.ServiceType.Name}|{reg.ImplementationType.Name}|{reg.Key}";
            if(!addedKeys.Add(entryKey))
                continue;

            entries.Add(new LazyRegistrationEntry(
                reg.ServiceType.Name,
                reg.ImplementationType.Name,
                reg.Lifetime,
                regWithTags.Tags));
        }

        return entries;
    }

    /// <summary>
    /// Scans constructor parameters for Lazy dependencies.
    /// </summary>
    private static void ScanParamsForLazyNeeds(
        ImmutableEquatableArray<ParameterData>? parameters,
        HashSet<string> neededTypes)
    {
        if(parameters is null)
            return;

        foreach(var param in parameters)
        {
            ScanTypeForLazyNeeds(param.Type, neededTypes);
        }
    }

    /// <summary>
    /// Scans injection members for Lazy dependencies.
    /// </summary>
    private static void ScanInjectionMembersForLazyNeeds(
        ImmutableEquatableArray<InjectionMemberData>? members,
        HashSet<string> neededTypes)
    {
        if(members is null or { Length: 0 })
            return;

        foreach(var member in members)
        {
            if(member.Type is not null)
                ScanTypeForLazyNeeds(member.Type, neededTypes);

            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                    ScanTypeForLazyNeeds(param.Type, neededTypes);
            }
        }
    }

    /// <summary>
    /// Checks if a type is or contains a direct Lazy dependency and tracks the inner service type.
    /// </summary>
    private static void ScanTypeForLazyNeeds(
        TypeData type,
        HashSet<string> neededTypes)
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
            case LazyTypeData lazy when lazy.InstanceType is not WrapperTypeData:
                neededTypes.Add(lazy.InstanceType.Name);
                break;

            case CollectionWrapperTypeData { ElementType: LazyTypeData lazy } when lazy.InstanceType is not WrapperTypeData:
                neededTypes.Add(lazy.InstanceType.Name);
                break;
        }
    }

    /// <summary>
    /// Writes standalone Lazy registrations.
    /// </summary>
    private static void WriteLazyRegistrations(
        SourceWriter writer,
        ImmutableEquatableArray<LazyRegistrationEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// Lazy wrapper registrations");

        foreach(var entry in entries)
        {
            entry.WriteRegistration(writer);
        }
    }

    /// <summary>
    /// Gets the array resolver method name for a Lazy wrapper type.
    /// </summary>
    private static string GetLazyArrayResolverMethodName(string innerServiceTypeName)
    {
        var safeInnerType = GetSafeIdentifier(innerServiceTypeName);
        return $"GetAllLazy_{safeInnerType}_Array";
    }

    private static ImmutableEquatableArray<IocSourceGenerator.ContainerEntry> CreateLazyWrapperContainerEntries(
        ImmutableEquatableArray<ServiceLookupEntry> singletons,
        ImmutableEquatableArray<ServiceLookupEntry> scoped,
        ImmutableEquatableArray<ServiceLookupEntry> transients,
        IReadOnlyDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<ServiceLookupEntry>> serviceLookup)
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

                var entryKey = $"{serviceType}|{reg.ImplementationType.Name}|{reg.Key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var safeInnerType = GetSafeIdentifier(serviceType);
                var safeImplType = GetSafeIdentifier(reg.ImplementationType.Name);
                var fieldName = $"_lazy_{safeInnerType}_{safeImplType}";

                entries.Add((serviceType, reg.ImplementationType.Name, fieldName, cached.ResolverMethodName, reg.Key));
            }
        }

        if(entries.Count == 0)
            return [];

        var collectionFieldsByServiceType = entries
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToDictionary(
                static g => g.Key,
                static g => g.Select(static e => e.FieldName).ToImmutableEquatableArray(),
                StringComparer.Ordinal);

        var collectionEmitterFieldByServiceType = entries
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToDictionary(
                static g => g.Key,
                static g => g.Last().FieldName,
                StringComparer.Ordinal);

        var wrapperEntries = new List<IocSourceGenerator.ContainerEntry>(entries.Count);

        foreach(var entry in entries)
        {
            var emitCollectionResolver = string.Equals(
                entry.FieldName,
                collectionEmitterFieldByServiceType[entry.InnerServiceTypeName],
                StringComparison.Ordinal);
            var collectionFieldNames = emitCollectionResolver
                ? collectionFieldsByServiceType[entry.InnerServiceTypeName]
                : [];

            wrapperEntries.Add(new LazyWrapperContainerEntry(
                entry.InnerServiceTypeName,
                entry.InnerImplTypeName,
                entry.FieldName,
                entry.ResolverMethodName,
                entry.Key,
                emitCollectionResolver,
                collectionFieldNames));
        }

        return wrapperEntries.ToImmutableEquatableArray();
    }
}
