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
                WriteCollectionResolverEntries(writer, collection.ElementServiceTypeName, collection.ArrayMethodName);
            }
        }

        // Add local resolvers for wrapper entries (KVP first, then Lazy, then Func)
        WriteWrapperLocalResolverEntries(writer, groups);

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
                writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>> _serviceResolvers =");
                writer.Indentation++;

                var isFirst = true;
                foreach(var module in container.ImportedModules)
                {
                    var fieldName = GetModuleFieldName(module.Type.Name);

                    // Every module after the first is appended with .Concat(...)
                    if(container.IntegrateServiceProvider)
                    {
                        writer.WriteLine($"{(isFirst ? "" : ".Concat(")}{ImportModuleResolversMethodName}({module.Type.Name}.Resolvers, static c => c.{fieldName}){(isFirst ? "" : ")")}");
                    }
                    else
                    {
                        writer.WriteLine($"{(isFirst ? "" : ".Concat(")}{module.Type.Name}.Resolvers.Select(static kvp => new global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>>(kvp.Key, c => kvp.Value(c.{fieldName}))){(isFirst ? "" : ")")}");
                    }

                    isFirst = false;
                }

                writer.WriteLine(".Concat(_localResolvers)");
                writer.WriteLine(".ToFrozenDictionary();");
                writer.Indentation--;

                if(container.IntegrateServiceProvider)
                {
                    WriteImportModuleResolversMethod(writer, container);
                }
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
    /// module instance of this container (scope). Its non-keyed <c>IEnumerable&lt;T&gt;</c> entries resolve the module's
    /// <c>T[]</c> entry instead, which never merges, and merge the fallback services of this container, so a collection
    /// resolved through the importer merges once, with the importer's fallback provider and fallback scope, whatever the
    /// module's own <c>IntegrateServiceProvider</c> (see Container.ImportModule). Value types (<c>KeyValuePair</c>
    /// collections, which are not merged) and modules without a <c>T[]</c> entry keep the module's entry.
    /// </summary>
    private static void WriteImportModuleResolversMethod(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine();
        writer.WriteLine($"private static global::System.Collections.Generic.IEnumerable<{ResolverEntryAlias}> {ImportModuleResolversMethodName}<TModule>(global::System.Collections.Generic.IReadOnlyCollection<global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<TModule, object>>> resolvers, global::System.Func<{container.ContainerTypeName}, TModule> module)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var arrayResolvers = new global::System.Collections.Generic.Dictionary<global::System.Type, global::System.Func<TModule, object>>();");
        writer.WriteLine("foreach(var entry in resolvers)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"if(entry.Key.ServiceType.IsArray && entry.Key.Key == {KeyedServiceAnyKey})");
        writer.Indentation++;
        writer.WriteLine("arrayResolvers[entry.Key.ServiceType.GetElementType()!] = entry.Value;");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("foreach(var entry in resolvers)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var serviceType = entry.Key.ServiceType;");
        writer.WriteLine("var resolver = entry.Value;");
        writer.WriteLine($"if(entry.Key.Key == {KeyedServiceAnyKey}");
        writer.Indentation++;
        writer.WriteLine("&& serviceType.IsConstructedGenericType");
        writer.WriteLine("&& serviceType.GetGenericTypeDefinition() == typeof(global::System.Collections.Generic.IEnumerable<>)");
        writer.WriteLine("&& !serviceType.GenericTypeArguments[0].IsValueType");
        writer.WriteLine("&& arrayResolvers.TryGetValue(serviceType.GenericTypeArguments[0], out var arrayResolver))");
        writer.Indentation--;
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"yield return new(entry.Key, c => c.{MergeFallbackServicesMethodName}(serviceType, (global::System.Array)arrayResolver(module(c)), null));");
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
