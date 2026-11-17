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
    private const string CreateLockMethodName = "__CreateLock";
    private const string CreateSemaphoreMethodName = "__CreateSemaphore";
    private const string ResolverEntryMethodName = "__Resolver";
    private const string KeyedResolverEntryMethodName = "__KeyedResolver";
    private const string CollectionResolverEntriesMethodName = "__CollectionResolvers";
    private const string KeyedCollectionResolverEntriesMethodName = "__KeyedCollectionResolvers";
    private const string EnumerableResolverEntryMethodName = "__EnumerableResolver";
    private const string ResolveCollectionMethodName = "__ResolveCollection";
    private const string FindCollectionElementResolverMethodName = "__FindCollectionElementResolver";
    private const string IsCollectionServiceMethodName = "__IsCollectionService";
    private const string GetFallbackServicesMethodName = "__GetFallbackServices";
    private const string MergeFallbackServicesMethodName = "__MergeFallbackServices";
    private const string ImportModuleResolversMethodName = "__ImportModuleResolvers";
    private const string CombineResolversMethodName = "__CombineResolvers";
    private const string CombineCollectionMethodName = "__CombineCollection";
    private const string IsEnumerableTypeMethodName = "__IsEnumerableType";
    private const string IsCombinedElementMethodName = "__IsCombinedElement";

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
    /// Writes the resolver entries of a generated collection resolver: the six collection types without key (non-keyed) or
    /// under the collection's key, or only <c>IEnumerable&lt;T&gt;</c> under <c>KeyedService.AnyKey</c> for the collection of
    /// all keyed services (<c>MS.DI</c> serves no other collection type with <c>AnyKey</c>). The collection types registered
    /// explicitly under the same key are left out, so the explicit registration wins (see Container.Collections).
    /// </summary>
    private static void WriteCollectionResolverEntries(SourceWriter writer, CollectionContainerEntry collection)
    {
        var elementType = collection.ElementServiceTypeName;

        if(collection.AllKeys)
        {
            // A single registration is created inline, without array method
            if(collection.ArrayMethodName is { } anyKeyArrayMethodName)
            {
                writer.WriteLine($"{EnumerableResolverEntryMethodName}<{elementType}>({KeyedServiceAnyKey}, static c => c.{anyKeyArrayMethodName}()),");
            }
            else if(collection.ElementResolvers[0] is DirectServiceDependency direct)
            {
                writer.WriteLine($"{EnumerableResolverEntryMethodName}<{elementType}>({KeyedServiceAnyKey}, static c => new {elementType}[] {{ c.{direct.ResolverMethodName}() }}),");
            }
            else
            {
                writer.WriteLine($"{EnumerableResolverEntryMethodName}<{elementType}>({KeyedServiceAnyKey}, static c => new {elementType}[] {{ {collection.ElementResolvers[0].FormatExpression(false)} }}),");
            }

            return;
        }

        var arrayMethodName = collection.ArrayMethodName!;
        var explicitTypes = collection.ExplicitTypes;

        if(explicitTypes == CollectionTypes.None)
        {
            if(collection.Key is { } key)
            {
                writer.WriteLine($"..{KeyedCollectionResolverEntriesMethodName}<{elementType}>({key}, static c => c.{arrayMethodName}()),");
            }
            else
            {
                WriteCollectionResolverEntries(writer, elementType, arrayMethodName);
            }

            return;
        }

        // Rare: one entry for each collection type that is not registered explicitly
        var keyArgument = collection.Key ?? NonKeyedServiceKey;
        if((explicitTypes & CollectionTypes.Enumerable) == 0)
        {
            writer.WriteLine($"{EnumerableResolverEntryMethodName}<{elementType}>({keyArgument}, static c => c.{arrayMethodName}()),");
        }

        foreach(var (type, prefix, suffix) in NonEnumerableCollectionTypes)
        {
            if((explicitTypes & type) != 0)
                continue;

            if(collection.Key is { } key)
            {
                writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof({prefix}{elementType}{suffix}), {key}, static c => c.{arrayMethodName}()),");
            }
            else
            {
                writer.WriteLine($"{ResolverEntryMethodName}(typeof({prefix}{elementType}{suffix}), static c => c.{arrayMethodName}()),");
            }
        }
    }

    /// <summary>
    /// Writes the helpers that build resolver entries, so each entry in <c>_localResolvers</c> stays short.
    /// </summary>
    private static void WriteResolverEntryHelpers(SourceWriter writer, ContainerModel container, ContainerRegistrationGroups groups)
    {
        var resolverType = $"global::System.Func<{container.ContainerTypeName}, object>";

        writer.WriteLine($"private static {ResolverEntryAlias} {ResolverEntryMethodName}(global::System.Type serviceType, {resolverType} resolver) =>");
        writer.Indentation++;
        writer.WriteLine($"new(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, {NonKeyedServiceKey}), resolver);");
        writer.Indentation--;

        var hasKeyedServices = false;
        foreach(var key in groups.LastWinsByServiceType.Keys)
        {
            if(key.Key is not null)
            {
                hasKeyedServices = true;
                break;
            }
        }

        if(hasKeyedServices)
        {
            writer.WriteLine();
            writer.WriteLine($"private static {ResolverEntryAlias} {KeyedResolverEntryMethodName}(global::System.Type serviceType, object key, {resolverType} resolver) =>");
            writer.Indentation++;
            writer.WriteLine("new(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key), resolver);");
            writer.Indentation--;
        }

        var hasCollectionEntries = groups.CollectionServiceTypes.Length > 0;
        var hasKeyedCollectionEntries = false;
        foreach(var entry in groups.CollectionEntries)
        {
            if(entry is CollectionContainerEntry { Key: not null, ExplicitTypes: CollectionTypes.None })
            {
                hasKeyedCollectionEntries = true;
            }
        }

        foreach(var entry in groups.LazyWrapperEntries)
        {
            hasCollectionEntries |= entry.EmitCollectionResolver;
        }

        foreach(var entry in groups.FuncWrapperEntries)
        {
            hasCollectionEntries |= entry.EmitCollectionResolver;
        }

        if(hasCollectionEntries || groups.CollectionEntries.Length > 0)
        {
            // IEnumerable<T> also returns the fallback provider's services under the same key, first (see Container.Collections).
            // IocCollectionResolver keeps the array resolver, so an importing container merges its own fallback services instead.
            writer.WriteLine();
            writer.WriteLine($"private static {ResolverEntryAlias} {EnumerableResolverEntryMethodName}<T>(object key, {resolverType} resolver)");
            writer.WriteLine("{");
            writer.Indentation++;
            if(container.IntegrateServiceProvider)
            {
                writer.WriteLine($"var serviceKey = key == {NonKeyedServiceKey} ? null : key;");
                writer.WriteLine($"return new(new global::SourceGen.Ioc.ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<T>), key), new global::SourceGen.Ioc.IocCollectionResolver<{container.ContainerTypeName}>(resolver, c => c.{MergeFallbackServicesMethodName}((T[])resolver(c), serviceKey)).Resolve);");
            }
            else
            {
                writer.WriteLine($"return new(new global::SourceGen.Ioc.ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<T>), key), new global::SourceGen.Ioc.IocCollectionResolver<{container.ContainerTypeName}>(resolver, resolver).Resolve);");
            }
            writer.Indentation--;
            writer.WriteLine("}");
        }

        if(hasCollectionEntries)
        {
            writer.WriteLine();
            writer.WriteLine($"private static {ResolverEntryAlias}[] {CollectionResolverEntriesMethodName}<T>({resolverType} resolver) =>");
            writer.WriteLine("[");
            writer.Indentation++;
            writer.WriteLine($"{EnumerableResolverEntryMethodName}<T>({NonKeyedServiceKey}, resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyCollection<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.ICollection<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyList<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.Collections.Generic.IList<T>), resolver),");
            writer.WriteLine($"{ResolverEntryMethodName}(typeof(T[]), resolver),");
            writer.Indentation--;
            writer.WriteLine("];");
        }

        if(hasKeyedCollectionEntries)
        {
            writer.WriteLine();
            writer.WriteLine($"private static {ResolverEntryAlias}[] {KeyedCollectionResolverEntriesMethodName}<T>(object key, {resolverType} resolver) =>");
            writer.WriteLine("[");
            writer.Indentation++;
            writer.WriteLine($"{EnumerableResolverEntryMethodName}<T>(key, resolver),");
            writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyCollection<T>), key, resolver),");
            writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof(global::System.Collections.Generic.ICollection<T>), key, resolver),");
            writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof(global::System.Collections.Generic.IReadOnlyList<T>), key, resolver),");
            writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof(global::System.Collections.Generic.IList<T>), key, resolver),");
            writer.WriteLine($"{KeyedResolverEntryMethodName}(typeof(T[]), key, resolver),");
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

    /// <summary>
    /// Writes the helpers allocating a <c>Lock</c> / <c>SemaphoreSlim</c> synchronization field on its first use:
    /// concurrent first callers agree on one instance through <c>Interlocked.CompareExchange</c>.
    /// </summary>
    private static void WriteSyncFieldHelpers(SourceWriter writer, ContainerRegistrationGroups groups)
    {
        var usesLock = false;
        var usesSemaphore = false;
        foreach(var entry in groups.SingletonEntries.Concat(groups.ScopedEntries))
        {
            usesLock |= entry.SynchronizationStrategy == ThreadSafeStrategy.Lock;
            usesSemaphore |= entry.SynchronizationStrategy == ThreadSafeStrategy.SemaphoreSlim;
        }

        if(usesLock)
        {
            writer.WriteLine($"private static global::System.Threading.Lock {CreateLockMethodName}(ref global::System.Threading.Lock? field)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("var created = new global::System.Threading.Lock();");
            writer.WriteLine("return global::System.Threading.Interlocked.CompareExchange(ref field, created, null) ?? created;");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
        }

        if(usesSemaphore)
        {
            // A losing semaphore is dropped undisposed: without AvailableWaitHandle it holds no unmanaged resource
            writer.WriteLine($"private static global::System.Threading.SemaphoreSlim {CreateSemaphoreMethodName}(ref global::System.Threading.SemaphoreSlim? field)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("var created = new global::System.Threading.SemaphoreSlim(1, 1);");
            writer.WriteLine("return global::System.Threading.Interlocked.CompareExchange(ref field, created, null) ?? created;");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
        }
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

        // The entries of each inner service type are written by its last non-keyed wrapper (EmitCollectionResolver), which a
        // keyed wrapper of the same type may follow; the other wrappers write nothing
        var lazyEntries = groups.LazyWrapperEntries;

        if(lazyEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Lazy wrapper resolvers");

            foreach(var group in lazyEntries.GroupBy(static entry => entry.InnerServiceTypeName))
            {
                group.FirstOrDefault(static entry => entry.EmitCollectionResolver)?.WriteLocalResolverEntries(writer);
            }
        }

        var funcEntries = groups.FuncWrapperEntries;

        if(funcEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Func wrapper resolvers");

            foreach(var group in funcEntries.GroupBy(static entry => entry.InnerServiceTypeName))
            {
                group.FirstOrDefault(static entry => entry.EmitCollectionResolver)?.WriteLocalResolverEntries(writer);
            }
        }
    }
}
