using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a Lazy standalone service registration entry to be generated.
    /// </summary>
    /// <param name="InnerServiceTypeName">The fully-qualified service type name.</param>
    /// <param name="ImplementationTypeName">The fully-qualified implementation type name.</param>
    /// <param name="Lifetime">The service lifetime matching the inner service.</param>
    /// <param name="Key">The key of a keyed source registration, which the wrapper is registered under; otherwise <see langword="null"/>.</param>
    /// <param name="ResolveCall">The expression resolving the source registration's instance.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly record struct LazyRegistrationEntry(
        string InnerServiceTypeName,
        string ImplementationTypeName,
        ServiceLifetime Lifetime,
        string? Key,
        string ResolveCall,
        ImmutableEquatableArray<string> Tags)
    {
        public void WriteRegistration(SourceWriter writer)
        {
            var lifetime = Lifetime.Name;
            if(Key is not null)
            {
                writer.WriteLine($"services.AddKeyed{lifetime}<global::System.Lazy<{InnerServiceTypeName}>>({Key}, ({IServiceProviderGlobalTypeName} sp, object? key) => new global::System.Lazy<{InnerServiceTypeName}>(() => {ResolveCall}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));");
            }
            else
            {
                writer.WriteLine($"services.Add{lifetime}<global::System.Lazy<{InnerServiceTypeName}>>(({IServiceProviderGlobalTypeName} sp) => new global::System.Lazy<{InnerServiceTypeName}>(() => {ResolveCall}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));");
            }
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
        var addedKeys = new HashSet<(string ServiceType, string ImplementationType, string? Key)>();

        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            if(!neededTypes.Contains(reg.ServiceType.Name) || reg.IsOpenGeneric)
                continue;

            if(!addedKeys.Add((reg.ServiceType.Name, reg.ImplementationType.Name, reg.Key)))
                continue;

            entries.Add(new LazyRegistrationEntry(
                reg.ServiceType.Name,
                reg.ImplementationType.Name,
                reg.Lifetime,
                reg.Key,
                GetWrapperResolveCall(reg),
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
}
