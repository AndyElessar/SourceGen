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
        writer.WriteLine($"return new {container.ClassName}(this);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("public global::Microsoft.Extensions.DependencyInjection.AsyncServiceScope CreateAsyncScope() => new(CreateScope());");
        writer.WriteLine();
        writer.WriteLine("global::System.IServiceProvider global::Microsoft.Extensions.DependencyInjection.IServiceScope.ServiceProvider => this;");
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
                    var fieldName = GetModuleFieldName(module.Name);

                    // Every module after the first is appended with .Concat(...)
                    writer.WriteLine($"{(isFirst ? "" : ".Concat(")}{module.Name}.Resolvers.Select(static kvp => new global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>>(kvp.Key, c => kvp.Value(c.{fieldName}))){(isFirst ? "" : ")")}");
                    isFirst = false;
                }

                writer.WriteLine(".Concat(_localResolvers)");
                writer.WriteLine(".ToFrozenDictionary();");
                writer.Indentation--;
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
