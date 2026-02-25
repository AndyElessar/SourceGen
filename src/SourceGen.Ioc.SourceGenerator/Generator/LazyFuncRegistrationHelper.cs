using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Represents a Lazy/Func standalone service registration entry to be generated.
    /// Used when consumers depend on <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c>,
    /// or <c>IEnumerable&lt;Lazy&lt;T&gt;&gt;</c> / <c>IEnumerable&lt;Func&lt;T&gt;&gt;</c>.
    /// </summary>
    /// <param name="WrapperKind">Whether this is Lazy or Func.</param>
    /// <param name="InnerServiceTypeName">The fully-qualified service type name (e.g., <c>global::TestNamespace.IMyService</c>).</param>
    /// <param name="ImplementationTypeName">The fully-qualified implementation type name (e.g., <c>global::TestNamespace.MyService</c>).</param>
    /// <param name="Lifetime">The service lifetime matching the inner service.</param>
    /// <param name="Tags">The tags inherited from the source registration.</param>
    private readonly record struct LazyFuncRegistrationEntry(
        WrapperKind WrapperKind,
        string InnerServiceTypeName,
        string ImplementationTypeName,
        ServiceLifetime Lifetime,
        ImmutableEquatableArray<string> Tags);

    /// <summary>
    /// Collects Lazy/Func standalone registration entries needed by consumer dependencies.
    /// Scans all registrations for <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c>,
    /// <c>IEnumerable&lt;Lazy&lt;T&gt;&gt;</c>, and <c>IEnumerable&lt;Func&lt;T&gt;&gt;</c> dependencies,
    /// then finds matching service registrations to generate standalone wrapper registrations for.
    /// </summary>
    private static List<LazyFuncRegistrationEntry> CollectLazyFuncEntries(
        ImmutableEquatableArray<ServiceRegistrationWithTags> registrations)
    {
        // Step 1: Scan for (WrapperKind, InnerTypeName) pairs needed by consumers
        var neededTypes = new HashSet<(WrapperKind Kind, string InnerTypeName)>();
        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;
            ScanParamsForLazyFuncNeeds(reg.ImplementationType.ConstructorParameters, neededTypes);
            ScanInjectionMembersForLazyFuncNeeds(reg.InjectionMembers, neededTypes);
        }

        if(neededTypes.Count == 0)
            return [];

        // Step 2: Find registrations matching those inner types
        var entries = new List<LazyFuncRegistrationEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var regWithTags in registrations)
        {
            var reg = regWithTags.Registration;

            foreach(var (kind, innerTypeName) in neededTypes)
            {
                if(!string.Equals(reg.ServiceType.Name, innerTypeName, StringComparison.Ordinal))
                    continue;

                // Open generic registrations are not wrapped (e.g., typeof(IHandler<>) cannot be wrapped in Lazy<>)
                if(reg.IsOpenGeneric)
                    continue;

                var entryKey = $"{kind}|{innerTypeName}|{reg.ImplementationType.Name}|{reg.Key}";
                if(!addedKeys.Add(entryKey))
                    continue;

                entries.Add(new LazyFuncRegistrationEntry(
                    kind,
                    innerTypeName,
                    reg.ImplementationType.Name,
                    reg.Lifetime,
                    regWithTags.Tags));
            }
        }

        return entries;
    }

    /// <summary>
    /// Scans constructor parameters for <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c> dependencies
    /// and adds needed (WrapperKind, InnerTypeName) pairs.
    /// </summary>
    private static void ScanParamsForLazyFuncNeeds(
        ImmutableEquatableArray<ParameterData>? parameters,
        HashSet<(WrapperKind Kind, string InnerTypeName)> neededTypes)
    {
        if(parameters is null)
            return;

        foreach(var param in parameters)
        {
            ScanTypeForLazyFuncNeeds(param.Type, neededTypes);
        }
    }

    /// <summary>
    /// Scans injection members for <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c> dependencies and adds needed pairs.
    /// </summary>
    private static void ScanInjectionMembersForLazyFuncNeeds(
        ImmutableEquatableArray<InjectionMemberData>? members,
        HashSet<(WrapperKind Kind, string InnerTypeName)> neededTypes)
    {
        if(members is null or { Length: 0 })
            return;

        foreach(var member in members)
        {
            if(member.Type is not null)
                ScanTypeForLazyFuncNeeds(member.Type, neededTypes);

            if(member.Parameters is not null)
            {
                foreach(var param in member.Parameters)
                    ScanTypeForLazyFuncNeeds(param.Type, neededTypes);
            }
        }
    }

    /// <summary>
    /// Checks if a type is or contains a direct <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c> dependency
    /// (where T is not itself a wrapper type) and adds the (WrapperKind, InnerTypeName) pair if found.
    /// Also handles <c>IEnumerable&lt;Lazy&lt;T&gt;&gt;</c> and <c>IEnumerable&lt;Func&lt;T&gt;&gt;</c>.
    /// </summary>
    private static void ScanTypeForLazyFuncNeeds(
        TypeData type,
        HashSet<(WrapperKind Kind, string InnerTypeName)> neededTypes)
    {
        switch(type)
        {
            // Direct Lazy<T> where T is not a wrapper
            case LazyTypeData lazy when lazy.InstanceType is not WrapperTypeData:
                neededTypes.Add((WrapperKind.Lazy, lazy.InstanceType.Name));
                break;

            // Direct Func<T> where T is not a wrapper
            case FuncTypeData func when func.ReturnType is not WrapperTypeData:
                neededTypes.Add((WrapperKind.Func, func.ReturnType.Name));
                break;

            // Collection<Lazy<T>> where T is not a wrapper
            case CollectionWrapperTypeData { ElementType: LazyTypeData lazy } when lazy.InstanceType is not WrapperTypeData:
                neededTypes.Add((WrapperKind.Lazy, lazy.InstanceType.Name));
                break;

            // Collection<Func<T>> where T is not a wrapper
            case CollectionWrapperTypeData { ElementType: FuncTypeData func } when func.ReturnType is not WrapperTypeData:
                neededTypes.Add((WrapperKind.Func, func.ReturnType.Name));
                break;
        }
    }

    /// <summary>
    /// Writes standalone <c>Lazy&lt;T&gt;</c> and <c>Func&lt;T&gt;</c> service registrations.
    /// These registrations are emitted within the appropriate tag group.
    /// Lazy and Func registrations are emitted in separate sections.
    /// </summary>
    private static void WriteLazyFuncRegistrations(
        SourceWriter writer,
        List<LazyFuncRegistrationEntry>? entries)
    {
        if(entries is null or { Count: 0 })
            return;

        var lazyEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Lazy).ToList();
        var funcEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Func).ToList();

        if(lazyEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Lazy wrapper registrations");

            foreach(var entry in lazyEntries)
            {
                var wrapperTypeName = $"global::System.Lazy<{entry.InnerServiceTypeName}>";
                var lifetime = entry.Lifetime.Name;
                var resolveCall = $"sp.{GetRequiredService}<{entry.ImplementationTypeName}>()";
                writer.WriteLine(
                    $"services.Add{lifetime}<{wrapperTypeName}>(({IServiceProviderGlobalTypeName} sp) => " +
                    $"new {wrapperTypeName}(() => {resolveCall}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));");
            }
        }

        if(funcEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Func wrapper registrations");

            foreach(var entry in funcEntries)
            {
                var wrapperTypeName = $"global::System.Func<{entry.InnerServiceTypeName}>";
                var lifetime = entry.Lifetime.Name;
                var resolveCall = $"sp.{GetRequiredService}<{entry.ImplementationTypeName}>()";
                writer.WriteLine(
                    $"services.Add{lifetime}<{wrapperTypeName}>(({IServiceProviderGlobalTypeName} sp) => " +
                    $"new {wrapperTypeName}(() => {resolveCall}));");
            }
        }
    }

    // ── Container Lazy/Func helper methods ──

    /// <summary>
    /// Represents a Lazy/Func resolver entry for container code generation.
    /// </summary>
    /// <param name="WrapperKind">Whether this is Lazy or Func.</param>
    /// <param name="InnerServiceTypeName">The fully-qualified inner service type name.</param>
    /// <param name="ResolverMethodName">The method name of the inner service resolver.</param>
    /// <param name="FieldName">The field name for storing the wrapper instance.</param>
    private readonly record struct ContainerLazyFuncEntry(
        WrapperKind WrapperKind,
        string InnerServiceTypeName,
        string ResolverMethodName,
        string FieldName);

    /// <summary>
    /// Collects Lazy/Func resolver entries for container code generation.
    /// Scans all container registrations for Lazy/Func dependencies and finds matching service resolvers.
    /// </summary>
    private static List<ContainerLazyFuncEntry> CollectContainerLazyFuncEntries(
        ContainerRegistrationGroups groups)
    {
        // Step 1: Scan all registrations for Lazy/Func needs
        var neededTypes = new HashSet<(WrapperKind Kind, string InnerTypeName)>();

        foreach(var lifetime in new[] { groups.Singletons, groups.Scoped, groups.Transients })
        {
            foreach(var cached in lifetime)
            {
                var reg = cached.Registration;
                ScanParamsForLazyFuncNeeds(reg.ImplementationType.ConstructorParameters, neededTypes);
                ScanInjectionMembersForLazyFuncNeeds(reg.InjectionMembers, neededTypes);
            }
        }

        if(neededTypes.Count == 0)
            return [];

        // Step 2: Find registrations matching those inner types
        var entries = new List<ContainerLazyFuncEntry>();
        var addedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach(var kvp in groups.ByServiceTypeAndKey)
        {
            var serviceType = kvp.Key.ServiceType;
            var key = kvp.Key.Key;

            // Only match non-keyed (AnyKey) registrations for now
            // Keyed wrapper resolvers would need additional service key handling
            foreach(var (kind, innerTypeName) in neededTypes)
            {
                if(!string.Equals(serviceType, innerTypeName, StringComparison.Ordinal))
                    continue;

                foreach(var cached in kvp.Value)
                {
                    var reg = cached.Registration;

                    // Skip open generic registrations
                    if(reg.IsOpenGeneric)
                        continue;

                    var entryKey = $"{kind}|{innerTypeName}|{reg.ImplementationType.Name}|{reg.Key}";
                    if(!addedKeys.Add(entryKey))
                        continue;

                    var wrapperPrefix = kind == WrapperKind.Lazy ? "lazy" : "func";
                    var safeInnerType = GetSafeIdentifier(innerTypeName);
                    var safeImplType = GetSafeIdentifier(reg.ImplementationType.Name);
                    var fieldName = $"_{wrapperPrefix}_{safeInnerType}_{safeImplType}";

                    entries.Add(new ContainerLazyFuncEntry(
                        kind,
                        innerTypeName,
                        cached.ResolverMethodName,
                        fieldName));
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// Writes Lazy/Func readonly field declarations and array resolvers for container generation.
    /// Individual fields store a single <c>Lazy&lt;T&gt;</c> or <c>Func&lt;T&gt;</c> instance.
    /// The array resolvers collect all wrapper entries for <c>GetServices&lt;Lazy&lt;T&gt;&gt;</c>.
    /// Lazy and Func fields and resolvers are emitted in separate sections.
    /// </summary>
    private static void WriteContainerLazyFuncFields(
        SourceWriter writer,
        List<ContainerLazyFuncEntry> entries)
    {
        if(entries.Count == 0)
            return;

        var lazyEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Lazy).ToList();
        var funcEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Func).ToList();

        // Write Lazy fields and array resolvers
        if(lazyEntries.Count > 0)
        {
            writer.WriteLine("// Lazy wrapper fields");
            writer.WriteLine();

            foreach(var entry in lazyEntries)
            {
                var wrapperTypeName = $"global::System.Lazy<{entry.InnerServiceTypeName}>";
                writer.WriteLine($"private readonly {wrapperTypeName} {entry.FieldName};");
            }
            writer.WriteLine();

            WriteLazyFuncArrayResolvers(writer, lazyEntries);
        }

        // Write Func fields and array resolvers
        if(funcEntries.Count > 0)
        {
            writer.WriteLine("// Func wrapper fields");
            writer.WriteLine();

            foreach(var entry in funcEntries)
            {
                var wrapperTypeName = $"global::System.Func<{entry.InnerServiceTypeName}>";
                writer.WriteLine($"private readonly {wrapperTypeName} {entry.FieldName};");
            }
            writer.WriteLine();

            WriteLazyFuncArrayResolvers(writer, funcEntries);
        }
    }

    /// <summary>
    /// Writes array resolver methods for a group of Lazy or Func entries.
    /// </summary>
    private static void WriteLazyFuncArrayResolvers(
        SourceWriter writer,
        List<ContainerLazyFuncEntry> entries)
    {
        var grouped = entries
            .GroupBy(static e => (e.WrapperKind, e.InnerServiceTypeName))
            .ToList();

        foreach(var group in grouped)
        {
            var (kind, innerServiceTypeName) = group.Key;
            var wrapperPrefix = kind == WrapperKind.Lazy
                ? "global::System.Lazy"
                : "global::System.Func";
            var wrapperTypeName = $"{wrapperPrefix}<{innerServiceTypeName}>";
            var arrayMethodName = GetLazyFuncArrayResolverMethodName(kind, innerServiceTypeName);

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
    /// Writes field initialization statements for Lazy/Func wrapper fields.
    /// Called from both the default constructor and the scoped constructor.
    /// Lazy and Func initializations are emitted in separate sections.
    /// </summary>
    private static void WriteContainerLazyFuncFieldInitializations(
        SourceWriter writer,
        List<ContainerLazyFuncEntry> entries)
    {
        if(entries.Count == 0)
            return;

        var lazyEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Lazy).ToList();
        var funcEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Func).ToList();

        if(lazyEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize Lazy wrapper fields");
            foreach(var entry in lazyEntries)
            {
                var wrapperTypeName = $"global::System.Lazy<{entry.InnerServiceTypeName}>";
                writer.WriteLine($"{entry.FieldName} = new {wrapperTypeName}(() => {entry.ResolverMethodName}(), global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);");
            }
        }

        if(funcEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize Func wrapper fields");
            foreach(var entry in funcEntries)
            {
                var wrapperTypeName = $"global::System.Func<{entry.InnerServiceTypeName}>";
                writer.WriteLine($"{entry.FieldName} = new {wrapperTypeName}(() => {entry.ResolverMethodName}());");
            }
        }
    }

    /// <summary>
    /// Writes <c>_localResolvers</c> entries for Lazy/Func wrapper services so that
    /// <c>GetServices&lt;Lazy&lt;T&gt;&gt;</c> and <c>GetRequiredService&lt;Lazy&lt;T&gt;&gt;</c> can resolve them.
    /// Lazy and Func resolver entries are emitted in separate sections.
    /// </summary>
    private static void WriteContainerLazyFuncLocalResolverEntries(
        SourceWriter writer,
        string containerTypeName,
        List<ContainerLazyFuncEntry> entries)
    {
        if(entries.Count == 0)
            return;

        var lazyEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Lazy).ToList();
        var funcEntries = entries.Where(static e => e.WrapperKind == WrapperKind.Func).ToList();

        if(lazyEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Lazy wrapper resolvers");
            WriteLazyFuncResolverGroup(writer, lazyEntries);
        }

        if(funcEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Func wrapper resolvers");
            WriteLazyFuncResolverGroup(writer, funcEntries);
        }
    }

    /// <summary>
    /// Writes _localResolvers entries for a group of Lazy or Func entries.
    /// </summary>
    private static void WriteLazyFuncResolverGroup(
        SourceWriter writer,
        List<ContainerLazyFuncEntry> entries)
    {
        var grouped = entries
            .GroupBy(static e => (e.WrapperKind, e.InnerServiceTypeName))
            .ToList();

        foreach(var group in grouped)
        {
            var (kind, innerServiceTypeName) = group.Key;
            var wrapperPrefix = kind == WrapperKind.Lazy
                ? "global::System.Lazy"
                : "global::System.Func";
            var wrapperTypeName = $"{wrapperPrefix}<{innerServiceTypeName}>";

            // Single service entry (last wins, same as other registrations)
            var lastEntry = group.Last();
            writer.WriteLine($"new(new ServiceIdentifier(typeof({wrapperTypeName}), {KeyedServiceAnyKey}), static c => c.{lastEntry.FieldName}),");

            // Collection entries (IEnumerable, IReadOnlyCollection, ICollection, IReadOnlyList, IList, T[])
            var arrayMethodName = GetLazyFuncArrayResolverMethodName(kind, innerServiceTypeName);
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyCollection<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.ICollection<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyList<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IList<{wrapperTypeName}>), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof({wrapperTypeName}[]), {KeyedServiceAnyKey}), static c => c.{arrayMethodName}()),");
        }
    }

    /// <summary>
    /// Gets the array resolver method name for a Lazy/Func wrapper type.
    /// </summary>
    private static string GetLazyFuncArrayResolverMethodName(WrapperKind kind, string innerServiceTypeName)
    {
        var wrapperPrefix = kind == WrapperKind.Lazy ? "Lazy" : "Func";
        var safeInnerType = GetSafeIdentifier(innerServiceTypeName);
        return $"GetAll{wrapperPrefix}_{safeInnerType}_Array";
    }
}
