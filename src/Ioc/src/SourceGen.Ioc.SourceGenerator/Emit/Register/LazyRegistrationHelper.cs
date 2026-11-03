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
        ImmutableEquatableArray<ServiceRegistrationWithTags> registrations,
        HashSet<string> neededTypes)
    {
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
