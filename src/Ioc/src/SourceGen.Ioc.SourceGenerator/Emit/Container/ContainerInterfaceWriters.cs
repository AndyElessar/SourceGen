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

        writer.WriteLine("public object? GetService(Type serviceType)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine();

        if(effectiveUseSwitchStatement)
        {
            // Built-in services (switch mode only - in dictionary mode these are in _localResolvers)
            writer.WriteLine("if(serviceType == typeof(IServiceProvider)) return this;");
            writer.WriteLine("if(serviceType == typeof(IServiceScopeFactory)) return this;");
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
            writer.WriteLine($"if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, {KeyedServiceAnyKey}), out var resolver))");
            writer.Indentation++;
            writer.WriteLine("return resolver(this);");
            writer.Indentation--;
            writer.WriteLine();
        }

        // Fallback
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("return _fallbackProvider?.GetService(serviceType);");
        }
        else
        {
            writer.WriteLine("return null;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
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

        writer.WriteLine("public object? GetKeyedService(Type serviceType, object? serviceKey)");
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

                    writer.WriteLine($"(Type t, object k) when t == typeof({mapping.ServiceType}) && Equals(k, {mapping.Key}) => {mapping.ResolverExpression},");
                }

                // Fallback in switch default case
                if(container.IntegrateServiceProvider)
                {
                    writer.WriteLine("_ => _fallbackProvider is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null");
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
                    writer.WriteLine("return _fallbackProvider is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;");
                }
                else
                {
                    writer.WriteLine("return null;");
                }
            }
        }
        else
        {
            writer.WriteLine("if(_serviceResolvers.TryGetValue(new ServiceIdentifier(serviceType, key), out var resolver))");
            writer.Indentation++;
            writer.WriteLine("return resolver(this);");
            writer.Indentation--;
            writer.WriteLine();

            // Fallback
            if(container.IntegrateServiceProvider)
            {
                writer.WriteLine("return _fallbackProvider is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;");
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
        writer.WriteLine("public object GetRequiredKeyedService(Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine("return GetKeyedService(serviceType, serviceKey) ?? throw new InvalidOperationException($\"No service for type '{serviceType}' with key '{serviceKey}' has been registered.\");");
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

        writer.WriteLine("public object GetRequiredService(Type serviceType)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine("return GetService(serviceType) ?? throw new InvalidOperationException($\"No service for type '{serviceType}' has been registered.\");");
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
            WriteServiceProviderExtensionsDictionaryMode(writer);
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
        writer.WriteLine("public System.Collections.Generic.IEnumerable<T> GetServices<T>()");
        writer.Indentation++;
        writer.WriteLine("=> (System.Collections.Generic.IEnumerable<T>?)GetService(typeof(System.Collections.Generic.IEnumerable<T>)) ?? [];");
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
        writer.WriteLine("public System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)");
        writer.Indentation++;
        writer.WriteLine("=> (System.Collections.Generic.IEnumerable<T>?)GetKeyedService(typeof(System.Collections.Generic.IEnumerable<T>), serviceKey) ?? [];");
        writer.Indentation--;
        writer.WriteLine();
    }

    /// <summary>
    /// Writes generic extension methods for dictionary mode.
    /// These directly query _serviceResolvers FrozenDictionary for optimal performance.
    /// </summary>
    private static void WriteServiceProviderExtensionsDictionaryMode(SourceWriter writer)
    {
        // GetService<T>
        writer.WriteLine("public T? GetService<T>() where T : class");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), {KeyedServiceAnyKey}), out var resolver)");
        writer.Indentation++;
        writer.WriteLine("? resolver(this) as T");
        writer.WriteLine(": null;");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // GetRequiredService<T>
        writer.WriteLine("public T GetRequiredService<T>() where T : notnull");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), {KeyedServiceAnyKey}), out var resolver)");
        writer.Indentation++;
        writer.WriteLine("? (T)resolver(this)");
        writer.WriteLine(": throw new InvalidOperationException($\"No service for type '{typeof(T)}' has been registered.\");");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // GetServices<T>
        writer.WriteLine("public System.Collections.Generic.IEnumerable<T> GetServices<T>()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(System.Collections.Generic.IEnumerable<T>), {KeyedServiceAnyKey}), out var resolver)");
        writer.Indentation++;
        writer.WriteLine("? (System.Collections.Generic.IEnumerable<T>)resolver(this)");
        writer.WriteLine(": [];");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // GetKeyedService<T>
        writer.WriteLine("public T? GetKeyedService<T>(object? serviceKey) where T : class");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");
        writer.WriteLine("return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), key), out var resolver)");
        writer.Indentation++;
        writer.WriteLine("? resolver(this) as T");
        writer.WriteLine(": null;");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // GetRequiredKeyedService<T>
        writer.WriteLine("public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");
        writer.WriteLine("return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(T), key), out var resolver)");
        writer.Indentation++;
        writer.WriteLine("? (T)resolver(this)");
        writer.WriteLine(": throw new InvalidOperationException($\"No service for type '{typeof(T)}' with key '{serviceKey}' has been registered.\");");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // GetKeyedServices<T>
        writer.WriteLine("public System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");
        writer.WriteLine("return _serviceResolvers.TryGetValue(new ServiceIdentifier(typeof(System.Collections.Generic.IEnumerable<T>), key), out var resolver)");
        writer.Indentation++;
        writer.WriteLine("? (System.Collections.Generic.IEnumerable<T>)resolver(this)");
        writer.WriteLine(": [];");
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
        writer.WriteLine("public bool IsService(Type serviceType)");
        writer.WriteLine("{");
        writer.Indentation++;

        if(effectiveUseSwitchStatement)
        {
            // Built-in services (switch mode only - in dictionary mode these are in _localResolvers)
            writer.WriteLine("if(serviceType == typeof(IServiceProvider)) return true;");
            writer.WriteLine("if(serviceType == typeof(IServiceScopeFactory)) return true;");
            writer.WriteLine($"if(serviceType == typeof({container.ClassName})) return true;");
            writer.WriteLine();

            foreach(var serviceType in groups.AllServiceTypes)
            {
                writer.WriteLine($"if(serviceType == typeof({serviceType})) return true;");
            }
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"if(_serviceResolvers.ContainsKey(new ServiceIdentifier(serviceType, {KeyedServiceAnyKey}))) return true;");
            writer.WriteLine();
        }

        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("return _fallbackProvider is IServiceProviderIsService isService && isService.IsService(serviceType);");
        }
        else
        {
            writer.WriteLine("return false;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // IsKeyedService
        writer.WriteLine("public bool IsKeyedService(Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"var key = serviceKey ?? {KeyedServiceAnyKey};");

        if(!effectiveUseSwitchStatement)
        {
            writer.WriteLine();
            writer.WriteLine("if(_serviceResolvers.ContainsKey(new ServiceIdentifier(serviceType, key))) return true;");
        }

        writer.WriteLine();

        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("return _fallbackProvider is IServiceProviderIsKeyedService isKeyed && isKeyed.IsKeyedService(serviceType, serviceKey);");
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

        writer.WriteLine("public IServiceScope CreateScope()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine($"return new {container.ClassName}(this);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        writer.WriteLine("public AsyncServiceScope CreateAsyncScope() => new(CreateScope());");
        writer.WriteLine();
        writer.WriteLine("IServiceProvider IServiceScope.ServiceProvider => this;");
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
            writer.WriteLine($"public static IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<{container.ContainerTypeName}, object>>> Resolvers => _localResolvers;");
        }
        else
        {
            writer.WriteLine($"public static IReadOnlyCollection<KeyValuePair<ServiceIdentifier, Func<{container.ContainerTypeName}, object>>> Resolvers => _serviceResolvers;");
        }
        writer.WriteLine();

        // Write _localResolvers as static field
        writer.WriteLine($"private static readonly KeyValuePair<ServiceIdentifier, Func<{container.ContainerTypeName}, object>>[] _localResolvers =");
        writer.WriteLine("[");
        writer.Indentation++;

        // Built-in services: IServiceProvider, IServiceScopeFactory, and the container itself
        writer.WriteLine($"new(new ServiceIdentifier(typeof(IServiceProvider), {KeyedServiceAnyKey}), static c => c),");
        writer.WriteLine($"new(new ServiceIdentifier(typeof(IServiceScopeFactory), {KeyedServiceAnyKey}), static c => c),");
        writer.WriteLine($"new(new ServiceIdentifier(typeof({container.ContainerTypeName}), {KeyedServiceAnyKey}), static c => c),");

        var entryByResolverMethodName = BuildEntryByResolverMethodName(groups);

        // Use service type resolver mappings (already filtered for non-open-generics)
        foreach(var mapping in EnumerateLocalResolverMappings(groups))
        {
            var keyExpr = mapping.Key ?? KeyedServiceAnyKey;

            string resolverExpr;
            if(entryByResolverMethodName.TryGetValue(mapping.ResolverMethodName, out var entry))
            {
                resolverExpr = GetResolverExpression(entry);
            }
            else
            {
                // Fallback path for compatibility with any unmapped resolver entries.
                resolverExpr = $"static c => c.{mapping.ResolverMethodName}()";
            }

            writer.WriteLine($"new(new ServiceIdentifier(typeof({mapping.ServiceType}), {keyExpr}), {resolverExpr}),");
        }

        // Add IEnumerable<T>, IReadOnlyCollection<T>, ICollection<T>, IReadOnlyList<T>, IList<T>, T[] entries for collection service types
        foreach(var serviceType in groups.CollectionServiceTypes)
        {
            var methodName = GetArrayResolverMethodName(serviceType);
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IEnumerable<{serviceType}>), {KeyedServiceAnyKey}), static c => c.{methodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyCollection<{serviceType}>), {KeyedServiceAnyKey}), static c => c.{methodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.ICollection<{serviceType}>), {KeyedServiceAnyKey}), static c => c.{methodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IReadOnlyList<{serviceType}>), {KeyedServiceAnyKey}), static c => c.{methodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof(global::System.Collections.Generic.IList<{serviceType}>), {KeyedServiceAnyKey}), static c => c.{methodName}()),");
            writer.WriteLine($"new(new ServiceIdentifier(typeof({serviceType}[]), {KeyedServiceAnyKey}), static c => c.{methodName}()),");
        }

        // Add local resolvers for wrapper entries (KVP first, then Lazy, then Func)
        WriteWrapperLocalResolverEntries(writer, groups.WrapperEntries);

        writer.Indentation--;
        writer.WriteLine("];");

        // Write _serviceResolvers as static field (only when not using switch statement)
        if(!effectiveUseSwitchStatement)
        {
            writer.WriteLine();
            if(container.ImportedModules.Length > 0)
            {
                // Combine with imported modules - wrap module resolvers to pass the correct module instance
                // Use static access (module.Name is the fully qualified type name) for static abstract Resolvers
                writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<ServiceIdentifier, Func<{container.ContainerTypeName}, object>> _serviceResolvers =");
                writer.Indentation++;

                var isFirst = true;
                foreach(var module in container.ImportedModules)
                {
                    var fieldName = GetModuleFieldName(module.Name);
                    var source = $"{module.Name}.Resolvers.Select(static kvp => new KeyValuePair<ServiceIdentifier, Func<{container.ContainerTypeName}, object>>(kvp.Key, c => kvp.Value(c.{fieldName})))";

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
                writer.WriteLine($"private static readonly global::System.Collections.Frozen.FrozenDictionary<ServiceIdentifier, Func<{container.ContainerTypeName}, object>> _serviceResolvers = _localResolvers.ToFrozenDictionary();");
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
        writer.WriteLine("public IServiceCollection CreateBuilder(IServiceCollection services) => services;");
        writer.WriteLine();

        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Creates the service provider from the built IServiceCollection.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("var fallbackProvider = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(containerBuilder);");
        writer.WriteLine($"return new {container.ClassName}(fallbackProvider);");
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
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine("#region Disposal");
        writer.WriteLine();

        // IDisposable.Dispose
        writer.WriteLine("public void Dispose()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(Interlocked.Exchange(ref _disposed, 1) != 0) return;");
        writer.WriteLine();
        WriteDisposalBody(writer, container, groups, isAsync: false);
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // IAsyncDisposable.DisposeAsync
        writer.WriteLine("public async ValueTask DisposeAsync()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(Interlocked.Exchange(ref _disposed, 1) != 0) return;");
        writer.WriteLine();
        WriteDisposalBody(writer, container, groups, isAsync: true);
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
                writer.WriteLine("activator = global::System.Array.Exists(_localResolvers, e => e.Key.Equals(new ServiceIdentifier(componentType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey)))");
            }
            else
            {
                writer.WriteLine("activator = _serviceResolvers.ContainsKey(new ServiceIdentifier(componentType, global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey))");
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
        bool isAsync)
    {
        // Dispose scoped services if this is a scope
        writer.WriteLine("if(!_isRootScope)");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteDisposalCalls(writer, groups.ScopedEntries, container.ImportedModules, isAsync);
        writer.WriteLine("return;");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Root scope disposal
        WriteDisposalCalls(writer, groups.SingletonEntries, container.ImportedModules, isAsync);
    }

    /// <summary>
    /// Writes disposal calls for services and modules.
    /// </summary>
    private static void WriteDisposalCalls(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerEntry> services,
        ImmutableEquatableArray<TypeData> modules,
        bool isAsync)
    {
        for(var i = services.Length - 1; i >= 0; i--)
        {
            services[i].WriteDisposal(writer, isAsync);
        }

        var moduleMethod = isAsync
            ? "await {0}.DisposeAsync()"
            : "{0}.Dispose()";

        foreach(var module in modules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            writer.WriteLine(string.Format(moduleMethod, fieldName) + ";");
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
        writer.WriteLine("ObjectDisposedException.ThrowIf(_disposed != 0, GetType());");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Helper method for async disposal
        writer.WriteLine("private static async ValueTask DisposeServiceAsync(object? service)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(service is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();");
        writer.WriteLine("else if(service is IDisposable disposable) disposable.Dispose();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Helper method for sync disposal
        writer.WriteLine("private static void DisposeService(object? service)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(service is IDisposable disposable) disposable.Dispose();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        if(hasAsyncInitServices)
        {
            // Overload for async-init services stored as Task<T>?
            writer.WriteLine("private static async ValueTask DisposeServiceAsync<T>(Task<T>? task)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(task is { IsCompletedSuccessfully: true })");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("try");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("await DisposeServiceAsync(await task);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine("catch(Exception ex)");
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

            writer.WriteLine("private static void DisposeService<T>(Task<T>? task)");
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
            writer.WriteLine("catch(Exception ex)");
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

            yield return (serviceEntry.Registration.ServiceType.Name, kvp.Key.Key, GetInterfaceResolverExpression(kvp.Value));
        }
    }

    private static IEnumerable<(string ServiceType, string? Key, string ResolverMethodName)> EnumerateLocalResolverMappings(ContainerRegistrationGroups groups)
    {
        foreach(var kvp in groups.LastWinsByServiceType)
        {
            if(!TryGetResolverMethodName(kvp.Value, out var resolverMethodName))
            {
                continue;
            }

            yield return (kvp.Key.ServiceType, kvp.Key.Key, resolverMethodName);
        }
    }

    private static IEnumerable<(string ServiceType, string? Key, string ResolverExpression)> EnumerateKeyedServiceResolvers(ContainerRegistrationGroups groups)
    {
        foreach(var kvp in groups.LastWinsByServiceType)
        {
            if(kvp.Key.Key is null)
            {
                continue;
            }

            yield return (kvp.Key.ServiceType, kvp.Key.Key, GetInterfaceResolverExpression(kvp.Value));
        }
    }

    private static void WriteWrapperLocalResolverEntries(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerEntry> wrapperEntries)
    {
        var kvpEntries = wrapperEntries
            .OfType<KvpWrapperContainerEntry>()
            .ToList();

        if(kvpEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// KeyValuePair resolvers");

            foreach(var group in kvpEntries.GroupBy(static entry => (entry.KeyTypeName, entry.ValueTypeName)))
            {
                group.First().WriteLocalResolverEntries(writer);
            }
        }

        var lazyEntries = wrapperEntries
            .OfType<LazyWrapperContainerEntry>()
            .ToList();

        if(lazyEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Lazy wrapper resolvers");

            foreach(var group in lazyEntries.GroupBy(static entry => entry.InnerServiceTypeName))
            {
                group.Last().WriteLocalResolverEntries(writer);
            }
        }

        var funcEntries = wrapperEntries
            .OfType<FuncWrapperContainerEntry>()
            .ToList();

        if(funcEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Func wrapper resolvers");

            foreach(var group in funcEntries.GroupBy(static entry => entry.InnerServiceTypeName))
            {
                group.Last().WriteLocalResolverEntries(writer);
            }
        }
    }

    private static Dictionary<string, ContainerEntry> BuildEntryByResolverMethodName(ContainerRegistrationGroups groups)
    {
        var map = new Dictionary<string, ContainerEntry>(StringComparer.Ordinal);

        foreach(var entry in groups.SingletonEntries)
            AddEntryByResolverMethodName(map, entry);
        foreach(var entry in groups.ScopedEntries)
            AddEntryByResolverMethodName(map, entry);
        foreach(var entry in groups.TransientEntries)
            AddEntryByResolverMethodName(map, entry);

        return map;
    }

    private static void AddEntryByResolverMethodName(Dictionary<string, ContainerEntry> map, ContainerEntry entry)
    {
        if(!TryGetResolverMethodName(entry, out var resolverMethodName))
            return;

        if(!map.ContainsKey(resolverMethodName))
        {
            map[resolverMethodName] = entry;
        }
    }

    private static bool TryGetResolverMethodName(ContainerEntry entry, out string resolverMethodName)
    {
        switch(entry)
        {
            case InstanceContainerEntry instance:
                resolverMethodName = instance.ResolverMethodName;
                return true;
            case EagerContainerEntry eager:
                resolverMethodName = eager.ResolverMethodName;
                return true;
            case LazyThreadSafeContainerEntry lazy:
                resolverMethodName = lazy.ResolverMethodName;
                return true;
            case TransientContainerEntry transient:
                resolverMethodName = transient.ResolverMethodName;
                return true;
            case AsyncContainerEntry asyncSingletonOrScoped:
                resolverMethodName = asyncSingletonOrScoped.ResolverMethodName;
                return true;
            case AsyncTransientContainerEntry asyncTransient:
                resolverMethodName = asyncTransient.ResolverMethodName;
                return true;
            default:
                resolverMethodName = string.Empty;
                return false;
        }
    }

    private static string GetResolverExpression(ContainerEntry entry)
    {
        return entry switch
        {
            InstanceContainerEntry instance => $"static _ => {instance.Registration.Instance}",
            EagerContainerEntry eager => $"static c => c.{eager.FieldName}!",
            LazyThreadSafeContainerEntry lazy => $"static c => c.{lazy.ResolverMethodName}()",
            TransientContainerEntry transient => $"static c => c.{transient.ResolverMethodName}()",
            AsyncContainerEntry asyncSingletonOrScoped => $"static c => c.{GetAsyncResolverMethodName(asyncSingletonOrScoped.ResolverMethodName)}()",
            AsyncTransientContainerEntry asyncTransient => $"static c => c.{GetAsyncCreateMethodName(asyncTransient.ResolverMethodName)}()",
            _ => throw new InvalidOperationException($"Unsupported container entry type: {entry.GetType().Name}")
        };
    }

    private static string GetInterfaceResolverExpression(ContainerEntry entry)
    {
        return entry switch
        {
            InstanceContainerEntry instance => instance.Registration.Instance!,
            AsyncContainerEntry asyncSingletonOrScoped => $"{GetAsyncResolverMethodName(asyncSingletonOrScoped.ResolverMethodName)}()",
            AsyncTransientContainerEntry asyncTransient => $"{GetAsyncCreateMethodName(asyncTransient.ResolverMethodName)}()",
            ServiceContainerEntry serviceEntry => $"{serviceEntry.ResolverMethodName}()",
            _ => throw new InvalidOperationException($"Unsupported container entry type: {entry.GetType().Name}")
        };
    }
}
