using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    private static IEnumerable<(string ServiceType, string? Key, string ResolverExpression)> EnumerateInterfaceServiceResolvers(ContainerRegistrationGroups groups)
    {
        foreach(var kvp in groups.LastWinsByServiceType)
        {
            if(kvp.Value is not ServiceContainerEntry serviceEntry)
            {
                continue;
            }

            // The lookup key, not the (possibly shared) registration's service type: an entry is
            // registered under every service type alias, including its implementation type
            var serviceType = kvp.Key.ServiceType;
            yield return (serviceEntry.GetExposedServiceType(serviceType), kvp.Key.Key, serviceEntry.FormatResolveExpression(serviceType, ""));
        }
    }

    private static IEnumerable<(string ServiceType, string? Key, string ResolverExpression)> EnumerateKeyedServiceResolvers(ContainerRegistrationGroups groups)
    {
        foreach(var kvp in groups.LastWinsByServiceType)
        {
            if(kvp.Key.Key is null || kvp.Value is not ServiceContainerEntry serviceEntry)
            {
                continue;
            }

            yield return (serviceEntry.GetExposedServiceType(kvp.Key.ServiceType), kvp.Key.Key, serviceEntry.FormatResolveExpression(kvp.Key.ServiceType, ""));
        }
    }

    private const string ProjectTaskMethodName = "__ProjectTask";
    private const string ResolverEntryMethodName = "__Resolver";
    private const string KeyedResolverEntryMethodName = "__KeyedResolver";
    private const string CollectionResolverEntriesMethodName = "__CollectionResolvers";

    /// <summary>
    /// Writes the <c>_localResolvers</c> entry resolving <paramref name="serviceType"/> (with <paramref name="key"/> when keyed) through <paramref name="entry"/>.
    /// </summary>
    private static void WriteLocalResolverEntry(SourceWriter writer, ServiceContainerEntry entry, string serviceType, string? key)
    {
        var exposedServiceType = entry.GetExposedServiceType(serviceType);

        // The common lambda is written directly instead of being formatted first
        if(entry.HasResolverMethodLambda)
        {
            if(key is not null)
            {
                writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof({exposedServiceType}), {key}, static c => c.{entry.ResolverMethodName}()),");
            }
            else
            {
                writer.WriteLine($"{ResolverEntryMethodName}(typeof({exposedServiceType}), static c => c.{entry.ResolverMethodName}()),");
            }

            return;
        }

        if(key is not null)
        {
            writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof({exposedServiceType}), {key}, {entry.FormatResolverLambda(serviceType)}),");
        }
        else
        {
            writer.WriteLine($"{ResolverEntryMethodName}(typeof({exposedServiceType}), {entry.FormatResolverLambda(serviceType)}),");
        }
    }

    /// <summary>
    /// Writes the spread of the <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IList&lt;T&gt;</c> and <c>T[]</c> resolver entries for <paramref name="elementType"/>,
    /// all resolved through the <paramref name="arrayMethodName"/> array resolver.
    /// </summary>
    private static void WriteCollectionResolverEntries(SourceWriter writer, string elementType, string arrayMethodName) =>
        writer.WriteLine($"..{CollectionResolverEntriesMethodName}<{elementType}>(static c => c.{arrayMethodName}()),");

    /// <summary>
    /// Writes the helpers that build resolver entries, so each entry in <c>_localResolvers</c> stays short.
    /// </summary>
    private static void WriteResolverEntryHelpers(SourceWriter writer, ContainerModel container, ContainerRegistrationGroups groups)
    {
        var resolverType = $"global::System.Func<{container.ContainerTypeName}, object>";

        writer.WriteLine($"private static {ResolverEntryAlias} {ResolverEntryMethodName}(global::System.Type serviceType, {resolverType} resolver) =>");
        writer.Indentation++;
        writer.WriteLine($"new(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, {KeyedServiceAnyKey}), resolver);");
        writer.Indentation--;

        if(groups.LastWinsByServiceType.Keys.Any(static key => key.Key is not null))
        {
            writer.WriteLine();
            writer.WriteLine($"private static {ResolverEntryAlias} {KeyedResolverEntryMethodName}(global::System.Type serviceType, object key, {resolverType} resolver) =>");
            writer.Indentation++;
            writer.WriteLine("new(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key), resolver);");
            writer.Indentation--;
        }

        var hasCollectionEntries = groups.CollectionServiceTypes.Length > 0
            || groups.LazyWrapperEntries.Any(static e => e.EmitCollectionResolver)
            || groups.FuncWrapperEntries.Any(static e => e.EmitCollectionResolver);

        if(hasCollectionEntries)
        {
            writer.WriteLine();
            writer.WriteLine($"private static {ResolverEntryAlias}[] {CollectionResolverEntriesMethodName}<T>({resolverType} resolver) =>");
            writer.WriteLine("[");
            writer.Indentation++;
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IEnumerable<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyCollection<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.ICollection<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyList<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IList<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(T[]), resolver),");
            writer.Indentation--;
            writer.WriteLine("];");
        }
    }

    /// <summary>
    /// Writes the helper that converts <c>Task&lt;TResult&gt;</c> to <c>Task&lt;TService&gt;</c> for async-init service aliases.
    /// </summary>
    private static void WriteProjectTaskHelper(SourceWriter writer, ContainerRegistrationGroups groups)
    {
        if(!groups.LastWinsByServiceType.Any(static kvp => kvp.Value is AsyncServiceContainerEntry asyncEntry && asyncEntry.NeedsTaskProjection(kvp.Key.ServiceType)))
            return;

        writer.WriteLine($"private static async global::System.Threading.Tasks.Task<TService> {ProjectTaskMethodName}<TService, TResult>(global::System.Threading.Tasks.Task<TResult> task)");
        writer.Indentation++;
        writer.WriteLine("=> (TService)(object)(await task.ConfigureAwait(false))!;");
        writer.Indentation--;
        writer.WriteLine();
    }

    private static void WriteWrapperLocalResolverEntries(
        SourceWriter writer,
        ContainerRegistrationGroups groups)
    {
        var kvpEntries = groups.KvpWrapperEntries;

        if(kvpEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// KeyValuePair resolvers");

            foreach(var group in kvpEntries.GroupBy(static entry => (entry.KeyTypeName, entry.ValueTypeName)))
            {
                group.First().WriteLocalResolverEntries(writer);
            }
        }

        var lazyEntries = groups.LazyWrapperEntries;

        if(lazyEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Lazy wrapper resolvers");

            foreach(var group in lazyEntries.GroupBy(static entry => entry.InnerServiceTypeName))
            {
                group.Last().WriteLocalResolverEntries(writer);
            }
        }

        var funcEntries = groups.FuncWrapperEntries;

        if(funcEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Func wrapper resolvers");

            foreach(var group in funcEntries.GroupBy(static entry => entry.InnerServiceTypeName))
            {
                group.Last().WriteLocalResolverEntries(writer);
            }
        }
    }
}
