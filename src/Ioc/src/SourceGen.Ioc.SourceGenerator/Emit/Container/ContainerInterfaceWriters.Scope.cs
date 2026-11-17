using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Writes IServiceScopeFactory implementation.
    /// </summary>
    private static void WriteIServiceScopeFactoryImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IServiceScopeFactory");
        writer.WriteLine();

        writer.WriteLine("public global::Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine($"return new {container.ClassName}(this, null);");
        }
        else
        {
            writer.WriteLine($"return new {container.ClassName}(this);");
        }
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("public global::Microsoft.Extensions.DependencyInjection.AsyncServiceScope CreateAsyncScope() => new(CreateScope());");
        writer.WriteLine();
        writer.WriteLine("global::System.IServiceProvider global::Microsoft.Extensions.DependencyInjection.IServiceScope.ServiceProvider => this;");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();

        if(container.IntegrateServiceProvider)
        {
            WriteIIocFallbackScopeOwnerImplementation(writer, container);
        }
    }

    /// <summary>
    /// Writes IIocFallbackScopeOwner implementation, through which scopes of an imported module
    /// resolve fallback services with the fallback scope of the importing container scope.
    /// </summary>
    private static void WriteIIocFallbackScopeOwnerImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IIocFallbackScopeOwner");
        writer.WriteLine();

        writer.WriteLine("global::Microsoft.Extensions.DependencyInjection.IServiceScope global::SourceGen.Ioc.IIocFallbackScopeOwner.CreateScope(global::SourceGen.Ioc.IIocFallbackScopeOwner fallbackScopeOwner)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"return new {container.ClassName}(this, fallbackScopeOwner);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("global::System.IServiceProvider? global::SourceGen.Ioc.IIocFallbackScopeOwner.GetFallbackServiceProvider() => GetFallbackServiceProvider();");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes IIocContainer implementation.
    /// </summary>
    private static void WriteIIocContainerImplementation(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region IIocContainer");
        writer.WriteLine();

        if(effectiveUseSwitchStatement)
        {
            writer.WriteLine($"public static global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>>> Resolvers => _localResolvers;");
        }
        else
        {
            writer.WriteLine($"public static global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>>> Resolvers => _serviceResolvers;");
        }
        writer.WriteLine();

        // Write _localResolvers as static field
        writer.WriteLine($"private static readonly global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>>[] _localResolvers =");
        writer.WriteLine("[");
        writer.Indentation++;

        // Built-in services: IServiceProvider, IServiceScopeFactory, and the container itself
        writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::System.IServiceProvider), static c => c),");
        writer.WriteLine($"{ResolverEntryMethodName}(typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory), static c => c),");
        writer.WriteLine($"{ResolverEntryMethodName}(typeof({container.ContainerTypeName}), static c => c),");

        // Wrapper entries (KVP, then Lazy, then Func) come before the registrations: ToFrozenDictionary keeps the last entry,
        // so a wrapper type registered explicitly (e.g. IEnumerable<Lazy<T>>) wins over the generated one, like MS.DI
        WriteWrapperLocalResolverEntries(writer, groups);

        // Use service type resolver mappings (already filtered for non-open-generics)
        foreach(var kvp in groups.LastWinsByServiceType)
        {
            if(kvp.Value is not ServiceContainerEntry entry)
                continue;

            WriteLocalResolverEntry(writer, entry, kvp.Key.ServiceType, kvp.Key.Key);
        }

        // Add IEnumerable<T>, IReadOnlyCollection<T>, ICollection<T>, IReadOnlyList<T>, IList<T>, T[] entries for collection service types
        foreach(var entry in groups.CollectionEntries)
        {
            if(entry is CollectionContainerEntry collection)
            {
                WriteCollectionResolverEntries(writer, collection);
            }
        }

        writer.Indentation--;
        writer.WriteLine("];");
        writer.WriteLine();

        WriteResolverEntryHelpers(writer, container, groups);

        // Write _serviceResolvers as static field (only when not using switch statement)
        if(!effectiveUseSwitchStatement)
        {
            writer.WriteLine();
            if(container.ImportedModules.Length > 0)
            {
                // Combine with imported modules - wrap module resolvers to pass the correct module instance
                // Use static access (module.Name is the fully qualified type name) for static abstract Resolvers
                // Sources in import order, then the container's own: single services are last-wins, collections are combined
                writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>> _serviceResolvers = {CombineResolversMethodName}(");
                writer.WriteLine("[");
                writer.Indentation++;

                foreach(var module in container.ImportedModules)
                {
                    var fieldName = GetModuleFieldName(module.Type.Name);
                    writer.WriteLine($"{ImportModuleResolversMethodName}({module.Type.Name}.Resolvers, static c => c.{fieldName}),");
                }

                writer.WriteLine("_localResolvers,");
                writer.Indentation--;
                writer.WriteLine("]);");

                WriteImportModuleResolversMethod(writer, container);
                WriteCombineResolversMethods(writer, container);

            }
            else
            {
                writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>> _serviceResolvers = _localResolvers.ToFrozenDictionary();");
            }
        }

        writer.WriteLine();
        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes <c>__ImportModuleResolvers&lt;TModule&gt;</c>, which wraps an imported module's resolvers to resolve with the
    /// module instance of this container (scope). The <c>IEnumerable&lt;T&gt;</c> entries of the module's generated
    /// collections (an <c>IocCollectionResolver</c>, under any key) resolve the collection's array instead, which never merges,
    /// and, when this container integrates the fallback provider, merge its fallback services under the same key (non-keyed,
    /// a service key, or <c>KeyedService.AnyKey</c>). A collection resolved through the importer therefore merges once, with
    /// the importer's fallback provider and fallback scope, whatever the module's own <c>IntegrateServiceProvider</c> (see
    /// Container.ImportModule). The wrapped entries stay <c>IocCollectionResolver</c>s, so nested imports merge at the
    /// outermost importer. Explicit registrations of <c>IEnumerable&lt;T&gt;</c> are kept as they are. A single-service
    /// entry that is an <c>IocCollectionResolver</c> (combined by a nested importer, see <see cref="WriteCombineResolversMethods"/>)
    /// keeps its array resolver and still resolves the single service.
    /// </summary>
    private static void WriteImportModuleResolversMethod(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine();
        writer.WriteLine($"private static global::System.Collections.Generic.IEnumerable<{ResolverEntryAlias}> {ImportModuleResolversMethodName}<TModule>(global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<TModule, object>>> resolvers, global::System.Func<{container.ContainerTypeName}, TModule> module)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("foreach(var entry in resolvers)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var resolver = entry.Value;");
        writer.WriteLine("if(resolver.Target is global::SourceGen.Ioc.IocCollectionResolver<TModule> collection)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var arrayResolver = collection.ArrayResolver;");
        writer.WriteLine($"global::System.Func<{container.ContainerTypeName}, object> array = c => arrayResolver(module(c));");

        if(container.IntegrateServiceProvider)
        {
            // The fallback services of the same key: null for non-keyed collections, KeyedService.AnyKey for all keyed services.
            // The single service of a nested importer that combines several sources keeps resolving that service.
            writer.WriteLine("var serviceType = entry.Key.ServiceType;");
            writer.WriteLine($"var serviceKey = entry.Key.Key == {NonKeyedServiceKey} ? null : entry.Key.Key;");
            writer.WriteLine($"global::System.Func<{container.ContainerTypeName}, object> resolve = {IsEnumerableTypeMethodName}(serviceType)");
            writer.Indentation++;
            writer.WriteLine($"? c => c.{MergeFallbackServicesMethodName}(serviceType, (global::System.Array)array(c), serviceKey)");
            writer.WriteLine(": c => resolver(module(c));");
            writer.Indentation--;
            writer.WriteLine($"yield return new(entry.Key, new global::SourceGen.Ioc.IocCollectionResolver<{container.ContainerTypeName}>(array, resolve).Resolve);");
        }
        else
        {
            writer.WriteLine($"yield return new(entry.Key, new global::SourceGen.Ioc.IocCollectionResolver<{container.ContainerTypeName}>(array, c => resolver(module(c))).Resolve);");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("yield return new(entry.Key, c => resolver(module(c)));");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes the helpers that build <c>_serviceResolvers</c> of a container importing modules from its sources (each
    /// imported module in import order, then <c>_localResolvers</c>), like <c>MS.DI</c> with the module's registrations added
    /// before the container's (see Container.ImportModule):
    /// <list type="bullet">
    /// <item><c>__CombineResolvers</c>: single services are last-wins (within and across sources). The <c>IEnumerable&lt;T&gt;</c>
    /// entry of each key (including <c>KeyedService.AnyKey</c>) is the last explicit registration of a source, if any;
    /// otherwise, with parts from two or more sources (a source's generated collection array, or its single service of
    /// <c>T</c> under the key), a combined <c>IocCollectionResolver</c> that concatenates them and merges the fallback
    /// services once. A single service registered by two or more sources without any <c>IEnumerable&lt;T&gt;</c> entry
    /// becomes an <c>IocCollectionResolver</c> whose array is the combined collection and which still resolves the
    /// last-wins service, for <c>__ResolveCollection</c> and an outer importer. Value type elements are not combined.</item>
    /// <item><c>__CombineCollection</c>: concatenates the parts into an array of the element type.</item>
    /// <item><c>__IsEnumerableType</c> / <c>__IsCombinedElement</c>: whether a type is <c>IEnumerable&lt;T&gt;</c>, and
    /// whether a single service is a collection element (not <c>KeyedService.AnyKey</c>, a value type, <c>Task&lt;T&gt;</c>,
    /// <c>IServiceProvider</c> / <c>IServiceScopeFactory</c>, which every source registers as itself, or a collection type
    /// that a source generates: <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyCollection&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>T[]</c>).</item>
    /// </list>
    /// </summary>
    private static void WriteCombineResolversMethods(SourceWriter writer, ContainerModel container)
    {
        const string identifier = "global::SourceGen.Ioc.ServiceIdentifier";
        var resolverType = $"global::System.Func<{container.ContainerTypeName}, object>";
        var collectionResolverType = $"global::SourceGen.Ioc.IocCollectionResolver<{container.ContainerTypeName}>";
        var dictionaryType = $"global::System.Collections.Generic.Dictionary<{identifier}, {resolverType}>";

        // __CombineResolvers
        writer.WriteLine();
        writer.WriteLine($"private static global::System.Collections.Frozen.FrozenDictionary<{identifier}, {resolverType}> {CombineResolversMethodName}(global::System.Collections.Generic.IEnumerable<{ResolverEntryAlias}>[] sources)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"var resolvers = new {dictionaryType}();");
        writer.WriteLine($"var sourceResolvers = new {dictionaryType}[sources.Length];");
        writer.WriteLine($"var collectionElements = new global::System.Collections.Generic.HashSet<{identifier}>();");
        writer.WriteLine("for(var i = 0; i < sources.Length; i++)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"var source = sourceResolvers[i] = new {dictionaryType}();");
        writer.WriteLine("foreach(var entry in sources[i])");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("source[entry.Key] = entry.Value;");
        writer.WriteLine("resolvers[entry.Key] = entry.Value;");
        writer.WriteLine($"if({IsEnumerableTypeMethodName}(entry.Key.ServiceType))");
        writer.Indentation++;
        writer.WriteLine($"collectionElements.Add(new {identifier}(entry.Key.ServiceType.GenericTypeArguments[0], entry.Key.Key));");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"var parts = new global::System.Collections.Generic.List<({resolverType} Resolver, bool IsArray)>();");
        writer.WriteLine($"{identifier}[] ids = [.. resolvers.Keys];");
        writer.WriteLine("foreach(var id in ids)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"var isCollection = {IsEnumerableTypeMethodName}(id.ServiceType);");
        writer.WriteLine("var elementType = isCollection ? id.ServiceType.GenericTypeArguments[0] : id.ServiceType;");
        writer.WriteLine($"var elementId = new {identifier}(elementType, id.Key);");
        writer.WriteLine($"var isElement = {IsCombinedElementMethodName}(elementId);");
        writer.WriteLine("if(elementType.IsValueType || (!isCollection && (!isElement || collectionElements.Contains(id))))");
        writer.Indentation++;
        writer.WriteLine("continue;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine("parts.Clear();");
        writer.WriteLine($"{resolverType}? explicitResolver = null;");
        writer.WriteLine("foreach(var source in sourceResolvers)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(isCollection && source.TryGetValue(id, out var resolver))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"if(resolver.Target is {collectionResolverType} collection)");
        writer.Indentation++;
        writer.WriteLine("parts.Add((collection.ArrayResolver, true));");
        writer.Indentation--;
        writer.WriteLine("else");
        writer.Indentation++;
        writer.WriteLine("explicitResolver = resolver;");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("else if(isElement && source.TryGetValue(elementId, out var element))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"parts.Add(element.Target is {collectionResolverType} combined ? (combined.ArrayResolver, true) : (element, false));");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("if(explicitResolver is not null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("resolvers[id] = explicitResolver;");
        writer.WriteLine("continue;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("if(parts.Count < 2)");
        writer.Indentation++;
        writer.WriteLine("continue;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine("var combinedParts = parts.ToArray();");
        writer.WriteLine($"{resolverType} array = c => {CombineCollectionMethodName}(elementType, combinedParts, c);");
        writer.WriteLine($"{resolverType} resolve = resolvers[id];");
        writer.WriteLine("if(isCollection)");
        writer.WriteLine("{");
        writer.Indentation++;
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("var serviceType = id.ServiceType;");
            writer.WriteLine($"var serviceKey = id.Key == {NonKeyedServiceKey} ? null : id.Key;");
            writer.WriteLine($"resolve = c => c.{MergeFallbackServicesMethodName}(serviceType, (global::System.Array)array(c), serviceKey);");
        }
        else
        {
            writer.WriteLine("resolve = array;");
        }
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine($"resolvers[id] = new {collectionResolverType}(array, resolve).Resolve;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return resolvers.ToFrozenDictionary();");
        writer.Indentation--;
        writer.WriteLine("}");

        // __CombineCollection: Array.CreateInstance is RequiresDynamicCode, but value type elements are never combined
        writer.WriteLine();
        writer.WriteLine("[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AotAnalysis\", \"IL3050:RequiresDynamicCode\", Justification = \"Arrays are only created for reference element types.\")]");
        writer.WriteLine($"private static global::System.Array {CombineCollectionMethodName}(global::System.Type elementType, ({resolverType} Resolver, bool IsArray)[] parts, {container.ContainerTypeName} container)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var values = new object[parts.Length];");
        writer.WriteLine("var length = 0;");
        writer.WriteLine("for(var i = 0; i < parts.Length; i++)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("values[i] = parts[i].Resolver(container);");
        writer.WriteLine("length += parts[i].IsArray ? ((global::System.Array)values[i]).Length : 1;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("var services = global::System.Array.CreateInstance(elementType, length);");
        writer.WriteLine("var index = 0;");
        writer.WriteLine("for(var i = 0; i < parts.Length; i++)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(parts[i].IsArray)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var array = (global::System.Array)values[i];");
        writer.WriteLine("array.CopyTo(services, index);");
        writer.WriteLine("index += array.Length;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("services.SetValue(values[i], index++);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return services;");
        writer.Indentation--;
        writer.WriteLine("}");

        // __IsEnumerableType / __IsCombinedElement
        writer.WriteLine();
        writer.WriteLine($"private static bool {IsEnumerableTypeMethodName}(global::System.Type type) =>");
        writer.Indentation++;
        writer.WriteLine("type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(global::System.Collections.Generic.IEnumerable<>);");
        writer.Indentation--;
        writer.WriteLine();
        // Every source registers itself as IServiceProvider / IServiceScopeFactory and its generated collection types,
        // which are not services to combine (MS.DI has a single IServiceProvider)
        writer.WriteLine($"private static bool {IsCombinedElementMethodName}({identifier} id)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var type = id.ServiceType;");
        writer.WriteLine($"if(id.Key == {KeyedServiceAnyKey} || type.IsValueType || type.IsArray || type == typeof(global::System.IServiceProvider) || type == typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory))");
        writer.Indentation++;
        writer.WriteLine("return false;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine("if(!type.IsConstructedGenericType)");
        writer.Indentation++;
        writer.WriteLine("return true;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine("var definition = type.GetGenericTypeDefinition();");
        writer.WriteLine("return definition != typeof(global::System.Threading.Tasks.Task<>)");
        writer.Indentation++;
        writer.WriteLine("&& definition != typeof(global::System.Collections.Generic.IEnumerable<>)");
        writer.WriteLine("&& definition != typeof(global::System.Collections.Generic.IReadOnlyCollection<>)");
        writer.WriteLine("&& definition != typeof(global::System.Collections.Generic.ICollection<>)");
        writer.WriteLine("&& definition != typeof(global::System.Collections.Generic.IReadOnlyList<>)");
        writer.WriteLine("&& definition != typeof(global::System.Collections.Generic.IList<>);");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes the nested ServiceProviderFactory type, which implements IServiceProviderFactory.
    /// A separate type is used so that no container instance (and no eager service) is created just to build one.
    /// </summary>
    private static void WriteServiceProviderFactory(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IServiceProviderFactory<IServiceCollection>");
        writer.WriteLine();

        // Provider built by ServiceProviderFactory.CreateServiceProvider is owned (and disposed) by the returned container
        writer.WriteLine("private object? _ownedFallbackProvider;");
        writer.WriteLine();

        writer.WriteLine("/// <summary>");
        writer.WriteLine($"/// Builds a <see cref=\"{container.ClassName}\"/> from an IServiceCollection, which becomes its fallback service provider.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("public sealed class ServiceProviderFactory : global::Microsoft.Extensions.DependencyInjection.IServiceProviderFactory<global::Microsoft.Extensions.DependencyInjection.IServiceCollection>");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Creates a new container builder (returns the same IServiceCollection).");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("public global::Microsoft.Extensions.DependencyInjection.IServiceCollection CreateBuilder(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;");
        writer.WriteLine();

        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Creates the service provider from the built IServiceCollection.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("public global::System.IServiceProvider CreateServiceProvider(global::Microsoft.Extensions.DependencyInjection.IServiceCollection containerBuilder)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var fallbackProvider = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(containerBuilder);");
        writer.WriteLine($"return new {container.ClassName}(fallbackProvider) {{ _ownedFallbackProvider = fallbackProvider }};");
        writer.Indentation--;
        writer.WriteLine("}");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }
}
