using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{

    /// <summary>
    /// Writes IServiceProvider implementation.
    /// </summary>
    private static void WriteIServiceProviderImplementation(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region IServiceProvider");
        writer.WriteLine();

        writer.WriteLine("public object? GetService(global::System.Type serviceType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine();

        if(effectiveUseSwitchStatement)
        {
            // Built-in services (switch mode only - in dictionary mode these are in _localResolvers)
            writer.WriteLine("if(serviceType == typeof(global::System.IServiceProvider)) return this;");
            writer.WriteLine("if(serviceType == typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) return this;");
            writer.WriteLine($"if(serviceType == typeof({container.ClassName})) return this;");
            writer.WriteLine();

            // Cascading if statements - registrations already filtered (no open generics)
            foreach(var mapping in EnumerateInterfaceServiceResolvers(groups))
            {
                if(mapping.Key is not null)
                    continue;

                writer.WriteLine($"if(serviceType == typeof({mapping.ServiceType})) return {mapping.ResolverExpression};");
            }

            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"if(_serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, {KeyedServiceAnyKey}), out var resolver))");
            writer.Indentation++;
            writer.WriteLine("return resolver(this);");
            writer.Indentation--;
            writer.WriteLine();
        }

        // Fallback
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("return GetFallbackServiceProvider()?.GetService(serviceType);");
        }
        else
        {
            writer.WriteLine("return null;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        if(container.IntegrateServiceProvider)
        {
            WriteFallbackServiceProviderMethods(writer);
        }

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the fallback provider accessor. Container scopes create the matching fallback scope
    /// only when a service is first resolved from the fallback provider.
    /// </summary>
    private static void WriteFallbackServiceProviderMethods(SourceWriter writer)
    {
        writer.WriteLine("private global::System.IServiceProvider? GetFallbackServiceProvider()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(_isRootScope || _fallbackProvider is null) return _fallbackProvider;");
        writer.WriteLine();
        writer.WriteLine("return (_fallbackScope ?? CreateFallbackScope(_fallbackProvider))?.ServiceProvider ?? _fallbackProvider;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("private global::Microsoft.Extensions.DependencyInjection.IServiceScope? CreateFallbackScope(global::System.IServiceProvider fallbackProvider)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(fallbackProvider.GetService(typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) is not global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory) return null;");
        writer.WriteLine();
        writer.WriteLine("var scope = scopeFactory.CreateScope();");
        writer.WriteLine("var existing = global::System.Threading.Interlocked.CompareExchange(ref _fallbackScope, scope, null);");
        writer.WriteLine("if(existing is null) return scope;");
        writer.WriteLine();
        writer.WriteLine("scope.Dispose();");
        writer.WriteLine("return existing;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes IKeyedServiceProvider implementation.
    /// </summary>
    private static void WriteIKeyedServiceProviderImplementation(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region IKeyedServiceProvider");
        writer.WriteLine();

        writer.WriteLine("public object? GetKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine();

        writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");
        writer.WriteLine();

        if(effectiveUseSwitchStatement)
        {
            // Use pre-computed hasKeyedServices flag
            if(groups.HasKeyedServices)
            {
                // Use tuple pattern matching switch expression
                writer.WriteLine("return (serviceType, key) switch");
                writer.WriteLine("{");
                writer.Indentation++;

                foreach(var mapping in EnumerateKeyedServiceResolvers(groups))
                {
                    if(mapping.Key is null)
                        continue;

                    writer.WriteLine($"(global::System.Type t, object k) when t == typeof({mapping.ServiceType}) && Equals(k, {mapping.Key}) => {mapping.ResolverExpression},");
                }

                // Fallback in switch default case
                if(container.IntegrateServiceProvider)
                {
                    writer.WriteLine("_ => GetFallbackServiceProvider() is global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null");
                }
                else
                {
                    writer.WriteLine("_ => null");
                }

                writer.Indentation--;
                writer.WriteLine("};");
            }
            else
            {
                // No keyed services, just return fallback
                if(container.IntegrateServiceProvider)
                {
                    writer.WriteLine("return GetFallbackServiceProvider() is global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;");
                }
                else
                {
                    writer.WriteLine("return null;");
                }
            }
        }
        else
        {
            writer.WriteLine("if(_serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key), out var resolver))");
            writer.Indentation++;
            writer.WriteLine("return resolver(this);");
            writer.Indentation--;
            writer.WriteLine();

            // Fallback
            if(container.IntegrateServiceProvider)
            {
                writer.WriteLine("return GetFallbackServiceProvider() is global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;");
            }
            else
            {
                writer.WriteLine("return null;");
            }
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // GetRequiredKeyedService
        writer.WriteLine("public object GetRequiredKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine("return GetKeyedService(serviceType, serviceKey) ?? throw new global::System.InvalidOperationException($\"No service for type '{serviceType}' with key '{serviceKey}' has been registered.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes ISupportRequiredService implementation.
    /// </summary>
    private static void WriteISupportRequiredServiceImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region ISupportRequiredService");
        writer.WriteLine();

        writer.WriteLine("public object GetRequiredService(global::System.Type serviceType)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine("return GetService(serviceType) ?? throw new global::System.InvalidOperationException($\"No service for type '{serviceType}' has been registered.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes generic service resolution extension methods matching
    /// ServiceProviderServiceExtensions and ServiceProviderKeyedServiceExtensions signatures.
    /// In dictionary mode, methods directly query _serviceResolvers for optimal performance.
    /// In switch mode, methods delegate to non-generic counterparts.
    /// </summary>
    private static void WriteServiceProviderExtensions(
        SourceWriter writer,
        ContainerModel container,
        bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region ServiceProvider Extensions");
        writer.WriteLine();

        if(effectiveUseSwitchStatement)
        {
            WriteServiceProviderExtensionsSwitchMode(writer);
        }
        else
        {
            WriteServiceProviderExtensionsDictionaryMode(writer, container.IntegrateServiceProvider);
        }

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes generic extension methods for switch/if-cascade mode.
    /// These delegate to the non-generic methods.
    /// </summary>
    private static void WriteServiceProviderExtensionsSwitchMode(SourceWriter writer)
    {
        // GetService<T>
        writer.WriteLine("public T? GetService<T>() where T : class");
        writer.Indentation++;
        writer.WriteLine("=> GetService(typeof(T)) as T;");
        writer.Indentation--;
        writer.WriteLine();

        // GetRequiredService<T>
        writer.WriteLine("public T GetRequiredService<T>() where T : notnull");
        writer.Indentation++;
        writer.WriteLine("=> (T)GetRequiredService(typeof(T));");
        writer.Indentation--;
        writer.WriteLine();

        // GetServices<T>
        writer.WriteLine("public global::System.Collections.Generic.IEnumerable<T> GetServices<T>()");
        writer.Indentation++;
        writer.WriteLine("=> (global::System.Collections.Generic.IEnumerable<T>?)GetService(typeof(global::System.Collections.Generic.IEnumerable<T>)) ?? [];");
        writer.Indentation--;
        writer.WriteLine();

        // GetKeyedService<T>
        writer.WriteLine("public T? GetKeyedService<T>(object? serviceKey) where T : class");
        writer.Indentation++;
        writer.WriteLine("=> GetKeyedService(typeof(T), serviceKey) as T;");
        writer.Indentation--;
        writer.WriteLine();

        // GetRequiredKeyedService<T>
        writer.WriteLine("public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull");
        writer.Indentation++;
        writer.WriteLine("=> (T)GetRequiredKeyedService(typeof(T), serviceKey);");
        writer.Indentation--;
        writer.WriteLine();

        // GetKeyedServices<T>
        writer.WriteLine("public global::System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)");
        writer.Indentation++;
        writer.WriteLine("=> (global::System.Collections.Generic.IEnumerable<T>?)GetKeyedService(typeof(global::System.Collections.Generic.IEnumerable<T>), serviceKey) ?? [];");
        writer.Indentation--;
        writer.WriteLine();
    }

    /// <summary>
    /// Writes generic extension methods for dictionary mode.
    /// These directly query _serviceResolvers FrozenDictionary for optimal performance.
    /// On a miss they fall back exactly like their non-generic counterparts.
    /// </summary>
    private static void WriteServiceProviderExtensionsDictionaryMode(SourceWriter writer, bool integrateServiceProvider)
    {
        // Fallback lookups used on a dictionary miss (null when the container is standalone)
        string? fallback = integrateServiceProvider ? "GetFallbackServiceProvider()" : null;
        string? keyedFallback = integrateServiceProvider ? "(GetFallbackServiceProvider() as global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)" : null;

        // GetService<T>
        WriteGenericResolveMethod(writer,
            "public T? GetService<T>() where T : class",
            keyed: false,
            "typeof(T)",
            "? resolver(this) as T",
            fallback is null ? ": null;" : $": {fallback}?.GetService(typeof(T)) as T;");

        // GetRequiredService<T>
        const string notFound = "throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' has been registered.\")";
        WriteGenericResolveMethod(writer,
            "public T GetRequiredService<T>() where T : notnull",
            keyed: false,
            "typeof(T)",
            "? (T)resolver(this)",
            fallback is null ? $": {notFound};" : $": (T)({fallback}?.GetService(typeof(T)) ?? {notFound});");

        // GetServices<T>
        WriteGenericResolveMethod(writer,
            "public global::System.Collections.Generic.IEnumerable<T> GetServices<T>()",
            keyed: false,
            "typeof(global::System.Collections.Generic.IEnumerable<T>)",
            "? (global::System.Collections.Generic.IEnumerable<T>)resolver(this)",
            fallback is null ? ": [];" : $": {fallback}?.GetService(typeof(global::System.Collections.Generic.IEnumerable<T>)) as global::System.Collections.Generic.IEnumerable<T> ?? [];");

        // GetKeyedService<T>
        WriteGenericResolveMethod(writer,
            "public T? GetKeyedService<T>(object? serviceKey) where T : class",
            keyed: true,
            "typeof(T)",
            "? resolver(this) as T",
            keyedFallback is null ? ": null;" : $": {keyedFallback}?.GetKeyedService(typeof(T), serviceKey) as T;");

        // GetRequiredKeyedService<T>
        const string keyedNotFound = "throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' with key '{serviceKey}' has been registered.\")";
        WriteGenericResolveMethod(writer,
            "public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull",
            keyed: true,
            "typeof(T)",
            "? (T)resolver(this)",
            keyedFallback is null ? $": {keyedNotFound};" : $": (T)({keyedFallback}?.GetKeyedService(typeof(T), serviceKey) ?? {keyedNotFound});");

        // GetKeyedServices<T>
        WriteGenericResolveMethod(writer,
            "public global::System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)",
            keyed: true,
            "typeof(global::System.Collections.Generic.IEnumerable<T>)",
            "? (global::System.Collections.Generic.IEnumerable<T>)resolver(this)",
            keyedFallback is null ? ": [];" : $": {keyedFallback}?.GetKeyedService(typeof(global::System.Collections.Generic.IEnumerable<T>), serviceKey) as global::System.Collections.Generic.IEnumerable<T> ?? [];");
    }

    private static void WriteGenericResolveMethod(
        SourceWriter writer,
        string signature,
        bool keyed,
        string serviceTypeExpr,
        string hitExpr,
        string missExpr)
    {
        writer.WriteLine(signature);
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");

        if(keyed)
        {
            writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");
        }

        var keyExpr = keyed ? "key" : KeyedServiceAnyKey;
        writer.WriteLine($"return _serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier({serviceTypeExpr}, {keyExpr}), out var resolver)");
        writer.Indentation++;
        writer.WriteLine(hitExpr);
        writer.WriteLine(missExpr);
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes IServiceProviderIsService implementation.
    /// </summary>
    private static void WriteIServiceProviderIsServiceImplementation(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region IServiceProviderIsService");
        writer.WriteLine();

        // IsService
        writer.WriteLine("public bool IsService(global::System.Type serviceType)");
        writer.WriteLine("{");
        writer.Indentation++;

        if(effectiveUseSwitchStatement)
        {
            // Built-in services (switch mode only - in dictionary mode these are in _localResolvers)
            writer.WriteLine("if(serviceType == typeof(global::System.IServiceProvider)) return true;");
            writer.WriteLine("if(serviceType == typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) return true;");
            writer.WriteLine($"if(serviceType == typeof({container.ClassName})) return true;");
            writer.WriteLine();

            foreach(var serviceType in groups.AllServiceTypes)
            {
                // Async-init services are exposed as Task<ServiceType>
                var exposedType = groups.LastWinsByServiceType.TryGetValue((serviceType, null), out var entry)
                    ? entry.GetExposedServiceType(serviceType)
                    : serviceType;
                writer.WriteLine($"if(serviceType == typeof({exposedType})) return true;");
            }
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"if(_serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, {KeyedServiceAnyKey}))) return true;");
            writer.WriteLine();
        }

        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("return _fallbackProvider is global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsService isService && isService.IsService(serviceType);");
        }
        else
        {
            writer.WriteLine("return false;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // IsKeyedService
        writer.WriteLine("public bool IsKeyedService(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");

        if(!effectiveUseSwitchStatement)
        {
            writer.WriteLine();
            writer.WriteLine("if(_serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key))) return true;");
        }

        writer.WriteLine();

        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("return _fallbackProvider is global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService isKeyed && isKeyed.IsKeyedService(serviceType, serviceKey);");
        }
        else
        {
            writer.WriteLine("return false;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

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
                    var source = $"{module.Name}.Resolvers.Select(static kvp => new global::System.Collections.Generic.KeyValuePair<global::SourceGen.Ioc.ServiceIdentifier, global::System.Func<{container.ContainerTypeName}, object>>(kvp.Key, c => kvp.Value(c.{fieldName})))";

                    if(isFirst)
                    {
                        writer.WriteLine(source);
                        isFirst = false;
                    }
                    else
                    {
                        writer.WriteLine($".Concat({source})");
                    }
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
    /// Writes IServiceProviderFactory implementation.
    /// </summary>
    private static void WriteIServiceProviderFactoryImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IServiceProviderFactory<IServiceCollection>");
        writer.WriteLine();

        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Creates a new container builder (returns the same IServiceCollection).");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("public global::Microsoft.Extensions.DependencyInjection.IServiceCollection CreateBuilder(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;");
        writer.WriteLine();

        // Provider built by CreateServiceProvider is owned (and disposed) by the returned container
        writer.WriteLine("private object? _ownedFallbackProvider;");
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
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes Disposal implementation (IDisposable and IAsyncDisposable).
    /// </summary>
    private static void WriteDisposalImplementation(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool ownsFallbackProvider)
    {
        writer.WriteLine("#region Disposal");
        writer.WriteLine();

        // IDisposable.Dispose
        writer.WriteLine("public void Dispose()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;");
        writer.WriteLine();
        WriteDisposalBody(writer, container, groups, ownsFallbackProvider, isAsync: false);
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // IAsyncDisposable.DisposeAsync
        writer.WriteLine("public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(global::System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;");
        writer.WriteLine();
        WriteDisposalBody(writer, container, groups, ownsFallbackProvider, isAsync: true);
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        var hasAsyncInitServices = groups.SingletonEntries.Any(static e => e is AsyncContainerEntry)
            || groups.ScopedEntries.Any(static e => e is AsyncContainerEntry);
        WriteDisposalHelperMethods(writer, hasAsyncInitServices);

        writer.WriteLine("#endregion");
    }

    /// <summary>
    /// Writes IControllerActivator implementation using ActivatorUtilities with ObjectFactory caching.
    /// </summary>
    private static void WriteIControllerActivatorImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IControllerActivator");
        writer.WriteLine();

        // Static cache field
        writer.WriteLine("private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Microsoft.Extensions.DependencyInjection.ObjectFactory> _controllerFactoryCache = new();");
        writer.WriteLine("private static global::Microsoft.Extensions.DependencyInjection.ObjectFactory CreateControllerFactory(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(");
        writer.WriteLine("        global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type t)");
        writer.WriteLine("    => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory(t, global::System.Type.EmptyTypes);");
        writer.WriteLine();

        // Create method
        writer.WriteLine("object global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator.Create(global::Microsoft.AspNetCore.Mvc.ControllerContext controllerContext)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(controllerContext);");
        writer.WriteLine("var controllerType = controllerContext.ActionDescriptor.ControllerTypeInfo.AsType();");
        writer.WriteLine("var instance = GetService(controllerType);");
        writer.WriteLine("if (instance is not null) return instance;");
        writer.WriteLine();
        writer.WriteLine("if (!_controllerFactoryCache.TryGetValue(controllerType, out var factory))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("factory = CreateControllerFactory(controllerType);");
        writer.WriteLine("_controllerFactoryCache.TryAdd(controllerType, factory);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return factory(this, []);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Release method
        writer.WriteLine("void global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator.Release(global::Microsoft.AspNetCore.Mvc.ControllerContext context, object controller)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(context);");
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(controller);");
        writer.WriteLine("if (controller is global::System.IDisposable disposable)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("disposable.Dispose();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // ReleaseAsync method
        writer.WriteLine("global::System.Threading.Tasks.ValueTask global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator.ReleaseAsync(global::Microsoft.AspNetCore.Mvc.ControllerContext context, object controller)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(context);");
        writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(controller);");
        writer.WriteLine("if (controller is global::System.IAsyncDisposable asyncDisposable)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("return asyncDisposable.DisposeAsync();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("((global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator)this).Release(context, controller);");
        writer.WriteLine("return default;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes IComponentActivator implementation using ActivatorUtilities with ObjectFactory caching.
    /// </summary>
    private static void WriteIComponentActivatorImplementation(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("#region IComponentActivator");
        writer.WriteLine();

        // Static cache field
        writer.WriteLine("private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Microsoft.Extensions.DependencyInjection.ObjectFactory> _componentFactoryCache = new();");
        writer.WriteLine("private static global::Microsoft.Extensions.DependencyInjection.ObjectFactory CreateComponentFactory(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(");
        writer.WriteLine("        global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type t)");
        writer.WriteLine("    => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateFactory(t, global::System.Type.EmptyTypes);");
        writer.WriteLine();

        // CreateInstance method
        writer.WriteLine("global::Microsoft.AspNetCore.Components.IComponent global::Microsoft.AspNetCore.Components.IComponentActivator.CreateInstance([global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type componentType)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if (!typeof(global::Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(componentType))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("throw new global::System.ArgumentException($\"The type {componentType.FullName} does not implement {nameof(global::Microsoft.AspNetCore.Components.IComponent)}.\", nameof(componentType));");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("var instance = GetService(componentType);");
        writer.WriteLine("if (instance is global::Microsoft.AspNetCore.Components.IComponent component) return component;");
        writer.WriteLine();
        writer.WriteLine("if (!_componentFactoryCache.TryGetValue(componentType, out var factory))");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("factory = CreateComponentFactory(componentType);");
        writer.WriteLine("_componentFactoryCache.TryAdd(componentType, factory);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("return (global::Microsoft.AspNetCore.Components.IComponent)factory(this, []);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    private static void WriteIComponentPropertyActivatorImplementation(SourceWriter writer, ContainerModel container, bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region IComponentPropertyActivator");
        writer.WriteLine();

        // Static cache field
        writer.WriteLine("private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent>> _propertyActivatorCache = new();");
        writer.WriteLine();

        // GetActivator method - explicit interface implementation
        writer.WriteLine("global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent> global::Microsoft.AspNetCore.Components.IComponentPropertyActivator.GetActivator(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] global::System.Type componentType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("if (!_propertyActivatorCache.TryGetValue(componentType, out var activator))");
        writer.WriteLine("{");
        writer.Indentation++;

        // No-op optimization: only when also implementing IComponentActivator
        if(container.ImplementComponentActivator)
        {
            if(effectiveUseSwitchStatement)
            {
                writer.WriteLine("activator = global::System.Array.Exists(_localResolvers, e => e.Key.Equals(new global::SourceGen.Ioc.ServiceIdentifier(componentType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey)))");
            }
            else
            {
                writer.WriteLine("activator = _serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(componentType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey))");
            }
            writer.WriteLine("    ? static (_, _) => { }");
            writer.WriteLine("    : CreateComponentPropertyInjector(componentType);");
        }
        else
        {
            writer.WriteLine("activator = CreateComponentPropertyInjector(componentType);");
        }

        writer.WriteLine("_propertyActivatorCache.TryAdd(componentType, activator);");
        writer.Indentation--;
        writer.WriteLine("}");

        writer.WriteLine("return activator;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // CreateComponentPropertyInjector - reflection fallback method
        WriteCreateComponentPropertyInjectorMethod(writer);

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    private static void WriteCreateComponentPropertyInjectorMethod(SourceWriter writer)
    {
        writer.WriteLine("private static global::System.Action<global::System.IServiceProvider, global::Microsoft.AspNetCore.Components.IComponent> CreateComponentPropertyInjector(");
        writer.WriteLine("    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] global::System.Type componentType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("const global::System.Reflection.BindingFlags flags = global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic;");
        writer.WriteLine("global::System.Collections.Generic.List<(global::System.Reflection.PropertyInfo Property, object? Key)>? injectables = null;");
        writer.WriteLine();

        // Walk inheritance chain
        writer.WriteLine("for (var type = componentType; type is not null; type = type.BaseType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("foreach (var property in type.GetProperties(flags))");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("if (property.DeclaringType != type) continue;");
        writer.WriteLine("var injectAttr = property.GetCustomAttributes(true)");
        writer.WriteLine("    .FirstOrDefault(a => a.GetType().Name is \"InjectAttribute\" or \"IocInjectAttribute\");");
        writer.WriteLine("if (injectAttr is null) continue;");
        writer.WriteLine();
        writer.WriteLine("var keyProp = injectAttr.GetType().GetProperty(\"Key\");");
        writer.WriteLine("var key = keyProp?.GetValue(injectAttr);");
        writer.WriteLine("injectables ??= new();");
        writer.WriteLine("injectables.Add((property, key));");

        writer.Indentation--;
        writer.WriteLine("}");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Return no-op if no injectables found
        writer.WriteLine("if (injectables is null) return static (_, _) => { };");
        writer.WriteLine();

        // Return injection delegate
        writer.WriteLine("return (serviceProvider, component) =>");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("foreach (var (property, serviceKey) in injectables)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("object? value;");
        writer.WriteLine("if (serviceKey is not null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if (serviceProvider is not global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyedProvider)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("throw new global::System.InvalidOperationException($\"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. The service provider does not implement 'IKeyedServiceProvider'.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("value = keyedProvider.GetRequiredKeyedService(property.PropertyType, serviceKey);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("else");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("value = serviceProvider.GetService(property.PropertyType) ?? throw new global::System.InvalidOperationException($\"Cannot provide a value for property '{property.Name}' on type '{componentType.FullName}'. There is no registered service of type '{property.PropertyType}'.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("property.SetValue(component, value);");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("};");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the __HotReloadHandler nested class for hot reload cache invalidation.
    /// </summary>
    private static void WriteHotReloadHandler(SourceWriter writer, ContainerModel container)
    {
        writer.WriteLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        writer.WriteLine(GeneratedCodeAttribute);
        writer.WriteLine("internal static class __HotReloadHandler");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("public static void ClearCache(global::System.Type[]? _)");
        writer.WriteLine("{");
        writer.Indentation++;
        if(container.ImplementComponentActivator)
        {
            writer.WriteLine("_componentFactoryCache.Clear();");
        }
        if(container.ImplementComponentPropertyActivator)
        {
            writer.WriteLine("_propertyActivatorCache.Clear();");
        }
        writer.Indentation--;
        writer.WriteLine("}");
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes the disposal body for both sync and async disposal methods.
    /// </summary>
    private static void WriteDisposalBody(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool ownsFallbackProvider,
        bool isAsync)
    {
        // Scoped services first, in both scopes and the root (the root caches scoped services resolved from it)
        WriteServiceDisposalCalls(writer, groups.ScopedEntries, isAsync);

        writer.WriteLine("if(!_isRootScope)");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteModuleDisposalCalls(writer, container.ImportedModules, isAsync);

        // Fallback scope last: container services may depend on its services
        if(container.IntegrateServiceProvider)
        {
            WriteDisposeCall(writer, "_fallbackScope", isAsync);
        }

        writer.WriteLine("return;");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Root scope disposal
        WriteServiceDisposalCalls(writer, groups.SingletonEntries, isAsync);
        WriteModuleDisposalCalls(writer, container.ImportedModules, isAsync);

        if(ownsFallbackProvider)
        {
            WriteDisposeCall(writer, "_ownedFallbackProvider", isAsync);
        }
    }

    /// <summary>
    /// Writes disposal calls for services in reverse registration order.
    /// </summary>
    private static void WriteServiceDisposalCalls(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerEntry> services,
        bool isAsync)
    {
        for(var i = services.Length - 1; i >= 0; i--)
        {
            services[i].WriteDisposal(writer, isAsync);
        }
    }

    /// <summary>
    /// Writes disposal calls for imported modules.
    /// </summary>
    private static void WriteModuleDisposalCalls(
        SourceWriter writer,
        ImmutableEquatableArray<TypeData> modules,
        bool isAsync)
    {
        foreach(var module in modules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            if(isAsync)
            {
                writer.WriteLine($"await {fieldName}.DisposeAsync().ConfigureAwait(false);");
            }
            else
            {
                writer.WriteLine($"{fieldName}.Dispose();");
            }
        }
    }

    /// <summary>
    /// Writes a sync or async disposal statement for a service, scope, or provider field.
    /// </summary>
    private static void WriteDisposeCall(SourceWriter writer, string target, bool isAsync)
    {
        if(isAsync)
        {
            writer.WriteLine($"await DisposeServiceAsync({target}).ConfigureAwait(false);");
        }
        else
        {
            writer.WriteLine($"DisposeService({target});");
        }
    }

    /// <summary>
    /// Writes the static helper methods for disposal.
    /// </summary>
    private static void WriteDisposalHelperMethods(SourceWriter writer, bool hasAsyncInitServices)
    {
        // Helper method to throw ObjectDisposedException if disposed
        writer.WriteLine("private void ThrowIfDisposed()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("global::System.ObjectDisposedException.ThrowIf(_disposed != 0, GetType());");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Helper method for async disposal
        writer.WriteLine("private static async global::System.Threading.Tasks.ValueTask DisposeServiceAsync(object? service)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(service is global::System.IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);");
        writer.WriteLine("else if(service is global::System.IDisposable disposable) disposable.Dispose();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Helper method for sync disposal; like Microsoft.Extensions.DependencyInjection, an
        // IAsyncDisposable-only service cannot be disposed synchronously
        writer.WriteLine("private static void DisposeService(object? service)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(service is global::System.IDisposable disposable) disposable.Dispose();");
        writer.WriteLine("else if(service is global::System.IAsyncDisposable) throw new global::System.InvalidOperationException($\"'{service.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.\");");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        if(hasAsyncInitServices)
        {
            // Overload for async-init services stored as Task<T>?
            writer.WriteLine("private static async global::System.Threading.Tasks.ValueTask DisposeServiceAsync<T>(global::System.Threading.Tasks.Task<T>? task)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(task is { IsCompletedSuccessfully: true })");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("await DisposeServiceAsync(await task.ConfigureAwait(false)).ConfigureAwait(false);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("catch(global::System.Exception ex)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("global::SourceGen.Ioc.IocContainerGlobalOptions.OnDisposeException?.Invoke(ex);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();

            writer.WriteLine("private static void DisposeService<T>(global::System.Threading.Tasks.Task<T>? task)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(task is { IsCompletedSuccessfully: true })");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("DisposeService(task.ConfigureAwait(false).GetAwaiter().GetResult());");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("catch(global::System.Exception ex)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("global::SourceGen.Ioc.IocContainerGlobalOptions.OnDisposeException?.Invoke(ex);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
        }
    }

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
