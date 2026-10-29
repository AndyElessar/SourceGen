using static SourceGen.Ioc.Constants;

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
        // Fallback lookups used on a dictionary miss (only when the container integrates a service provider).
        // They are constants, so each miss expression below is a constant string as well.
        const string fallback = "GetFallbackServiceProvider()";
        const string keyedFallback = "(GetFallbackServiceProvider() as global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)";

        // GetService<T>
        WriteGenericResolveMethod(writer,
            "public T? GetService<T>() where T : class",
            keyed: false,
            "typeof(T)",
            "? resolver(this) as T",
            integrateServiceProvider ? $": {fallback}?.GetService(typeof(T)) as T;" : ": null;");

        // GetRequiredService<T>
        const string notFound = "throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' has been registered.\")";
        WriteGenericResolveMethod(writer,
            "public T GetRequiredService<T>() where T : notnull",
            keyed: false,
            "typeof(T)",
            "? (T)resolver(this)",
            integrateServiceProvider ? $": (T)({fallback}?.GetService(typeof(T)) ?? {notFound});" : $": {notFound};");

        // GetServices<T>
        WriteGenericResolveMethod(writer,
            "public global::System.Collections.Generic.IEnumerable<T> GetServices<T>()",
            keyed: false,
            "typeof(global::System.Collections.Generic.IEnumerable<T>)",
            "? (global::System.Collections.Generic.IEnumerable<T>)resolver(this)",
            integrateServiceProvider ? $": {fallback}?.GetService(typeof(global::System.Collections.Generic.IEnumerable<T>)) as global::System.Collections.Generic.IEnumerable<T> ?? [];" : ": [];");

        // GetKeyedService<T>
        WriteGenericResolveMethod(writer,
            "public T? GetKeyedService<T>(object? serviceKey) where T : class",
            keyed: true,
            "typeof(T)",
            "? resolver(this) as T",
            integrateServiceProvider ? $": {keyedFallback}?.GetKeyedService(typeof(T), serviceKey) as T;" : ": null;");

        // GetRequiredKeyedService<T>
        const string keyedNotFound = "throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' with key '{serviceKey}' has been registered.\")";
        WriteGenericResolveMethod(writer,
            "public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull",
            keyed: true,
            "typeof(T)",
            "? (T)resolver(this)",
            integrateServiceProvider ? $": (T)({keyedFallback}?.GetKeyedService(typeof(T), serviceKey) ?? {keyedNotFound});" : $": {keyedNotFound};");

        // GetKeyedServices<T>
        WriteGenericResolveMethod(writer,
            "public global::System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)",
            keyed: true,
            "typeof(global::System.Collections.Generic.IEnumerable<T>)",
            "? (global::System.Collections.Generic.IEnumerable<T>)resolver(this)",
            integrateServiceProvider ? $": {keyedFallback}?.GetKeyedService(typeof(global::System.Collections.Generic.IEnumerable<T>), serviceKey) as global::System.Collections.Generic.IEnumerable<T> ?? [];" : ": [];");
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
}
