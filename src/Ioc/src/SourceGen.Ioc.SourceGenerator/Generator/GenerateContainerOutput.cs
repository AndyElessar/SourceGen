using static SourceGen.Ioc.SourceGenerator.Models.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
    /// <summary>
    /// Generates the container source code output.
    /// </summary>
    private static void GenerateContainerOutput(
        in SourceProductionContext ctx,
        ContainerWithGroups containerWithGroups,
        string assemblyName,
        MsBuildProperties msbuildProps,
        bool hasDIPackage)
    {
        if((msbuildProps.Features & IocFeatures.Container) == 0)
            return;

        var filteredContainerWithGroups = FilterContainerWithGroupsForFeatures(containerWithGroups, msbuildProps.Features);
        var source = GenerateContainerSource(filteredContainerWithGroups, assemblyName, msbuildProps, hasDIPackage);
        var fileName = $"{containerWithGroups.Container.ClassName}.Container.g.cs";
        ctx.AddSource(fileName, source);
    }

    /// <summary>
    /// Generates the container source code.
    /// </summary>
    private static string GenerateContainerSource(
        ContainerWithGroups containerWithGroups,
        string assemblyName,
        MsBuildProperties msbuildProps,
        bool hasDIPackage)
    {
        var writer = new SourceWriter();
        var container = containerWithGroups.Container;
        var groups = containerWithGroups.Groups;

        // Determine if IServiceProviderFactory should be generated
        // Only generate if IntegrateServiceProvider is true AND the DI package is referenced
        var canGenerateServiceProviderFactory = container.IntegrateServiceProvider && hasDIPackage;

        // Effective UseSwitchStatement: when there are imported modules, always use FrozenDictionary
        // because combining services from multiple sources requires dictionary-based lookup
        var effectiveUseSwitchStatement = container.UseSwitchStatement && container.ImportedModules.Length == 0;

        WriteContainerHeader(writer);

        // Write [assembly: MetadataUpdateHandler] attribute for hot reload cache invalidation
        if(container.ImplementComponentActivator || container.ImplementComponentPropertyActivator)
        {
            writer.WriteLine($"[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof({container.ContainerTypeName}.__HotReloadHandler))]");
            writer.WriteLine();
        }

        WriteContainerNamespaceAndClass(writer, container, groups, canGenerateServiceProviderFactory, effectiveUseSwitchStatement);

        return writer.ToString();
    }

    private static ContainerWithGroups FilterContainerWithGroupsForFeatures(ContainerWithGroups containerWithGroups, IocFeatures features)
    {
        if(IocFeaturesHelper.HasAllInjectionFeatures(features))
            return containerWithGroups;

        var groups = containerWithGroups.Groups;

        var byServiceTypeAndKey = new Dictionary<(string ServiceType, string? Key), ImmutableEquatableArray<CachedRegistration>>();
        foreach(var kvp in groups.ByServiceTypeAndKey)
        {
            byServiceTypeAndKey[kvp.Key] = FilterCachedRegistrations(kvp.Value, features);
        }

        var filteredByServiceTypeAndKey = byServiceTypeAndKey.ToImmutableEquatableDictionary();

        var filteredSingletons = FilterCachedRegistrations(groups.Singletons, features);
        var filteredScoped = FilterCachedRegistrations(groups.Scoped, features);
        var filteredTransients = FilterCachedRegistrations(groups.Transients, features);

        var collectionRegistrations = new Dictionary<string, ImmutableEquatableArray<CachedRegistration>>();
        foreach(var kvp in groups.CollectionRegistrations)
        {
            collectionRegistrations[kvp.Key] = FilterCachedRegistrations(kvp.Value, features);
        }

        var filteredGroups = groups with
        {
            ByServiceTypeAndKey = filteredByServiceTypeAndKey,
            Singletons = filteredSingletons,
            Scoped = filteredScoped,
            Transients = filteredTransients,
            CollectionRegistrations = collectionRegistrations.ToImmutableEquatableDictionary(),
            ReversedSingletonsForDisposal = FilterCachedRegistrations(groups.ReversedSingletonsForDisposal, features),
            ReversedScopedForDisposal = FilterCachedRegistrations(groups.ReversedScopedForDisposal, features),
            EagerSingletons = filteredSingletons.Where(static c => c.IsEager).ToImmutableEquatableArray(),
            EagerScoped = filteredScoped.Where(static c => c.IsEager).ToImmutableEquatableArray(),
            LazyEntries = CollectContainerLazyEntries(filteredSingletons, filteredScoped, filteredTransients, filteredByServiceTypeAndKey),
            FuncEntries = CollectContainerFuncEntries(filteredSingletons, filteredScoped, filteredTransients, filteredByServiceTypeAndKey),
            KvpEntries = CollectContainerKvpEntries(filteredSingletons, filteredScoped, filteredTransients, filteredByServiceTypeAndKey)
        };

        return containerWithGroups with { Groups = filteredGroups };
    }

    private static ImmutableEquatableArray<CachedRegistration> FilterCachedRegistrations(
        ImmutableEquatableArray<CachedRegistration> registrations,
        IocFeatures features)
    {
        if(registrations.Length == 0)
            return registrations;

        List<CachedRegistration>? filteredRegistrations = null;
        for(var i = 0; i < registrations.Length; i++)
        {
            var registration = registrations[i];
            var filteredRegistration = FilterRegistrationForFeatures(registration.Registration, features);
            if(ReferenceEquals(filteredRegistration, registration.Registration))
            {
                if(filteredRegistrations is not null)
                    filteredRegistrations.Add(registration);

                continue;
            }

            filteredRegistrations ??= new List<CachedRegistration>(registrations.Length);
            if(filteredRegistrations.Count == 0)
            {
                for(var j = 0; j < i; j++)
                    filteredRegistrations.Add(registrations[j]);
            }

            filteredRegistrations.Add(registration with { Registration = filteredRegistration, IsAsyncInit = HasAsyncInitMembers(filteredRegistration) });
        }

        return filteredRegistrations is null ? registrations : filteredRegistrations.ToImmutableEquatableArray();
    }

    /// <summary>
    /// Writes the auto-generated header and using directives.
    /// </summary>
    private static void WriteContainerHeader(SourceWriter writer)
    {
        writer.WriteLine(AutoGeneratedHeader);
        writer.WriteLine(NullableEnable);
        writer.WriteLine("#pragma warning disable SGIOCEXP001");
        writer.WriteLine();
        writer.WriteLine("using System;");
        writer.WriteLine("using System.Collections.Frozen;");
        writer.WriteLine("using System.Collections.Generic;");
        writer.WriteLine("using System.Linq;");
        writer.WriteLine("using System.Threading;");
        writer.WriteLine("using System.Threading.Tasks;");
        writer.WriteLine("using Microsoft.Extensions.DependencyInjection;");
        writer.WriteLine("using SourceGen.Ioc;");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the namespace and class declaration with all implemented interfaces.
    /// </summary>
    private static void WriteContainerNamespaceAndClass(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool canGenerateServiceProviderFactory,
        bool effectiveUseSwitchStatement)
    {
        // Write namespace if not global
        bool hasNamespace = !string.IsNullOrEmpty(container.ContainerNamespace);
        if(hasNamespace)
        {
            writer.WriteLine($"namespace {container.ContainerNamespace};");
            writer.WriteLine();
        }

        // Get interface list
        var interfaces = GetContainerInterfaces(container, canGenerateServiceProviderFactory);

        // Write class declaration
        writer.WriteLine($"partial class {container.ClassName} : {string.Join(", ", interfaces)}");
        writer.WriteLine("{");
        writer.Indentation++;

        // Write fields
        WriteContainerFields(writer, container);

        // Write constructors
        WriteContainerConstructors(writer, container, groups, effectiveUseSwitchStatement);

        // Write service resolver methods
        WriteServiceResolverMethods(writer, container.ThreadSafeStrategy, groups);

        // Write partial accessor implementations (user-declared partial methods/properties)
        WritePartialAccessorImplementations(writer, container, groups);

        // Write IServiceProvider implementation
        WriteIServiceProviderImplementation(writer, container, groups, effectiveUseSwitchStatement);

        // Write IKeyedServiceProvider implementation
        WriteIKeyedServiceProviderImplementation(writer, container, groups, effectiveUseSwitchStatement);

        // Write ISupportRequiredService implementation
        WriteISupportRequiredServiceImplementation(writer, container);

        // Write ServiceProvider extension methods (generic overloads)
        WriteServiceProviderExtensions(writer, container, effectiveUseSwitchStatement);

        // Write IServiceProviderIsService implementation
        WriteIServiceProviderIsServiceImplementation(writer, container, groups, effectiveUseSwitchStatement);

        // Write IServiceScopeFactory implementation
        WriteIServiceScopeFactoryImplementation(writer, container);

        // Write IIocContainer implementation
        WriteIIocContainerImplementation(writer, container, groups, effectiveUseSwitchStatement);

        // Write IServiceProviderFactory implementation (if DI package is available)
        if(canGenerateServiceProviderFactory)
        {
            WriteIServiceProviderFactoryImplementation(writer, container);
        }

        // Write Disposal implementation
        WriteDisposalImplementation(writer, container, groups);

        // Write IControllerActivator implementation (if container declares the interface)
        if(container.ImplementControllerActivator)
        {
            WriteIControllerActivatorImplementation(writer, container);
        }

        // Write IComponentActivator implementation (if container declares the interface)
        if(container.ImplementComponentActivator)
        {
            WriteIComponentActivatorImplementation(writer, container);
        }

        // Write IComponentPropertyActivator implementation (if container declares the interface)
        if(container.ImplementComponentPropertyActivator)
        {
            WriteIComponentPropertyActivatorImplementation(writer, container, effectiveUseSwitchStatement);
        }

        // Write hot reload handler (if either component activator interface is implemented)
        if(container.ImplementComponentActivator || container.ImplementComponentPropertyActivator)
        {
            WriteHotReloadHandler(writer, container);
        }

        writer.Indentation--;
        writer.WriteLine("}");
    }

    private static readonly string[] _FixedContainerInterfaces = [
        "IServiceProvider",
        "IKeyedServiceProvider",
        "IServiceProviderIsService",
        "IServiceProviderIsKeyedService",
        "ISupportRequiredService",
        "IServiceScopeFactory",
        "IServiceScope",
        "IDisposable",
        "IAsyncDisposable"
    ];
    /// <summary>
    /// Builds the list of interfaces the container should implement.
    /// </summary>
    private static IEnumerable<string> GetContainerInterfaces(ContainerModel container, bool canGenerateServiceProviderFactory)
    {
        yield return $"IIocContainer<{container.ContainerTypeName}>";

        foreach(var i in _FixedContainerInterfaces)
            yield return i;

        if(canGenerateServiceProviderFactory)
            yield return "IServiceProviderFactory<IServiceCollection>";
    }

    /// <summary>
    /// Writes container fields (fallback provider, service storage, locks, etc.).
    /// </summary>
    private static void WriteContainerFields(
        SourceWriter writer,
        ContainerModel container)
    {
        // Fallback provider field (only if IntegrateServiceProvider is enabled)
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("private readonly IServiceProvider? _fallbackProvider;");
        }

        // Scope tracking
        writer.WriteLine("private readonly bool _isRootScope = true;");
        writer.WriteLine("private int _disposed;");
        writer.WriteLine();

        // Imported module fields
        foreach(var module in container.ImportedModules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            writer.WriteLine($"private readonly {module.Name} {fieldName};");
        }

        if(container.ImportedModules.Length > 0)
        {
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes a service instance field and synchronization field based on ThreadSafeStrategy.
    /// For eager services, fields are non-nullable and no synchronization is needed.
    /// </summary>
    private static void WriteServiceInstanceField(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        ServiceRegistrationModel reg,
        string fieldName,
        bool hasDecorators,
        bool isEager)
    {
        // When there are decorators, field type is ServiceType (interface), otherwise ImplementationType
        var typeName = hasDecorators ? reg.ServiceType.Name : reg.ImplementationType.Name;

        if(isEager)
        {
            // Eager services use non-nullable fields (initialized in constructor)
            // Use null! to suppress CS8618 warning - field will be initialized in constructor
            writer.WriteLine($"private {typeName} {fieldName} = null!;");
            // No synchronization field needed for eager services
            return;
        }

        // Lazy services use nullable fields
        writer.WriteLine($"private {typeName}? {fieldName};");

        // Generate synchronization field based on strategy
        switch(strategy)
        {
            case ThreadSafeStrategy.None:
                // No synchronization field needed
                break;

            case ThreadSafeStrategy.Lock:
                writer.WriteLine($"private readonly Lock {fieldName}Lock = new();");
                break;

            case ThreadSafeStrategy.SemaphoreSlim:
                writer.WriteLine($"private readonly SemaphoreSlim {fieldName}Semaphore = new(1, 1);");
                break;

            case ThreadSafeStrategy.SpinLock:
                // SpinLock must NOT be readonly because Enter/Exit mutate it
                writer.WriteLine($"private SpinLock {fieldName}SpinLock = new(false);");
                break;

            case ThreadSafeStrategy.CompareExchange:
                // No synchronization field needed - uses Interlocked.CompareExchange
                break;
        }
    }

    /// <summary>
    /// Writes container constructors.
    /// </summary>
    private static void WriteContainerConstructors(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool effectiveUseSwitchStatement)
    {
        writer.WriteLine("#region Constructors");
        writer.WriteLine();

        // Default constructor
        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// Creates a new standalone container without external service provider fallback.");
        writer.WriteLine("/// </summary>");

        // Need fallback provider constructor if IntegrateServiceProvider is enabled
        var needsFallbackProvider = container.IntegrateServiceProvider;

        if(needsFallbackProvider)
        {
            writer.WriteLine($"public {container.ClassName}() : this((IServiceProvider?)null) {{ }}");
        }
        else
        {
            // Standalone mode - no fallback provider
            writer.WriteLine($"public {container.ClassName}()");
            writer.WriteLine("{");
            writer.Indentation++;
            WriteConstructorBody(writer, container, groups, hasParameter: false, effectiveUseSwitchStatement);
            writer.Indentation--;
            writer.WriteLine("}");
        }
        writer.WriteLine();

        // Constructor with fallback provider (if enabled)
        if(needsFallbackProvider)
        {
            writer.WriteLine("/// <summary>");
            writer.WriteLine("/// Creates a new container with optional fallback to external service provider.");
            writer.WriteLine("/// </summary>");
            writer.WriteLine("/// <param name=\"fallbackProvider\">Optional external service provider for unknown dependencies.</param>");
            writer.WriteLine($"public {container.ClassName}(IServiceProvider? fallbackProvider)");
            writer.WriteLine("{");
            writer.Indentation++;
            writer.WriteLine("_fallbackProvider = fallbackProvider;");
            WriteConstructorBody(writer, container, groups, hasParameter: true, effectiveUseSwitchStatement);
            writer.Indentation--;
            writer.WriteLine("}");
            writer.WriteLine();
        }

        // Private constructor for scoped instances
        writer.WriteLine($"private {container.ClassName}({container.ClassName} parent)");
        writer.WriteLine("{");
        writer.Indentation++;
        if(needsFallbackProvider)
        {
            writer.WriteLine("_fallbackProvider = parent._fallbackProvider;");
        }
        writer.WriteLine("_isRootScope = false;");

        // Copy singleton references from parent (already filtered for non-open-generics)
        // Skip instance registrations as they don't have fields
        foreach(var cached in groups.Singletons)
        {
            // Instance registrations don't have fields, skip them
            if(cached.Registration.Instance is not null)
                continue;

            writer.WriteLine($"{cached.FieldName} = parent.{cached.FieldName};");
        }

        // Create scopes for imported modules (so their scoped services are properly isolated)
        foreach(var module in container.ImportedModules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            writer.WriteLine($"{fieldName} = ({module.Name})parent.{fieldName}.CreateScope().ServiceProvider;");
        }

        // Initialize eager scoped services by calling their Get methods
        var eagerScoped = groups.EagerScoped;
        if(eagerScoped.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize eager scoped services");
            foreach(var cached in eagerScoped)
            {
                writer.WriteLine($"{cached.FieldName} = {cached.ResolverMethodName}();");
            }
        }

        // Initialize Lazy/Func wrapper fields (each scope gets its own wrappers)
        WriteContainerLazyFieldInitializations(writer, groups.LazyEntries);
        WriteContainerFuncFieldInitializations(writer, groups.FuncEntries);

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes the constructor body for building the service resolver dictionary.
    /// </summary>
    private static void WriteConstructorBody(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups,
        bool hasParameter,
        bool effectiveUseSwitchStatement)
    {
        // Initialize imported modules
        foreach(var module in container.ImportedModules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            if(container.IntegrateServiceProvider && hasParameter)
            {
                writer.WriteLine($"{fieldName} = new {module.Name}(fallbackProvider);");
            }
            else
            {
                writer.WriteLine($"{fieldName} = new {module.Name}();");
            }
        }

        // Initialize eager singletons by calling their Get methods
        // This ensures dependencies are resolved in the correct order
        var eagerSingletons = groups.EagerSingletons;
        if(eagerSingletons.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize eager singletons");
            foreach(var cached in eagerSingletons)
            {
                writer.WriteLine($"{cached.FieldName} = {cached.ResolverMethodName}();");
            }
        }

        // Initialize Lazy/Func wrapper fields
        WriteContainerLazyFieldInitializations(writer, groups.LazyEntries);
        WriteContainerFuncFieldInitializations(writer, groups.FuncEntries);
    }

    /// <summary>
    /// Writes individual service resolver methods.
    /// </summary>
    private static void WriteServiceResolverMethods(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine("#region Service Resolution");
        writer.WriteLine();

        var writtenMethods = new HashSet<string>();

        // All registrations are already filtered (no open generics)
        WriteServiceResolverGroup(writer, strategy, groups.Singletons, writtenMethods, groups);
        WriteServiceResolverGroup(writer, strategy, groups.Scoped, writtenMethods, groups);
        WriteServiceResolverGroup(writer, strategy, groups.Transients, writtenMethods, groups);

        // Write array resolver methods for IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, T[] support
        foreach(var serviceType in groups.CollectionServiceTypes)
        {
            WriteArrayResolverMethod(writer, serviceType, groups);
            writer.WriteLine();
        }

        // Write KVP resolver methods for keyed services consumed as KeyValuePair/Dictionary
        WriteContainerKvpResolverMethods(writer, groups.KvpEntries);

        // Write Lazy wrapper field declarations and array resolvers
        WriteContainerLazyFields(writer, groups.LazyEntries);

        // Write Func wrapper field declarations and array resolvers
        WriteContainerFuncFields(writer, groups.FuncEntries);

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Writes implementations for user-declared partial methods and partial properties
    /// that serve as fast-path service accessors.
    /// </summary>
    private static void WritePartialAccessorImplementations(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups)
    {
        if(container.PartialAccessors.Length == 0)
            return;

        writer.WriteLine("#region Partial Accessor Implementations");
        writer.WriteLine();

        foreach(var accessor in container.PartialAccessors)
        {
            var isTaskReturn = accessor.Kind == PartialAccessorKind.Method
                && TryExtractTaskInnerType(accessor.ReturnTypeName, out _);

            var resolveExpression = ResolvePartialAccessorExpression(accessor, container, groups);

            switch(accessor.Kind)
            {
                case PartialAccessorKind.Method:
                    // Async partial methods (returning Task<T>) require the 'async' modifier
                    if(isTaskReturn)
                    {
                        writer.WriteLine($"public partial async {accessor.ReturnTypeName} {accessor.Name}() => {resolveExpression};");
                    }
                    else
                    {
                        writer.WriteLine($"public partial {accessor.ReturnTypeName}{(accessor.IsNullable ? "?" : "")} {accessor.Name}() => {resolveExpression};");
                    }
                    break;

                case PartialAccessorKind.Property:
                    writer.WriteLine($"public partial {accessor.ReturnTypeName}{(accessor.IsNullable ? "?" : "")} {accessor.Name} {{ get => {resolveExpression}; }}");
                    break;
            }

            writer.WriteLine();
        }

        writer.WriteLine("#endregion");
        writer.WriteLine();
    }

    /// <summary>
    /// Resolves the expression to use for a partial accessor implementation.
    /// Looks up the registration by return type and optional key, with fallback to IServiceProvider.
    /// For <c>Task&lt;T&gt;</c> return types, routes through the async resolver.
    /// </summary>
    private static string ResolvePartialAccessorExpression(
        PartialAccessorData accessor,
        ContainerModel container,
        ContainerRegistrationGroups groups)
    {
        var serviceType = accessor.ReturnTypeName;
        var key = accessor.Key;

        // Handle Task<T> return types — route through the async resolver.
        if(TryExtractTaskInnerType(serviceType, out var innerTypeName))
        {
            if(groups.ByServiceTypeAndKey.TryGetValue((innerTypeName, key), out var taskRegistrations))
            {
                var cached = taskRegistrations[^1]; // Last registration wins

                if(cached.IsAsyncInit)
                {
                    // Async-init: await the shared async resolver and let the async method wrap the cast.
                    var asyncMethodName = GetAsyncResolverMethodName(cached.ResolverMethodName);
                    return $"await {asyncMethodName}()";
                }
                else
                {
                    // Sync-only service wrapped as Task<T>: use Task.FromResult with cast.
                    return $"global::System.Threading.Tasks.Task.FromResult(({innerTypeName}){cached.ResolverMethodName}())";
                }
            }

            // Fallback: delegate to IServiceProvider if available
            if(container.IntegrateServiceProvider)
            {
                if(key is not null)
                    return $"({serviceType})GetRequiredKeyedService(typeof({serviceType}), {key})";
                return $"({serviceType})GetRequiredService(typeof({serviceType}))";
            }

            return $"""throw new global::System.InvalidOperationException("Service '{innerTypeName}' is not registered.")""";
        }

        // Try to find direct resolver in this container
        if(groups.ByServiceTypeAndKey.TryGetValue((serviceType, key), out var registrations))
        {
            var cached = registrations[^1]; // Last registration wins

            if(cached.Registration.Instance is not null)
            {
                // Instance registration: use the instance expression directly
                return cached.Registration.Instance;
            }

            return $"{cached.ResolverMethodName}()";
        }

        // Fallback to GetService/GetRequiredService (only when IntegrateServiceProvider is enabled)
        if(container.IntegrateServiceProvider)
        {
            if(key is not null)
            {
                return accessor.IsNullable
                    ? $"GetKeyedService(typeof({serviceType}), {key}) as {serviceType}"
                    : $"({serviceType})GetRequiredKeyedService(typeof({serviceType}), {key})";
            }

            return accessor.IsNullable
                ? $"GetService(typeof({serviceType})) as {serviceType}"
                : $"({serviceType})GetRequiredService(typeof({serviceType}))";
        }

        // No resolver found and no fallback: throw (analyzer should have caught this)
        return accessor.IsNullable
            ? "default"
            : $"""throw new global::System.InvalidOperationException("Service '{serviceType}' is not registered.")""";
    }

    /// <summary>
    /// Writes resolver methods for a group of registrations, ensuring unique method names.
    /// </summary>
    private static void WriteServiceResolverGroup(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        IEnumerable<CachedRegistration> registrations,
        HashSet<string> writtenMethods,
        ContainerRegistrationGroups groups)
    {
        foreach(var cached in registrations)
        {
            if(!writtenMethods.Add(cached.ResolverMethodName))
                continue;
            WriteServiceResolverMethod(writer, strategy, cached, groups);
            writer.WriteLine();
        }
    }

    /// <summary>
    /// Writes an array resolver method for IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, T[] resolution.
    /// </summary>
    private static void WriteArrayResolverMethod(
        SourceWriter writer,
        string serviceType,
        ContainerRegistrationGroups groups)
    {
        var methodName = GetArrayResolverMethodName(serviceType);
        var returnType = $"{serviceType}[]";

        // Use pre-computed collection registrations (already filtered for non-open-generics)
        if(!groups.CollectionRegistrations.TryGetValue(serviceType, out var registrations))
            return;

        // Deduplicate by resolver method name (or instance expression for instance registrations)
        var uniqueKeys = new HashSet<string>();
        var resolverEntries = new List<CachedRegistration>();

        foreach(var cached in registrations)
        {
            // Use instance expression as key for instance registrations, otherwise use method name
            var key = cached.Registration.Instance ?? cached.ResolverMethodName;
            if(uniqueKeys.Add(key))
            {
                resolverEntries.Add(cached);
            }
        }

        // Only generate collection if there are multiple unique entries
        if(resolverEntries.Count < 2)
            return;

        writer.WriteLine($"private {returnType} {methodName}() =>");
        writer.Indentation++;
        writer.WriteLine("[");
        writer.Indentation++;

        foreach(var cached in resolverEntries)
        {
            if(cached.Registration.Instance is not null)
            {
                // Instance registration: directly use the instance expression
                writer.WriteLine($"{cached.Registration.Instance},");
            }
            else
            {
                // Regular registration: call the resolver method
                writer.WriteLine($"{cached.ResolverMethodName}(),");
            }
        }

        writer.Indentation--;
        writer.WriteLine("];");
        writer.Indentation--;
    }

    /// <summary>
    /// Writes a single service resolver method.
    /// </summary>
    private static void WriteServiceResolverMethod(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        CachedRegistration cached,
        ContainerRegistrationGroups groups)
    {
        var reg = cached.Registration;
        var methodName = cached.ResolverMethodName;
        var fieldName = cached.FieldName;
        var isEager = cached.IsEager;

        // Check if factory or instance registration
        bool hasFactory = reg.Factory is not null;
        bool hasInstance = reg.Instance is not null;
        bool hasDecorators = reg.Decorators.Length > 0;

        // Return type: if there are decorators, return the ServiceType (interface), otherwise ImplementationType
        var returnType = hasDecorators ? reg.ServiceType.Name : reg.ImplementationType.Name;

        // Instance registration: no resolver method needed, will be inlined in _localResolvers
        if(hasInstance)
        {
            return;
        }

        // For async-init services: generate async resolver instead of sync resolver
        if(cached.IsAsyncInit)
        {
            switch(reg.Lifetime)
            {
                case ServiceLifetime.Singleton:
                case ServiceLifetime.Scoped:
                    WriteAsyncServiceResolverMethod(writer, strategy, methodName, returnType, fieldName, reg, hasFactory, hasDecorators, groups);
                    break;

                case ServiceLifetime.Transient:
                    WriteAsyncTransientResolverMethod(writer, methodName, returnType, reg, hasFactory, hasDecorators, groups);
                    break;
            }
            return;
        }

        switch(reg.Lifetime)
        {
            case ServiceLifetime.Singleton:
            case ServiceLifetime.Scoped:
                // Write field and synchronization field above the resolver method for better readability
                WriteServiceInstanceField(writer, strategy, reg, fieldName, hasDecorators, isEager);

                if(isEager)
                {
                    // Eager services: Get method still creates instance on first call (from constructor)
                    // but no synchronization needed since constructor runs single-threaded
                    WriteEagerResolverMethod(writer, methodName, returnType, fieldName, reg, hasFactory, hasDecorators, groups);
                }
                else
                {
                    // Lazy services: Write resolver method based on thread-safe strategy
                    WriteResolverMethodWithThreadSafety(writer, strategy, methodName, returnType, fieldName, reg, hasFactory, hasDecorators, groups);
                }
                break;

            case ServiceLifetime.Transient:
                WriteTransientResolverMethod(writer, methodName, returnType, reg, hasFactory, hasDecorators, groups);
                break;
        }
    }

    /// <summary>
    /// Writes resolver method for eager services.
    /// Eager services are initialized in the constructor, so no synchronization is needed.
    /// The Get method still handles first-call initialization for dependency resolution.
    /// </summary>
    private static void WriteEagerResolverMethod(
        SourceWriter writer,
        string methodName,
        string returnType,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"private {returnType} {methodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        // For non-nullable reference types, we use object comparison instead of pattern matching
        // since the field is initialized to null! and will be set during constructor
        writer.WriteLine($"if({fieldName} is not null) return {fieldName};");
        writer.WriteLine();

        // No synchronization needed - constructor runs single-threaded
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"{fieldName} = instance;");
        writer.WriteLine("return instance;");

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver method based on the specified thread-safety strategy.
    /// </summary>
    private static void WriteResolverMethodWithThreadSafety(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        string methodName,
        string returnType,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"private {returnType} {methodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        // Early return check (common to all strategies)
        writer.WriteLine($"if({fieldName} is not null) return {fieldName};");
        writer.WriteLine();

        switch(strategy)
        {
            case ThreadSafeStrategy.None:
                WriteResolverBodyNone(writer, fieldName, reg, hasFactory, hasDecorators, groups);
                break;

            case ThreadSafeStrategy.Lock:
                WriteResolverBodyLock(writer, fieldName, reg, hasFactory, hasDecorators, groups);
                break;

            case ThreadSafeStrategy.SemaphoreSlim:
                WriteResolverBodySemaphoreSlim(writer, fieldName, reg, hasFactory, hasDecorators, groups);
                break;

            case ThreadSafeStrategy.SpinLock:
                WriteResolverBodySpinLock(writer, fieldName, reg, hasFactory, hasDecorators, groups);
                break;

            case ThreadSafeStrategy.CompareExchange:
                WriteResolverBodyCompareExchange(writer, fieldName, reg, hasFactory, hasDecorators, groups);
                break;
        }

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.None (no synchronization).
    /// </summary>
    private static void WriteResolverBodyNone(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"{fieldName} = instance;");
        writer.WriteLine("return instance;");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.Lock (using lock statement).
    /// </summary>
    private static void WriteResolverBodyLock(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"lock({fieldName}Lock)");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"if({fieldName} is not null) return {fieldName};");
        writer.WriteLine();

        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"{fieldName} = instance;");
        writer.WriteLine("return instance;");

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.SemaphoreSlim.
    /// </summary>
    private static void WriteResolverBodySemaphoreSlim(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"{fieldName}Semaphore.Wait();");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"if({fieldName} is not null) return {fieldName};");
        writer.WriteLine();

        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"{fieldName} = instance;");
        writer.WriteLine("return instance;");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"{fieldName}Semaphore.Release();");
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.SpinLock.
    /// </summary>
    private static void WriteResolverBodySpinLock(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine("bool lockTaken = false;");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"{fieldName}SpinLock.Enter(ref lockTaken);");
        writer.WriteLine($"if({fieldName} is not null) return {fieldName};");
        writer.WriteLine();

        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"{fieldName} = instance;");
        writer.WriteLine("return instance;");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("if(lockTaken) {fieldName}SpinLock.Exit();".Replace("{fieldName}", fieldName));
        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes resolver body for ThreadSafeStrategy.CompareExchange (lock-free CAS pattern).
    /// Uses Interlocked.CompareExchange to atomically set the field. If another thread wins the race,
    /// the losing instance is disposed via DisposeService and the winning instance is returned.
    /// </summary>
    private static void WriteResolverBodyCompareExchange(
        SourceWriter writer,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine();
        writer.WriteLine($"var existing = Interlocked.CompareExchange(ref {fieldName}, instance, null);");
        writer.WriteLine("if(existing is not null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine("DisposeService(instance);");
        writer.WriteLine("return existing;");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("return instance;");
    }

    /// <summary>
    /// Writes transient resolver method (creates new instance each time).
    /// </summary>
    private static void WriteTransientResolverMethod(
        SourceWriter writer,
        string methodName,
        string returnType,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        writer.WriteLine($"private {returnType} {methodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        // Simple case: no injection members and no decorators
        if(reg.InjectionMembers.Length == 0 && !hasDecorators)
        {
            var instanceCreation = BuildInstanceCreationInline(reg, hasFactory, groups);
            writer.WriteLine($"return {instanceCreation};");
            writer.Indentation--;
            writer.WriteLine("}");
            return;
        }

        // Complex case: has injection members or decorators
        var variableType = hasDecorators ? reg.ServiceType.Name : null;
        WriteInstanceCreationWithInjection(writer, "instance", reg, hasFactory, variableType, groups);

        if(hasDecorators)
        {
            writer.WriteLine();
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine("return instance;");
        writer.Indentation--;
        writer.WriteLine("}");
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Async-init service resolver generation
    // Async-init services have at least one InjectionMemberType.AsyncMethod member.
    // They use Task<T> caching and async resolver methods.
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the async routing resolver method name by appending "Async" to the sync method name.
    /// </summary>
    private static string GetAsyncResolverMethodName(string syncMethodName)
        => syncMethodName + "Async";

    /// <summary>
    /// Returns the async creation method name (e.g. "CreateFooBarAsync" from "GetFooBar").
    /// </summary>
    private static string GetAsyncCreateMethodName(string syncMethodName)
    {
        if(syncMethodName.Length > 3 && syncMethodName.StartsWith("Get", StringComparison.Ordinal))
            return "Create" + syncMethodName[3..] + "Async";
        return syncMethodName + "_CreateAsync";
    }

    /// <summary>
    /// Returns the effective thread-safety strategy for a registration.
    /// Async-init services auto-upgrade async-incompatible strategies to <see cref="ThreadSafeStrategy.SemaphoreSlim"/>.
    /// </summary>
    private static ThreadSafeStrategy GetEffectiveThreadSafeStrategy(
        ThreadSafeStrategy strategy,
        bool isAsyncInit)
    {
        if(!isAsyncInit)
            return strategy;

        return strategy is ThreadSafeStrategy.None ? ThreadSafeStrategy.None : ThreadSafeStrategy.SemaphoreSlim;
    }

    /// <summary>
    /// Writes the field declaration for an async-init service's cached <c>Task&lt;T&gt;</c>.
    /// The caller must pass the effective async-init strategy.
    /// </summary>
    private static void WriteAsyncServiceInstanceField(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        string fieldName,
        string taskFieldTypeName)
    {
        writer.WriteLine($"private {taskFieldTypeName}? {fieldName};");

        // Only SemaphoreSlim is async-compatible; others fall back to unsynchronized access.
        if(strategy == ThreadSafeStrategy.SemaphoreSlim)
        {
            writer.WriteLine($"private readonly global::System.Threading.SemaphoreSlim {fieldName}Semaphore = new(1, 1);");
        }
    }

    /// <summary>
    /// Writes an async routing resolver + async creation method for a singleton/scoped async-init service.
    /// </summary>
    private static void WriteAsyncServiceResolverMethod(
        SourceWriter writer,
        ThreadSafeStrategy strategy,
        string syncMethodName,
        string returnType,
        string fieldName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var asyncMethodName = GetAsyncResolverMethodName(syncMethodName);
        var createMethodName = GetAsyncCreateMethodName(syncMethodName);
        var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";
        var effectiveStrategy = GetEffectiveThreadSafeStrategy(strategy, true);

        // Write the Task<T>? instance field (+ semaphore if SemaphoreSlim)
        WriteAsyncServiceInstanceField(writer, effectiveStrategy, fieldName, taskReturnType);
        writer.WriteLine();

        // ── Routing resolver method ──
        writer.WriteLine($"private async {taskReturnType} {asyncMethodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"if({fieldName} is not null)");
        writer.Indentation++;
        writer.WriteLine($"return await {fieldName};");
        writer.Indentation--;
        writer.WriteLine();

        if(effectiveStrategy == ThreadSafeStrategy.SemaphoreSlim)
        {
            WriteAsyncResolverBodySemaphoreSlim(writer, fieldName, createMethodName);
        }
        else
        {
            WriteAsyncResolverBodyNone(writer, fieldName, createMethodName);
        }

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // ── Creation method ──
        writer.WriteLine($"private async {taskReturnType} {createMethodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteAsyncInstanceCreationBody(writer, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes an async creation method for a transient async-init service.
    /// Each call produces a new Task (no caching).
    /// </summary>
    private static void WriteAsyncTransientResolverMethod(
        SourceWriter writer,
        string syncMethodName,
        string returnType,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var createMethodName = GetAsyncCreateMethodName(syncMethodName);
        var taskReturnType = $"global::System.Threading.Tasks.Task<{returnType}>";

        writer.WriteLine($"private async {taskReturnType} {createMethodName}()");
        writer.WriteLine("{");
        writer.Indentation++;

        WriteAsyncInstanceCreationBody(writer, reg, hasFactory, hasDecorators, groups);

        writer.Indentation--;
        writer.WriteLine("}");
    }

    /// <summary>
    /// Writes the instance creation body for an async-init service:
    /// constructor, sync injection (properties + sync methods), await async methods, optional decorators.
    /// </summary>
    private static void WriteAsyncInstanceCreationBody(
        SourceWriter writer,
        ServiceRegistrationModel reg,
        bool hasFactory,
        bool hasDecorators,
        ContainerRegistrationGroups groups)
    {
        var (properties, syncMethods, asyncMethods) = CategorizeInjectionMembersAsync(reg.InjectionMembers);
        var args = BuildConstructorArgumentsString(reg, groups);

        // When decorators are present AND there is method injection (sync or async), we cannot
        // type the instance as the service interface because [IocInject] methods may be on the
        // concrete implementation only.
        //
        // Two-variable pattern:
        //   var baseInstance = new Impl(args) { Props... };
        //   baseInstance.SyncMethod(...);
        //   await baseInstance.AsyncMethod(...);
        //   ServiceType instance = baseInstance;
        //   instance = new Decorator(instance);   // decorator chain
        //
        // Single-variable pattern (no decorators, or decorators + pure property injection):
        //   var instance = new Impl(args) { Props... };
        //   await instance.AsyncInit(...);
        bool hasMethods = syncMethods is { Count: > 0 } || asyncMethods is { Count: > 0 };
        bool needsTwoVarPattern = hasDecorators && hasMethods;

        // ── Create the instance ──
        string injectionVar = needsTwoVarPattern ? "baseInstance" : "instance";
        string varTypeDecl = (hasDecorators && !needsTwoVarPattern) ? reg.ServiceType.Name : "var";

        if(hasFactory)
        {
            var factoryCall = BuildFactoryCallForContainer(reg.Factory!, reg, groups);
            writer.WriteLine($"{varTypeDecl} {injectionVar} = ({reg.ImplementationType.Name}){factoryCall};");
        }
        else
        {
            WriteConstructorWithPropertyInitializers(writer, injectionVar, varTypeDecl, reg.ImplementationType.Name, args, properties, groups);
        }

        // ── Sync method injection ──
        if(syncMethods is { Count: > 0 })
        {
            foreach(var method in syncMethods)
            {
                var methodArgs = method.Parameters is { Length: > 0 }
                    ? string.Join(", ", method.Parameters.Select(p => BuildParameterForContainer(p, reg, groups)))
                    : "";
                writer.WriteLine($"{injectionVar}.{method.Name}({methodArgs});");
            }
        }

        // ── Awaited async method injection ──
        if(asyncMethods is { Count: > 0 })
        {
            foreach(var method in asyncMethods)
            {
                var methodArgs = method.Parameters is { Length: > 0 }
                    ? string.Join(", ", method.Parameters.Select(p => BuildParameterForContainer(p, reg, groups)))
                    : "";
                writer.WriteLine($"await {injectionVar}.{method.Name}({methodArgs});");
            }
        }

        // ── Apply decorators after all injection ──
        if(hasDecorators)
        {
            writer.WriteLine();
            if(needsTwoVarPattern)
            {
                // Convert the concrete implementation variable to the service type
                // so the decorator chain can reassign the variable.
                writer.WriteLine($"{reg.ServiceType.Name} instance = {injectionVar};");
            }
            WriteDecoratorApplication(writer, "instance", reg, groups);
        }

        writer.WriteLine("return instance;");
    }

    /// <summary>
    /// Writes the async routing resolver body for <see cref="ThreadSafeStrategy.None"/> (no synchronization).
    /// </summary>
    private static void WriteAsyncResolverBodyNone(
        SourceWriter writer,
        string fieldName,
        string createMethodName)
    {
        writer.WriteLine($"{fieldName} = {createMethodName}();");
        writer.WriteLine($"return await {fieldName};");
    }

    /// <summary>
    /// Writes the async routing resolver body for <see cref="ThreadSafeStrategy.SemaphoreSlim"/>.
    /// Uses <c>WaitAsync()</c> for async-compatible locking.
    /// </summary>
    private static void WriteAsyncResolverBodySemaphoreSlim(
        SourceWriter writer,
        string fieldName,
        string createMethodName)
    {
        writer.WriteLine($"await {fieldName}Semaphore.WaitAsync();");
        writer.WriteLine("try");
        writer.WriteLine("{");
        writer.Indentation++;

        writer.WriteLine($"if({fieldName} is null)");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"{fieldName} = {createMethodName}();");
        writer.Indentation--;
        writer.WriteLine("}");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine("finally");
        writer.WriteLine("{");
        writer.Indentation++;
        writer.WriteLine($"{fieldName}Semaphore.Release();");
        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine($"return await {fieldName};");
    }

    /// <summary>
    /// Categorizes injection members into properties/fields, sync methods, and async methods.
    /// </summary>
    private static (List<InjectionMemberData>? Properties, List<InjectionMemberData>? SyncMethods, List<InjectionMemberData>? AsyncMethods) CategorizeInjectionMembersAsync(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers)
    {
        List<InjectionMemberData>? properties = null;
        List<InjectionMemberData>? syncMethods = null;
        List<InjectionMemberData>? asyncMethods = null;

        foreach(var member in injectionMembers)
        {
            switch(member.MemberType)
            {
                case InjectionMemberType.Property:
                case InjectionMemberType.Field:
                    properties ??= [];
                    properties.Add(member);
                    break;
                case InjectionMemberType.Method:
                    syncMethods ??= [];
                    syncMethods.Add(member);
                    break;
                case InjectionMemberType.AsyncMethod:
                    asyncMethods ??= [];
                    asyncMethods.Add(member);
                    break;
            }
        }

        return (properties, syncMethods, asyncMethods);
    }

    /// <summary>
    /// Tries to extract the inner type name from a
    /// <c>global::System.Threading.Tasks.Task&lt;T&gt;</c> type name string.
    /// Returns <see langword="true"/> and sets <paramref name="innerTypeName"/> if matched.
    /// </summary>
    private static bool TryExtractTaskInnerType(string typeName, out string innerTypeName)
    {
        const string TaskPrefix = "global::System.Threading.Tasks.Task<";
        if(typeName.StartsWith(TaskPrefix, StringComparison.Ordinal)
            && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            innerTypeName = typeName[TaskPrefix.Length..^1];
            return true;
        }
        innerTypeName = string.Empty;
        return false;
    }

    /// <summary>
    /// Writes instance creation with property/method injection.
    /// </summary>
    /// <param name="variableType">Optional explicit type for the variable. When null, 'var' is used.</param>
    private static void WriteInstanceCreationWithInjection(
        SourceWriter writer,
        string varName,
        ServiceRegistrationModel reg,
        bool hasFactory,
        string? variableType,
        ContainerRegistrationGroups groups)
    {
        var typeDeclaration = variableType ?? "var";

        if(hasFactory)
        {
            var factoryCall = BuildFactoryCallForContainer(reg.Factory!, reg, groups);
            writer.WriteLine($"{typeDeclaration} {varName} = ({reg.ImplementationType.Name}){factoryCall};");
            return;
        }

        WriteConstructorWithInjection(writer, varName, typeDeclaration, reg, groups);
    }

    /// <summary>
    /// Writes constructor invocation with property/field and method injection.
    /// </summary>
    private static void WriteConstructorWithInjection(
        SourceWriter writer,
        string varName,
        string typeDeclaration,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        var (properties, methods) = CategorizeInjectionMembers(reg.InjectionMembers);
        var args = BuildConstructorArgumentsString(reg, groups);

        WriteConstructorWithPropertyInitializers(writer, varName, typeDeclaration, reg.ImplementationType.Name, args, properties, groups);
        WriteMethodInjectionCalls(writer, varName, methods, reg, groups);
    }

    /// <summary>
    /// Categorizes injection members into properties/fields and methods.
    /// </summary>
    private static (List<InjectionMemberData>? Properties, List<InjectionMemberData>? Methods) CategorizeInjectionMembers(
        ImmutableEquatableArray<InjectionMemberData> injectionMembers)
    {
        List<InjectionMemberData>? properties = null;
        List<InjectionMemberData>? methods = null;

        foreach(var member in injectionMembers)
        {
            if(member.MemberType is InjectionMemberType.Property or InjectionMemberType.Field)
            {
                properties ??= [];
                properties.Add(member);
            }
            else if(member.MemberType == InjectionMemberType.Method)
            {
                methods ??= [];
                methods.Add(member);
            }
        }

        return (properties, methods);
    }

    /// <summary>
    /// Writes constructor invocation with optional property initializers.
    /// </summary>
    private static void WriteConstructorWithPropertyInitializers(
        SourceWriter writer,
        string varName,
        string typeDeclaration,
        string typeName,
        string args,
        List<InjectionMemberData>? properties,
        ContainerRegistrationGroups groups)
    {
        if(properties is not { Count: > 0 })
        {
            writer.WriteLine($"{typeDeclaration} {varName} = new {typeName}({args});");
            return;
        }

        writer.WriteLine($"{typeDeclaration} {varName} = new {typeName}({args})");
        writer.WriteLine("{");
        writer.Indentation++;

        foreach(var prop in properties)
        {
            var resolveCall = BuildServiceResolutionCallForContainer(prop.Type!, prop.Key, prop.IsNullable, groups);
            writer.WriteLine($"{prop.Name} = {resolveCall},");
        }

        writer.Indentation--;
        writer.WriteLine("};");
    }

    /// <summary>
    /// Writes method injection calls.
    /// </summary>
    private static void WriteMethodInjectionCalls(
        SourceWriter writer,
        string varName,
        List<InjectionMemberData>? methods,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        if(methods is null)
            return;

        foreach(var method in methods)
        {
            var methodArgs = method.Parameters is { Length: > 0 }
                ? string.Join(", ", method.Parameters.Select(p => BuildParameterForContainer(p, reg, groups)))
                : "";
            writer.WriteLine($"{varName}.{method.Name}({methodArgs});");
        }
    }

    /// <summary>
    /// Writes decorator application code.
    /// Decorators are applied in reverse order (from innermost to outermost),
    /// matching the behavior of Register mode.
    /// </summary>
    private static void WriteDecoratorApplication(
        SourceWriter writer,
        string varName,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        // Decorators array is in order from outermost to innermost,
        // we iterate in reverse order for building the chain from inner to outer
        var decorators = reg.Decorators;
        for(int i = decorators.Length - 1; i >= 0; i--)
        {
            var decorator = decorators[i];
            var hasInjectionMembers = decorator.InjectionMembers?.Length > 0;
            var argsString = string.Join(", ", GetDecoratorArguments(varName, decorator, groups));

            if(hasInjectionMembers)
            {
                // When decorator has injection members, use a temporary variable with concrete type
                // to allow accessing the decorator's properties/methods before assigning to interface variable
                var decoratorVarName = $"decorator{decorators.Length - 1 - i}";
                WriteDecoratorCreationWithInjection(writer, decoratorVarName, decorator, argsString, reg, groups);

                // Assign to the interface variable
                writer.WriteLine($"{varName} = {decoratorVarName};");
            }
            else
            {
                // No injection members, directly assign to the interface variable
                writer.WriteLine($"{varName} = new {decorator.Name}({argsString});");
            }
        }
    }

    /// <summary>
    /// Yields decorator constructor arguments without allocating a List.
    /// First parameter is always the inner instance (the decorated service).
    /// </summary>
    private static IEnumerable<string> GetDecoratorArguments(string innerInstance, TypeData decorator, ContainerRegistrationGroups groups)
    {
        yield return innerInstance;

        if(decorator.ConstructorParameters?.Length > 1)
        {
            // Skip the first parameter (it's the inner/decorated service)
            foreach(var param in decorator.ConstructorParameters.Skip(1))
            {
                yield return BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsNullable, groups);
            }
        }
    }

    /// <summary>
    /// Writes decorator creation with object initializer for property/field injection,
    /// and method calls for method injection.
    /// </summary>
    private static void WriteDecoratorCreationWithInjection(
        SourceWriter writer,
        string varName,
        TypeData decorator,
        string argsString,
        ServiceRegistrationModel reg,
        ContainerRegistrationGroups groups)
    {
        var injectionMembers = decorator.InjectionMembers;
        if(injectionMembers is null or { Length: 0 })
        {
            writer.WriteLine($"var {varName} = new {decorator.Name}({argsString});");
            return;
        }

        List<string>? propertyAssignments = null;
        List<string>? methodInvocations = null;

        foreach(var member in injectionMembers)
        {
            switch(member.MemberType)
            {
                case InjectionMemberType.Property:
                case InjectionMemberType.Field:
                    if(member.Type is not null)
                    {
                        var resolveCall = BuildServiceResolutionCallForContainer(member.Type, member.Key, member.IsNullable, groups);
                        propertyAssignments ??= [];
                        propertyAssignments.Add($"{member.Name} = {resolveCall},");
                    }
                    break;

                case InjectionMemberType.Method:
                    var methodArgs = member.Parameters is { Length: > 0 }
                        ? string.Join(", ", member.Parameters.Select(p => BuildParameterForInjectionMethod(p, reg, groups)))
                        : "";
                    methodInvocations ??= [];
                    methodInvocations.Add($"{varName}.{member.Name}({methodArgs});");
                    break;
            }
        }

        WriteDecoratorConstructorWithProperties(writer, varName, decorator.Name, argsString, propertyAssignments);

        if(methodInvocations is not null)
        {
            foreach(var invocation in methodInvocations)
            {
                writer.WriteLine(invocation);
            }
        }
    }

    /// <summary>
    /// Writes decorator constructor with optional property initializers.
    /// </summary>
    private static void WriteDecoratorConstructorWithProperties(
        SourceWriter writer,
        string varName,
        string decoratorName,
        string argsString,
        List<string>? propertyAssignments)
    {
        if(propertyAssignments is not { Count: > 0 })
        {
            writer.WriteLine($"var {varName} = new {decoratorName}({argsString});");
            return;
        }

        writer.WriteLine($"var {varName} = new {decoratorName}({argsString})");
        writer.WriteLine("{");
        writer.Indentation++;

        foreach(var assignment in propertyAssignments)
        {
            writer.WriteLine(assignment);
        }

        writer.Indentation--;
        writer.WriteLine("};");
    }

    /// <summary>
    /// Builds instance creation inline (for return statements).
    /// </summary>
    private static string BuildInstanceCreationInline(
        ServiceRegistrationModel reg,
        bool hasFactory,
        ContainerRegistrationGroups groups)
    {
        if(hasFactory)
        {
            var factoryCall = BuildFactoryCallForContainer(reg.Factory!, reg, groups);
            return $"({reg.ImplementationType.Name}){factoryCall}";
        }

        var args = BuildConstructorArgumentsString(reg, groups);
        return $"new {reg.ImplementationType.Name}({args})";
    }

    /// <summary>
    /// Builds constructor arguments as a string.
    /// </summary>
    private static string BuildConstructorArgumentsString(ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        var parameters = reg.ImplementationType.ConstructorParameters;
        if(parameters is null or { Length: 0 })
            return "";

        return string.Join(", ", parameters.Select(p => BuildParameterForContainer(p, reg, groups)));
    }

    /// <summary>
    /// Builds a single parameter for injection method (handles IServiceProvider and service resolution).
    /// </summary>
    private static string BuildParameterForInjectionMethod(ParameterData param, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        if(param.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
            return "this";

        return BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsOptional, groups);
    }

    /// <summary>
    /// Builds a single parameter for container (constructor, method injection, or factory).
    /// Handles [ServiceKey], [FromKeyedServices], IServiceProvider, and regular service resolution.
    /// </summary>
    private static string BuildParameterForContainer(ParameterData param, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        if(param.HasServiceKeyAttribute)
            return reg.Key ?? "null";

        if(param.Type.Name is IServiceProviderTypeName or IServiceProviderGlobalTypeName)
            return "this";

        return BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsOptional, groups);
    }

    /// <summary>
    /// Builds a factory method call string for container.
    /// </summary>
    private static string BuildFactoryCallForContainer(FactoryMethodData factory, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        var args = GetFactoryArguments(factory, reg, groups);
        var genericTypeArgs = BuildGenericFactoryTypeArgs(factory, reg.ServiceType);
        var factoryCallPath = genericTypeArgs is not null ? $"{factory.Path}<{genericTypeArgs}>" : factory.Path;

        return $"{factoryCallPath}({string.Join(", ", args)})";
    }

    /// <summary>
    /// Yields factory method arguments without allocating a List.
    /// </summary>
    private static IEnumerable<string> GetFactoryArguments(FactoryMethodData factory, ServiceRegistrationModel reg, ContainerRegistrationGroups groups)
    {
        if(factory.HasServiceProvider)
            yield return "this";

        if(factory.HasKey && reg.Key is not null)
            yield return reg.Key;

        foreach(var param in factory.AdditionalParameters)
            yield return BuildParameterForContainer(param, reg, groups);
    }

    /// <summary>
    /// Builds a service resolution call for container (direct call or GetService/GetRequiredService).
    /// When the dependency is registered in the same container, calls the resolver method directly.
    /// </summary>
    private static string BuildServiceResolutionCallForContainer(
        TypeData type,
        string? key,
        bool isOptional,
        ContainerRegistrationGroups groups)
    {
        // Collection types - use collection resolver method if available
        if(type is CollectionWrapperTypeData collectionType)
        {
            var elementTypeName = collectionType.ElementType.Name;

            // Keyed collection services - fallback to GetKeyedServices (no direct call support yet)
            if(key is not null)
            {
                return $"GetKeyedServices<{elementTypeName}>({key})";
            }

            // Check if element type is KeyValuePair<K,V> — use KVP resolver if available
            if(collectionType.ElementType is KeyValuePairTypeData kvpElement)
            {
                var kvpKeyType = kvpElement.KeyType.Name;
                var kvpValueType = kvpElement.ValueType.Name;
                if(HasKvpRegistrations(kvpKeyType, kvpValueType, groups))
                {
                    // IEnumerable, IReadOnlyCollection, ICollection → Dictionary resolver
                    // IReadOnlyList, IList, T[] → Array resolver (consistent with _localResolvers)
                    var isArrayType = collectionType.WrapperKind is WrapperKind.ReadOnlyList or WrapperKind.List or WrapperKind.Array;
                    var methodName = isArrayType
                        ? GetKvpArrayResolverMethodName(kvpKeyType, kvpValueType)
                        : GetKvpDictionaryResolverMethodName(kvpKeyType, kvpValueType);
                    return $"{methodName}()";
                }
            }

            // Check if we have a collection resolver for this element type
            if(groups.CollectionRegistrations.ContainsKey(elementTypeName))
            {
                var methodName = GetArrayResolverMethodName(elementTypeName);
                return $"{methodName}()";
            }

            return $"GetServices<{elementTypeName}>()";
        }

        // Wrapper types - Lazy<T>, Func<T>, KeyValuePair<K,V>, Task<T>
        if(type is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            return BuildWrapperExpressionForContainer(type, key, isOptional, groups);
        }

        // Try to find direct resolver in this container
        if(groups.ByServiceTypeAndKey.TryGetValue((type.Name, key), out var registrations))
        {
            var cached = registrations[^1]; // Last wins
            // Async-init services: the sync method was not generated; use the async method instead.
            // Callers that depend on an async-init service should be taking Task<T>, not T directly.
            // The analyzer (SGIOC027/029) normally prevents this, but fall back gracefully.
            if(cached.IsAsyncInit)
            {
                if(cached.Registration.Lifetime == ServiceLifetime.Transient)
                    return $"{GetAsyncCreateMethodName(cached.ResolverMethodName)}()";
                return $"{GetAsyncResolverMethodName(cached.ResolverMethodName)}()";
            }
            return $"{cached.ResolverMethodName}()";
        }

        // Fallback to GetService/GetRequiredService for dependencies not in this container

        // Keyed services
        if(key is not null)
        {
            if(isOptional)
            {
                return $"GetKeyedService(typeof({type.Name}), {key}) as {type.Name}";
            }
            return $"({type.Name})GetRequiredKeyedService(typeof({type.Name}), {key})";
        }

        // Regular services
        if(isOptional)
        {
            return $"GetService(typeof({type.Name})) as {type.Name}";
        }
        return $"({type.Name})GetRequiredService(typeof({type.Name}))";
    }

    /// <summary>
    /// Builds a wrapper expression for container resolution (Lazy, Func, KeyValuePair, Dictionary).
    /// Recursively handles nested wrapper types.
    /// </summary>
    private static string BuildWrapperExpressionForContainer(
        TypeData type,
        string? key,
        bool isOptional,
        ContainerRegistrationGroups groups,
        bool useResolverMethods = true)
    {
        switch(type)
        {
            case LazyTypeData lazy:
            {
                var innerType = lazy.InstanceType;
                // Direct Lazy<T> where T is not a wrapper — call wrapper resolver if available (only at top level)
                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(groups.ByServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegs))
                    {
                        var lastReg = innerRegs[^1];
                        var safeInnerType = GetSafeIdentifier(innerType.Name);
                        var safeImplType = GetSafeIdentifier(lastReg.Registration.ImplementationType.Name);
                        return $"_lazy_{safeInnerType}_{safeImplType}";
                    }
                    // Fallback: no matching inner service
                    return BuildServiceResolutionCallForContainer(type, key, isOptional, groups);
                }
                // Nested wrapper or inside nested context — inline construction
                var lazyInnerExpr = BuildInnerResolutionForContainer(innerType, key, isOptional, groups);
                return $"new global::System.Lazy<{innerType.Name}>(() => {lazyInnerExpr}, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)";
            }

            case FuncTypeData func:
            {
                var innerType = func.ReturnType;

                if(func.HasInputParameters)
                {
                    if(groups.ByServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegs))
                    {
                        var targetRegistration = innerRegs[^1].Registration;
                        return BuildContainerMultiParamFuncExpression(func, targetRegistration, groups);
                    }

                    return BuildServiceResolutionCallForContainer(type, key, isOptional, groups);
                }

                // Direct Func<T> where T is not a wrapper — call wrapper resolver if available (only at top level)
                if(innerType is not WrapperTypeData && useResolverMethods)
                {
                    if(groups.ByServiceTypeAndKey.TryGetValue((innerType.Name, key), out var innerRegs))
                    {
                        var lastReg = innerRegs[^1];
                        var safeInnerType = GetSafeIdentifier(innerType.Name);
                        var safeImplType = GetSafeIdentifier(lastReg.Registration.ImplementationType.Name);
                        return $"_func_{safeInnerType}_{safeImplType}";
                    }
                    // Fallback: no matching inner service
                    return BuildServiceResolutionCallForContainer(type, key, isOptional, groups);
                }
                // Nested wrapper or inside nested context — inline construction
                var funcInnerExpr = BuildInnerResolutionForContainer(innerType, key, isOptional, groups);
                return $"new global::System.Func<{innerType.Name}>(() => {funcInnerExpr})";
            }

            case KeyValuePairTypeData kvp:
            {
                var keyType = kvp.KeyType;
                var valueType = kvp.ValueType;
                var keyExpr = key ?? "default";
                var valueExpr = BuildInnerResolutionForContainer(valueType, key, isOptional, groups);
                return $"new global::System.Collections.Generic.KeyValuePair<{keyType.Name}, {valueType.Name}>({keyExpr}, {valueExpr})";
            }

            case DictionaryTypeData dict:
            {
                // Dictionary resolution: use KVP dictionary resolver if available, otherwise fallback.
                var keyType = dict.KeyType;
                var valueType = dict.ValueType;
                if(key is null && HasKvpRegistrations(keyType.Name, valueType.Name, groups))
                {
                    var methodName = GetKvpDictionaryResolverMethodName(keyType.Name, valueType.Name);
                    return $"{methodName}()";
                }
                var kvpTypeName = $"global::System.Collections.Generic.KeyValuePair<{keyType.Name}, {valueType.Name}>";
                if(key is not null)
                {
                    return $"GetKeyedServices<{kvpTypeName}>({key}).ToDictionary()";
                }
                return $"GetServices<{kvpTypeName}>().ToDictionary()";
            }

            case TaskTypeData task:
            {
                // Task<T> wrapper: route based on sync vs async-init registration.
                var innerType = task.InnerType;
                var innerTypeName = innerType.Name;

                if(groups.ByServiceTypeAndKey.TryGetValue((innerTypeName, key), out var innerRegs))
                {
                    var lastReg = innerRegs[^1];
                    if(lastReg.IsAsyncInit)
                    {
                        // Async-init: project Task<ImplType> → Task<ServiceType> via async lambda (not ContinueWith)
                        // so that exceptions propagate as-awaited rather than wrapped in AggregateException.
                        var asyncMethodName = GetAsyncResolverMethodName(lastReg.ResolverMethodName);
                        return $"((global::System.Func<global::System.Threading.Tasks.Task<{innerTypeName}>>)(async () => ({innerTypeName})(await {asyncMethodName}())))()";
                    }
                    else
                    {
                        // Sync-only: wrap in Task.FromResult with cast.
                        return $"global::System.Threading.Tasks.Task.FromResult(({innerTypeName}){lastReg.ResolverMethodName}())";
                    }
                }

                // Fallback to IServiceProvider
                if(key is not null)
                    return isOptional
                        ? $"GetKeyedService(typeof({type.Name}), {key}) as {type.Name}"
                        : $"({type.Name})GetRequiredKeyedService(typeof({type.Name}), {key})";

                return isOptional
                    ? $"GetService(typeof({type.Name})) as {type.Name}"
                    : $"({type.Name})GetRequiredService(typeof({type.Name}))";
            }

            default:
                return BuildServiceResolutionCallForContainer(type, key, isOptional, groups);
        }
    }

    /// <summary>
    /// Builds an inner resolution expression for container — handles nested wrappers, nested
    /// collections (via <see cref="BuildServiceResolutionCallForContainer"/>), or direct resolution.
    /// Supports nesting such as Lazy&lt;IEnumerable&lt;T&gt;&gt;.
    /// </summary>
    private static string BuildInnerResolutionForContainer(
        TypeData innerType,
        string? key,
        bool isOptional,
        ContainerRegistrationGroups groups)
    {
        if(innerType is LazyTypeData or FuncTypeData or KeyValuePairTypeData or DictionaryTypeData or TaskTypeData)
        {
            // Inner wrappers always use inline construction (no resolver methods).
            // NOTE: Nested Task<T> shapes such as Lazy<Task<T>> or IEnumerable<Task<T>> are not
            // supported by the spec and should ideally be rejected by a diagnostic (TODO: add
            // SGIOC diagnostic for nested-Task wrapper shapes). They fall back to IServiceProvider.
            return BuildWrapperExpressionForContainer(innerType, key, isOptional, groups, useResolverMethods: false);
        }

        // Delegates to BuildServiceResolutionCallForContainer which handles:
        // - Collection types (EnumerableTypeData, ReadOnlyCollectionTypeData, CollectionTypeData, ReadOnlyListTypeData, ListTypeData, ArrayTypeData) via GetServices/array resolvers
        // - Direct service resolution via resolver methods or GetRequiredService fallback
        return BuildServiceResolutionCallForContainer(innerType, key, isOptional, groups);
    }

    /// <summary>
    /// Builds an inline multi-parameter Func expression for container resolution.
    /// Uses first-unused type matching from Func input args to constructor/method/property injection targets.
    /// </summary>
    private static string BuildContainerMultiParamFuncExpression(
        FuncTypeData funcType,
        ServiceRegistrationModel registration,
        ContainerRegistrationGroups groups)
    {
        var inputTypes = funcType.InputTypes;
        var inputArgNames = new string[inputTypes.Length];
        var inputArgTypeNames = new string[inputTypes.Length];
        var inputArgUsed = new bool[inputTypes.Length];

        for(var i = 0; i < inputTypes.Length; i++)
        {
            inputArgNames[i] = $"arg{i}";
            inputArgTypeNames[i] = inputTypes[i].Type.Name;
            inputArgUsed[i] = false;
        }

        var lambdaParams = string.Join(", ", inputTypes.Select(static (t, i) => $"{t.Type.Name} arg{i}"));

        var statements = new List<string>();
        var ctorParams = registration.ImplementationType.ConstructorParameters ?? [];
        var ctorEntries = new List<(string Name, string? Value, bool NeedsConditional)>(ctorParams.Length);

        var resolvedParamIndex = 0;
        foreach(var param in ctorParams)
        {
            var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
            if(matchedArg is not null)
            {
                ctorEntries.Add((param.Name, matchedArg, false));
                continue;
            }

            var paramVar = $"p{resolvedParamIndex}";
            var expr = BuildServiceResolutionCallForContainer(param.Type, param.ServiceKey, param.IsOptional, groups);
            statements.Add($"var {paramVar} = {expr};");
            ctorEntries.Add((param.Name, paramVar, false));
            resolvedParamIndex++;
        }

        var propertyInits = new List<string>();
        var propertyIndex = 0;
        foreach(var member in registration.InjectionMembers)
        {
            if(member.MemberType is not (InjectionMemberType.Property or InjectionMemberType.Field))
                continue;

            var memberType = member.Type;
            if(memberType is null)
                continue;

            var matchedArg = TryConsumeMatchingFuncInputArg(memberType.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
            if(matchedArg is not null)
            {
                propertyInits.Add($"{member.Name} = {matchedArg}");
                continue;
            }

            var memberVar = $"s0_p{propertyIndex}";
            var expr = BuildServiceResolutionCallForContainer(memberType, member.Key, member.IsNullable, groups);
            statements.Add($"var {memberVar} = {expr};");
            propertyInits.Add($"{member.Name} = {memberVar}");
            propertyIndex++;
        }

        var ctorArgs = BuildArgumentListFromEntries([.. ctorEntries]);
        var initializerPart = propertyInits.Count > 0 ? $" {{ {string.Join(", ", propertyInits)} }}" : string.Empty;
        var ctorInvocation = BuildConstructorInvocation(registration.ImplementationType.Name, ctorArgs, initializerPart);
        statements.Add($"var s0 = {ctorInvocation};");

        var methodIndex = 0;
        foreach(var method in registration.InjectionMembers)
        {
            if(method.MemberType != InjectionMemberType.Method)
                continue;

            var methodParams = method.Parameters ?? [];
            var methodEntries = new List<(string Name, string? Value, bool NeedsConditional)>(methodParams.Length);
            foreach(var param in methodParams)
            {
                var matchedArg = TryConsumeMatchingFuncInputArg(param.Type.Name, inputArgNames, inputArgTypeNames, inputArgUsed);
                if(matchedArg is not null)
                {
                    methodEntries.Add((param.Name, matchedArg, false));
                    continue;
                }

                var paramVar = $"s0_m{methodIndex}";
                var expr = BuildServiceResolutionCallForContainer(param.Type, method.Key ?? param.ServiceKey, param.IsOptional, groups);
                statements.Add($"var {paramVar} = {expr};");
                methodEntries.Add((param.Name, paramVar, false));
                methodIndex++;
            }

            var methodArgs = BuildArgumentListFromEntries([.. methodEntries]);
            statements.Add($"s0.{method.Name}({methodArgs});");
        }

        statements.Add("return s0;");

        return $"new {funcType.Name}(({lambdaParams}) => {{ {string.Join(" ", statements)} }})";
    }

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
            foreach(var kvp in groups.ByServiceTypeAndKey)
            {
                if(kvp.Key.Key is not null)
                    continue;

                var cached = kvp.Value[^1]; // Last wins
                var reg = cached.Registration;
                writer.WriteLine($"if(serviceType == typeof({reg.ServiceType.Name})) return {cached.ResolverMethodName}();");
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

                foreach(var kvp in groups.ByServiceTypeAndKey)
                {
                    if(kvp.Key.Key is null)
                        continue;

                    var cached = kvp.Value[^1]; // Last wins
                    writer.WriteLine($"(Type t, object k) when t == typeof({kvp.Key.ServiceType}) && Equals(k, {kvp.Key.Key}) => {cached.ResolverMethodName}(),");
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

        // Use ByServiceTypeAndKey to include all service types (already filtered for non-open-generics)
        foreach(var kvp in groups.ByServiceTypeAndKey)
        {
            var cached = kvp.Value[^1]; // Last wins
            var keyExpr = kvp.Key.Key ?? KeyedServiceAnyKey;

            // Determine resolver expression based on registration type
            string resolverExpr;
            if(cached.Registration.Instance is not null)
            {
                // Instance registration: directly return the instance
                resolverExpr = $"static _ => {cached.Registration.Instance}";
            }
            else if(cached.IsAsyncInit)
            {
                // Async-init services: expose Task<T> from GetService.
                // Singleton/Scoped: call the routing async resolver method.
                // Transient: call the async creation method directly (no caching).
                if(cached.Registration.Lifetime == ServiceLifetime.Transient)
                {
                    var createMethodName = GetAsyncCreateMethodName(cached.ResolverMethodName);
                    resolverExpr = $"static c => c.{createMethodName}()";
                }
                else
                {
                    var asyncMethodName = GetAsyncResolverMethodName(cached.ResolverMethodName);
                    resolverExpr = $"static c => c.{asyncMethodName}()";
                }
            }
            else if(cached.IsEager)
            {
                // Eager services: directly access the field
                resolverExpr = $"static c => c.{cached.FieldName}!";
            }
            else
            {
                // Lazy services: call the Get method
                resolverExpr = $"static c => c.{cached.ResolverMethodName}()";
            }

            writer.WriteLine($"new(new ServiceIdentifier(typeof({kvp.Key.ServiceType}), {keyExpr}), {resolverExpr}),");
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

        // Add KeyValuePair<K,V> collection entries for keyed services consumed as KVP/Dictionary
        WriteContainerKvpLocalResolverEntries(writer, container.ContainerTypeName, groups.KvpEntries);

        // Add Lazy<T>/Func<T> wrapper entries for consumers that depend on Lazy<T>/Func<T>
        WriteContainerLazyLocalResolverEntries(writer, container.ContainerTypeName, groups.LazyEntries);
        WriteContainerFuncLocalResolverEntries(writer, container.ContainerTypeName, groups.FuncEntries);

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

        var hasAsyncInitServices = groups.ReversedSingletonsForDisposal.Any(static c => c.IsAsyncInit)
            || groups.ReversedScopedForDisposal.Any(static c => c.IsAsyncInit);
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

        WriteDisposalCalls(writer, groups.ReversedScopedForDisposal, container.ImportedModules, container.ThreadSafeStrategy, isAsync);
        writer.WriteLine("return;");

        writer.Indentation--;
        writer.WriteLine("}");
        writer.WriteLine();

        // Root scope disposal
        WriteDisposalCalls(writer, groups.ReversedSingletonsForDisposal, container.ImportedModules, container.ThreadSafeStrategy, isAsync);
    }

    /// <summary>
    /// Writes disposal calls for services and modules.
    /// </summary>
    private static void WriteDisposalCalls(
        SourceWriter writer,
        IEnumerable<CachedRegistration> services,
        ImmutableEquatableArray<TypeData> modules,
        ThreadSafeStrategy strategy,
        bool isAsync)
    {
        var (serviceMethod, moduleMethod) = isAsync
            ? ("await DisposeServiceAsync", "await {0}.DisposeAsync()")
            : ("DisposeService", "{0}.Dispose()");

        foreach(var cached in services)
        {
            // Skip instance registrations - they are externally managed and should not be disposed by the container
            if(cached.Registration.Instance is not null)
                continue;

            var effectiveStrategy = GetEffectiveThreadSafeStrategy(strategy, cached.IsAsyncInit);

            writer.WriteLine($"{serviceMethod}({cached.FieldName});");

            // Dispose SemaphoreSlim if using SemaphoreSlim strategy (only for non-eager services)
            if(effectiveStrategy == ThreadSafeStrategy.SemaphoreSlim && !cached.IsEager)
            {
                writer.WriteLine($"{cached.FieldName}Semaphore.Dispose();");
            }
        }

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

    /// <summary>
    /// Gets the array resolver method name for IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;, T[] resolution.
    /// </summary>
    private static string GetArrayResolverMethodName(string serviceType)
    {
        var baseName = GetSafeIdentifier(serviceType);
        return $"GetAll{baseName}Array";
    }

    /// <summary>
    /// Gets the field name for an imported module.
    /// </summary>
    private static string GetModuleFieldName(string moduleName)
    {
        var baseName = GetSafeIdentifier(moduleName);
        return $"_{char.ToLowerInvariant(baseName[0])}{baseName[1..]}";
    }
}
