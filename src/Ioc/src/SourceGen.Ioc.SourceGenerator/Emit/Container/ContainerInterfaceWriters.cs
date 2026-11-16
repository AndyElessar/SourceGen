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

            // Generated collection resolvers, which the dictionary mode registers in _localResolvers
            WriteSwitchCollectionBranches(writer, groups, container.IntegrateServiceProvider, isServiceCheck: false);

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

        // A collection without collection resolver, then the fallback
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine($"return {ResolveCollectionMethodName}(serviceType, null) ?? GetFallbackServiceProvider()?.GetService(serviceType);");
        }
        else
        {
            writer.WriteLine($"return {ResolveCollectionMethodName}(serviceType, null);");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        WriteCollectionFallbackMethods(writer, container, effectiveUseSwitchStatement);

        if(container.IntegrateServiceProvider)
        {
            WriteFallbackServiceProviderMethods(writer);
        }

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the fallback provider accessor. Container scopes create the matching fallback scope
    /// only when a service is first resolved from the fallback provider; scopes of an imported module
    /// use the fallback scope of the importing container scope instead.
    /// </summary>
    private static void WriteFallbackServiceProviderMethods(SourceWriter writer)
    {
        writer.WriteLine("private global::System.IServiceProvider? GetFallbackServiceProvider()");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(_isRootScope || _fallbackProvider is null) return _fallbackProvider;");
        writer.WriteLine("if(_fallbackScopeOwner is not null) return _fallbackScopeOwner.GetFallbackServiceProvider();");
        writer.WriteLine();
        writer.WriteLine("return (_fallbackScope ?? CreateFallbackScope(_fallbackProvider))?.ServiceProvider ?? _fallbackProvider;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("private global::Microsoft.Extensions.DependencyInjection.IServiceScope? CreateFallbackScope(global::System.IServiceProvider fallbackProvider)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("ThrowIfDisposed();");
        writer.WriteLine("if(fallbackProvider.GetService(typeof(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)) is not global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory) return null;");
        writer.WriteLine();
        writer.WriteLine("var scope = scopeFactory.CreateScope();");
        writer.WriteLine("var existing = global::System.Threading.Interlocked.CompareExchange(ref _fallbackScope, scope, null);");
        writer.WriteLine("if(existing is not null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("scope.Dispose();");
        writer.WriteLine("return existing;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
        // Dispose sets _disposed before it takes _fallbackScope, so a scope published after Dispose took it
        // is seen here; whichever of Dispose and this method takes the scope out of the field disposes it
        writer.WriteLine("if(global::System.Threading.Volatile.Read(ref _disposed) == 0) return scope;");
        writer.WriteLine();
        // Another resolution may have used the published scope meanwhile (e.g. left an IAsyncDisposable-only
        // service in it), so a disposal error is reported instead of replacing the ObjectDisposedException
        writer.WriteLine("if(global::System.Threading.Interlocked.CompareExchange(ref _fallbackScope, null, scope) == scope)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("scope.Dispose();");
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
        writer.WriteLine();
        writer.WriteLine("throw new global::System.ObjectDisposedException(GetType().FullName);");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the collection helpers that <c>GetService</c>, <c>GetKeyedService</c> and <c>IsService</c> / <c>IsKeyedService</c> use
    /// when no generated collection resolver serves an <c>IEnumerable&lt;T&gt;</c>:
    /// <list type="bullet">
    /// <item><c>__FindCollectionElementResolver</c>: the single-service resolver of <c>T</c> under the same key, unless the key is
    /// <c>KeyedService.AnyKey</c> (all keyed services) or <c>T</c> is a value type or <c>Task&lt;T&gt;</c> (async-init services,
    /// which collections exclude). Switch mode searches <c>_localResolvers</c>, as it has no dictionary.</item>
    /// <item><c>__IsCollectionService</c>: whether that resolver exists.</item>
    /// <item><c>__ResolveCollection</c>: the fallback provider's services of the collection (when integrated) followed by the
    /// element resolved by that resolver. Switch mode first looks for an exact <c>IEnumerable&lt;T&gt;</c> entry, which a
    /// <c>GetKeyedService(…, null)</c> request reaches without the switch.</item>
    /// </list>
    /// </summary>
    private static void WriteCollectionFallbackMethods(SourceWriter writer, ContainerModel container, bool effectiveUseSwitchStatement)
    {
        var resolverType = $"global::System.Func<{container.ContainerTypeName}, object>";

        // __FindCollectionElementResolver
        writer.WriteLine($"private static {resolverType}? {FindCollectionElementResolverMethodName}(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"if(serviceKey == {KeyedServiceAnyKey} || !serviceType.IsConstructedGenericType || serviceType.GetGenericTypeDefinition() != typeof(global::System.Collections.Generic.IEnumerable<>))");
        writer.Indentation++;
        writer.WriteLine("return null;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine("var elementType = serviceType.GenericTypeArguments[0];");
        writer.WriteLine("if(elementType.IsValueType || (elementType.IsConstructedGenericType && elementType.GetGenericTypeDefinition() == typeof(global::System.Threading.Tasks.Task<>)))");
        writer.Indentation++;
        writer.WriteLine("return null;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine($"var elementId = new global::SourceGen.Ioc.ServiceIdentifier(elementType, serviceKey ?? {KeyedServiceAnyKey});");

        if(effectiveUseSwitchStatement)
        {
            writer.WriteLine($"{resolverType}? resolver = null;");
            writer.WriteLine("foreach(var entry in _localResolvers)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(entry.Key == elementId) resolver = entry.Value;");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
            writer.WriteLine("return resolver;");
        }
        else
        {
            writer.WriteLine("return _serviceResolvers.TryGetValue(elementId, out var resolver) ? resolver : null;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // __IsCollectionService
        writer.WriteLine($"private static bool {IsCollectionServiceMethodName}(global::System.Type serviceType, object? serviceKey) => {FindCollectionElementResolverMethodName}(serviceType, serviceKey) is not null;");
        writer.WriteLine();

        // __ResolveCollection: Array.CreateInstance is RequiresDynamicCode, but the array code of a reference element type is shared (as in MS.DI)
        writer.WriteLine("[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"AotAnalysis\", \"IL3050:RequiresDynamicCode\", Justification = \"Arrays are only created for reference element types.\")]");
        writer.WriteLine($"private object? {ResolveCollectionMethodName}(global::System.Type serviceType, object? serviceKey)");
        writer.WriteLine("{");
        writer.Indentation++;

        if(effectiveUseSwitchStatement)
        {
            writer.WriteLine($"var collectionId = new global::SourceGen.Ioc.ServiceIdentifier(serviceType, serviceKey ?? {KeyedServiceAnyKey});");
            writer.WriteLine("foreach(var entry in _localResolvers)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("if(entry.Key == collectionId) return entry.Value(this);");
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
        }

        writer.WriteLine($"if({FindCollectionElementResolverMethodName}(serviceType, serviceKey) is not {{ }} resolver)");
        writer.Indentation++;
        writer.WriteLine("return null;");
        writer.Indentation--;
        writer.WriteLine();
        writer.WriteLine("var elementType = serviceType.GenericTypeArguments[0];");
        writer.WriteLine("var service = resolver(this);");

        if(container.IntegrateServiceProvider)
        {
            // Fallback services first, like registrations added before the container's (see Container.Collections)
            writer.WriteLine("global::System.Collections.Generic.List<object?>? fallbackServices = null;");
            writer.WriteLine($"if({GetFallbackServicesMethodName}(serviceType, serviceKey) is global::System.Collections.IEnumerable enumerable && enumerable is not global::System.Collections.ICollection {{ Count: 0 }})");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("foreach(var item in enumerable)");
            writer.Indentation++;
            writer.WriteLine("(fallbackServices ??= []).Add(item);");
            writer.Indentation--;
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
            writer.WriteLine("var count = fallbackServices?.Count ?? 0;");
            writer.WriteLine("var services = global::System.Array.CreateInstance(elementType, count + 1);");
            writer.WriteLine("for(var i = 0; i < count; i++)");
            writer.Indentation++;
            writer.WriteLine("services.SetValue(fallbackServices![i], i);");
            writer.Indentation--;
            writer.WriteLine();
            writer.WriteLine("services.SetValue(service, count);");
            writer.WriteLine("return services;");
        }
        else
        {
            writer.WriteLine("var services = global::System.Array.CreateInstance(elementType, 1);");
            writer.WriteLine("services.SetValue(service, 0);");
            writer.WriteLine("return services;");
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        if(!container.IntegrateServiceProvider)
            return;

        // __GetFallbackServices: the fallback provider's collection, keyed when a key is given (null means non-keyed, like GetKeyedService)
        writer.WriteLine($"private object? {GetFallbackServicesMethodName}(global::System.Type serviceType, object? serviceKey) =>");
        writer.Indentation++;
        writer.WriteLine("serviceKey is null");
        writer.Indentation++;
        writer.WriteLine("? GetFallbackServiceProvider()?.GetService(serviceType)");
        writer.WriteLine(": (GetFallbackServiceProvider() as global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)?.GetKeyedService(serviceType, serviceKey);");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine();

        // __MergeFallbackServices<T>: the generated collection resolvers' and the generic methods' counterpart
        writer.WriteLine($"private T[] {MergeFallbackServicesMethodName}<T>(T[] services, object? serviceKey) =>");
        writer.Indentation++;
        writer.WriteLine($"{GetFallbackServicesMethodName}(typeof(global::System.Collections.Generic.IEnumerable<T>), serviceKey) is global::System.Collections.Generic.IEnumerable<T> fallbackServices && fallbackServices is not global::System.Collections.Generic.ICollection<T> {{ Count: 0 }}");
        writer.Indentation++;
        writer.WriteLine("? [.. fallbackServices, .. services]");
        writer.WriteLine(": services;");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the switch-mode branches of the generated collection resolvers (the entries the dictionary mode gets from
    /// <c>__CollectionResolvers&lt;T&gt;</c> and the <c>KeyValuePair</c> entries): <c>return …;</c> in <c>GetService</c>, or
    /// <c>return true;</c> in <c>IsService</c>. <c>IEnumerable&lt;T&gt;</c> of a collection resolver merges the fallback services.
    /// </summary>
    private static void WriteSwitchCollectionBranches(SourceWriter writer, ContainerRegistrationGroups groups, bool integrateServiceProvider, bool isServiceCheck)
    {
        foreach(var entry in groups.CollectionEntries)
        {
            if(entry is CollectionContainerEntry collection)
            {
                WriteSwitchArrayCollectionBranches(writer, collection.ElementServiceTypeName, collection.ArrayMethodName, integrateServiceProvider, isServiceCheck);
            }
        }

        foreach(var lazy in groups.LazyWrapperEntries)
        {
            if(lazy.EmitCollectionResolver)
            {
                WriteSwitchArrayCollectionBranches(writer, $"global::System.Lazy<{lazy.InnerServiceTypeName}>", lazy.ArrayMethodName!, integrateServiceProvider, isServiceCheck);
            }
        }

        foreach(var func in groups.FuncWrapperEntries)
        {
            if(func.EmitCollectionResolver)
            {
                WriteSwitchArrayCollectionBranches(writer, $"global::System.Func<{func.InnerServiceTypeName}>", func.ArrayMethodName!, integrateServiceProvider, isServiceCheck);
            }
        }

        foreach(var group in groups.KvpWrapperEntries.GroupBy(static entry => (entry.KeyTypeName, entry.ValueTypeName)))
        {
            var kvp = group.First();
            var kvpType = $"global::System.Collections.Generic.KeyValuePair<{kvp.KeyTypeName}, {kvp.ValueTypeName}>";

            if(isServiceCheck)
            {
                writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IEnumerable<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.IReadOnlyCollection<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.ICollection<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.IReadOnlyList<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.IList<{kvpType}>) || serviceType == typeof({kvpType}[])) return true;");
                writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IReadOnlyDictionary<{kvp.KeyTypeName}, {kvp.ValueTypeName}>) || serviceType == typeof(global::System.Collections.Generic.IDictionary<{kvp.KeyTypeName}, {kvp.ValueTypeName}>) || serviceType == typeof(global::System.Collections.Generic.Dictionary<{kvp.KeyTypeName}, {kvp.ValueTypeName}>)) return true;");
            }
            else
            {
                writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IEnumerable<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.IReadOnlyCollection<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.ICollection<{kvpType}>)) return {kvp.DictionaryMethodName}();");
                writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IReadOnlyList<{kvpType}>) || serviceType == typeof(global::System.Collections.Generic.IList<{kvpType}>) || serviceType == typeof({kvpType}[])) return {kvp.ArrayMethodName}();");
                writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IReadOnlyDictionary<{kvp.KeyTypeName}, {kvp.ValueTypeName}>) || serviceType == typeof(global::System.Collections.Generic.IDictionary<{kvp.KeyTypeName}, {kvp.ValueTypeName}>) || serviceType == typeof(global::System.Collections.Generic.Dictionary<{kvp.KeyTypeName}, {kvp.ValueTypeName}>)) return {kvp.DictionaryMethodName}();");
            }
        }
    }

    private static void WriteSwitchArrayCollectionBranches(SourceWriter writer, string elementType, string arrayMethodName, bool integrateServiceProvider, bool isServiceCheck)
    {
        if(isServiceCheck)
        {
            writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IEnumerable<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.IReadOnlyCollection<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.ICollection<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.IReadOnlyList<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.IList<{elementType}>) || serviceType == typeof({elementType}[])) return true;");
            return;
        }

        if(integrateServiceProvider)
        {
            writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IEnumerable<{elementType}>)) return {MergeFallbackServicesMethodName}({arrayMethodName}(), null);");
        }
        else
        {
            writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IEnumerable<{elementType}>)) return {arrayMethodName}();");
        }

        writer.WriteLine($"if(serviceType == typeof(global::System.Collections.Generic.IReadOnlyCollection<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.ICollection<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.IReadOnlyList<{elementType}>) || serviceType == typeof(global::System.Collections.Generic.IList<{elementType}>) || serviceType == typeof({elementType}[])) return {arrayMethodName}();");
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

        // On a miss: a collection without collection resolver, then the fallback (constants, so nothing is allocated)
        const string resolveCollection = $"{ResolveCollectionMethodName}(serviceType, serviceKey)";
        const string resolveCollectionOrFallback = $"{resolveCollection} ?? (GetFallbackServiceProvider() is global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null)";
        var missExpr = container.IntegrateServiceProvider ? resolveCollectionOrFallback : resolveCollection;

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

                // Miss in switch default case
                writer.WriteLine($"_ => {missExpr}");

                writer.Indentation--;
                writer.WriteLine("};");
            }
            else
            {
                // No keyed services, just the miss
                writer.WriteLine($"return {missExpr};");
            }
        }
        else
        {
            writer.WriteLine("if(_serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, key), out var resolver))");
            writer.Indentation++;
            writer.WriteLine("return resolver(this);");
            writer.Indentation--;
            writer.WriteLine();

            writer.WriteLine($"return {missExpr};");
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

        // A T of IEnumerable<TElement> without collection resolver, like the non-generic methods
        const string resolveCollection = $"{ResolveCollectionMethodName}(typeof(T), null)";
        const string resolveKeyedCollection = $"{ResolveCollectionMethodName}(typeof(T), serviceKey)";

        // GetService<T>
        WriteGenericResolveMethod(writer,
            "public T? GetService<T>() where T : class",
            keyed: false,
            "typeof(T)",
            "? resolver(this) as T",
            integrateServiceProvider ? $": ({resolveCollection} ?? {fallback}?.GetService(typeof(T))) as T;" : $": {resolveCollection} as T;");

        // GetRequiredService<T>
        const string notFound = "throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' has been registered.\")";
        WriteGenericResolveMethod(writer,
            "public T GetRequiredService<T>() where T : notnull",
            keyed: false,
            "typeof(T)",
            "? (T)resolver(this)",
            integrateServiceProvider ? $": (T)({resolveCollection} ?? {fallback}?.GetService(typeof(T)) ?? {notFound});" : $": (T)({resolveCollection} ?? {notFound});");

        // GetServices<T>
        WriteGenericResolveMethod(writer,
            "public global::System.Collections.Generic.IEnumerable<T> GetServices<T>()",
            keyed: false,
            "typeof(global::System.Collections.Generic.IEnumerable<T>)",
            "? (global::System.Collections.Generic.IEnumerable<T>)resolver(this)",
            integrateServiceProvider
                ? $": {ResolveCollectionMethodName}<T>(null) ?? {fallback}?.GetService(typeof(global::System.Collections.Generic.IEnumerable<T>)) as global::System.Collections.Generic.IEnumerable<T> ?? [];"
                : $": {ResolveCollectionMethodName}<T>(null) ?? [];");

        // GetKeyedService<T>
        WriteGenericResolveMethod(writer,
            "public T? GetKeyedService<T>(object? serviceKey) where T : class",
            keyed: true,
            "typeof(T)",
            "? resolver(this) as T",
            integrateServiceProvider ? $": ({resolveKeyedCollection} ?? {keyedFallback}?.GetKeyedService(typeof(T), serviceKey)) as T;" : $": {resolveKeyedCollection} as T;");

        // GetRequiredKeyedService<T>
        const string keyedNotFound = "throw new global::System.InvalidOperationException($\"No service for type '{typeof(T)}' with key '{serviceKey}' has been registered.\")";
        WriteGenericResolveMethod(writer,
            "public T GetRequiredKeyedService<T>(object? serviceKey) where T : notnull",
            keyed: true,
            "typeof(T)",
            "? (T)resolver(this)",
            integrateServiceProvider ? $": (T)({resolveKeyedCollection} ?? {keyedFallback}?.GetKeyedService(typeof(T), serviceKey) ?? {keyedNotFound});" : $": (T)({resolveKeyedCollection} ?? {keyedNotFound});");

        // GetKeyedServices<T>
        WriteGenericResolveMethod(writer,
            "public global::System.Collections.Generic.IEnumerable<T> GetKeyedServices<T>(object? serviceKey)",
            keyed: true,
            "typeof(global::System.Collections.Generic.IEnumerable<T>)",
            "? (global::System.Collections.Generic.IEnumerable<T>)resolver(this)",
            integrateServiceProvider
                ? $": {ResolveCollectionMethodName}<T>(serviceKey) ?? {keyedFallback}?.GetKeyedService(typeof(global::System.Collections.Generic.IEnumerable<T>), serviceKey) as global::System.Collections.Generic.IEnumerable<T> ?? [];"
                : $": {ResolveCollectionMethodName}<T>(serviceKey) ?? [];");

        // The generic counterpart of __ResolveCollection(Type, object?), which needs no Array.CreateInstance
        writer.WriteLine($"private T[]? {ResolveCollectionMethodName}<T>(object? serviceKey) =>");
        writer.Indentation++;
        writer.WriteLine($"serviceKey != {KeyedServiceAnyKey}");
        writer.WriteLine("&& !typeof(T).IsValueType");
        writer.WriteLine("&& !(typeof(T).IsConstructedGenericType && typeof(T).GetGenericTypeDefinition() == typeof(global::System.Threading.Tasks.Task<>))");
        writer.WriteLine($"&& _serviceResolvers.TryGetValue(new global::SourceGen.Ioc.ServiceIdentifier(typeof(T), serviceKey ?? {KeyedServiceAnyKey}), out var resolver)");
        writer.Indentation++;
        if(integrateServiceProvider)
        {
            writer.WriteLine($"? {MergeFallbackServicesMethodName}<T>([(T)resolver(this)], serviceKey)");
        }
        else
        {
            writer.WriteLine("? [(T)resolver(this)]");
        }
        writer.WriteLine(": null;");
        writer.Indentation--;
        writer.Indentation--;
        writer.WriteLine();
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
                var exposedType = groups.LastWinsByServiceType.TryGetValue(new(serviceType, null), out var entry)
                    ? entry.GetExposedServiceType(serviceType)
                    : serviceType;
                writer.WriteLine($"if(serviceType == typeof({exposedType})) return true;");
            }

            WriteSwitchCollectionBranches(writer, groups, container.IntegrateServiceProvider, isServiceCheck: true);
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"if(_serviceResolvers.ContainsKey(new global::SourceGen.Ioc.ServiceIdentifier(serviceType, {KeyedServiceAnyKey}))) return true;");
            writer.WriteLine();
        }

        // An IEnumerable<T> that the container resolves from the single-service resolver of T
        writer.WriteLine($"if({IsCollectionServiceMethodName}(serviceType, null)) return true;");
        writer.WriteLine();

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
        writer.WriteLine($"if({IsCollectionServiceMethodName}(serviceType, serviceKey)) return true;");
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
