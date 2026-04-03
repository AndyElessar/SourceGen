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
    private readonly record struct LazyRegistrationEntry(
        string InnerServiceTypeName,
        string ImplementationTypeName,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<string> Tags);

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
        List<LazyRegistrationEntry>? entries)
    {
        if(entries is null or { Count: 0 })
            return;

        writer.WriteLine();
        writer.WriteLine("// Lazy wrapper registrations");

        foreach(var entry in entries)
        {
            var wrapperTypeName = $"global::System.Lazy<{entry.InnerServiceTypeName}>";
            var lifetime = entry.Lifetime.Name;
            var resolveCall = $"sp.{GetRequiredService}<{entry.ImplementationTypeName}>()";
            writer.WriteLine(
                $"services.Add{lifetime}<{wrapperTypeName}>(({IServiceProviderGlobalTypeName} sp) => " +
                $"new {wrapperTypeName}(() => {resolveCall}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));");
        }
    }

    /// <summary>
    /// Collects Lazy resolver entries for container code generation.
    /// </summary>
    private static ImmutableEquatableArray<ContainerLazyEntry> CollectContainerLazyEntries(
        ImmutableEquatableArray<CachedRegistration> singletons,
        ImmutableEquatableArray<CachedRegistration> scoped,
        ImmutableEquatableArray<CachedRegistration> transients,
        ImmutableEquatableDictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>> byServiceTypeAndKey)
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

        var entries = new List<ContainerLazyEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in byServiceTypeAndKey)
        {
            var serviceType = kvp.Key.ServiceType;
            if(!neededTypes.Contains(serviceType))
                continue;

            foreach(var cached in kvp.Value)
            {
                var reg = cached.Registration;
                if(reg.IsOpenGeneric)
                    continue;

                // Async-init services cannot be resolved synchronously — exclude from Lazy<T> entries
                if(cached.IsAsyncInit)
                    continue;

                var entryKey = $"{serviceType}|{reg.ImplementationType.Name}|{reg.Key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                var safeInnerType = GetSafeIdentifier(serviceType);
                var safeImplType = GetSafeIdentifier(reg.ImplementationType.Name);
                var fieldName = $"_lazy_{safeInnerType}_{safeImplType}";

                entries.Add(new ContainerLazyEntry(serviceType, cached.ResolverMethodName, fieldName));
            }
        }

        return entries.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Writes Lazy field declarations and array resolvers for container generation.
    /// </summary>
    private static void WriteContainerLazyFields(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerLazyEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine("// Lazy wrapper fields");
        writer.WriteLine();

        foreach(var entry in entries)
        {
            var wrapperTypeName = $"global::System.Lazy<{entry.InnerServiceTypeName}>";
            writer.WriteLine($"private readonly {wrapperTypeName} {entry.FieldName};");
        }
        writer.WriteLine();

        var grouped = entries
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToList();

        foreach(var group in grouped)
        {
            var innerServiceTypeName = group.Key;
            var wrapperTypeName = $"global::System.Lazy<{innerServiceTypeName}>";
            var arrayMethodName = GetLazyArrayResolverMethodName(innerServiceTypeName);

            writer.WriteLine($"private {wrapperTypeName}[] {arrayMethodName}() =>");
            writer.Indentation++;
            writer.WriteLine("[");
            writer.Indentation++;

            foreach(var entry in group)
            {
                writer.WriteLine($"{entry.FieldName},");
            }

            writer.Indentation--;
            writer.WriteLine("];");
            writer.Indentation--;
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes field initialization statements for Lazy wrapper fields.
    /// </summary>
    private static void WriteContainerLazyFieldInitializations(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerLazyEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// Initialize Lazy wrapper fields");
        foreach(var entry in entries)
        {
            var wrapperTypeName = $"global::System.Lazy<{entry.InnerServiceTypeName}>";
            writer.WriteLine($"{entry.FieldName} = new {wrapperTypeName}(() => {entry.ResolverMethodName}(), global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);");
        }
    }

    /// <summary>
    /// Writes _localResolvers entries for Lazy wrapper services.
    /// </summary>
    private static void WriteContainerLazyLocalResolverEntries(
        SourceWriter writer,
        string containerTypeName,
        ImmutableEquatableArray<ContainerLazyEntry> entries)
    {
        if(entries.Length == 0)
            return;

        writer.WriteLine();
        writer.WriteLine("// Lazy wrapper resolvers");

        var grouped = entries
            .GroupBy(static e => e.InnerServiceTypeName)
            .ToList();

        foreach(var group in grouped)
        {
            var innerServiceTypeName = group.Key;
            var wrapperTypeName = $"global::System.Lazy<{innerServiceTypeName}>";

            var lastEntry = group.Last();
            writer.WriteLine($"new(new ServiceIdentifier(typeof({wrapperTypeName}), {KeyedServiceAnyKey}), static c => c.{lastEntry.FieldName}),");

            var arrayMethodName = GetLazyArrayResolverMethodName(innerServiceTypeName);
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyCollection<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.ICollection<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyList<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IList<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof({wrapperTypeName}[]), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
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
}
