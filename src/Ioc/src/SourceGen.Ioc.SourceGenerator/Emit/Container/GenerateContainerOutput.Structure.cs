using System;
using System.Collections.Generic;
using System.Linq;
using static SourceGen.Ioc.Constants;

namespace SourceGen.Ioc;

partial class IocSourceGenerator
{
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

        // Write class declaration
        writer.WriteLine(GeneratedCodeAttribute);
        writer.WriteLine($"partial class {container.ClassName} : global::SourceGen.Ioc.IIocContainer<{container.ContainerTypeName}>, {FixedContainerInterfaces}");
        writer.WriteLine("{");
        writer.Indentation++;

        // Write fields
        WriteContainerFields(writer, container, groups);

        // Write constructors
        WriteContainerConstructors(writer, container, groups, effectiveUseSwitchStatement);

        // Write service resolver methods
        WriteServiceResolverSection(writer, groups);

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

        // Write the nested ServiceProviderFactory type (if DI package is available)
        if(canGenerateServiceProviderFactory)
        {
            WriteServiceProviderFactory(writer, container);
        }

        // Write Disposal implementation
        WriteDisposalImplementation(writer, container, groups, canGenerateServiceProviderFactory);

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

    /// <summary>
    /// Interfaces every container implements after <c>IIocContainer&lt;T&gt;</c>.
    /// </summary>
    private const string FixedContainerInterfaces =
        "global::System.IServiceProvider, "
        + "global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider, "
        + "global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsService, "
        + "global::Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService, "
        + "global::Microsoft.Extensions.DependencyInjection.ISupportRequiredService, "
        + "global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory, "
        + "global::Microsoft.Extensions.DependencyInjection.IServiceScope, "
        + "global::System.IDisposable, "
        + "global::System.IAsyncDisposable";

    /// <summary>
    /// Writes container fields (fallback provider, service storage, locks, etc.).
    /// </summary>
    private static void WriteContainerFields(
        SourceWriter writer,
        ContainerModel container,
        ContainerRegistrationGroups groups)
    {
        // Fallback provider field (only if IntegrateServiceProvider is enabled)
        if(container.IntegrateServiceProvider)
        {
            writer.WriteLine("private readonly global::System.IServiceProvider? _fallbackProvider;");
            // Scope on the fallback provider, created on first fallback resolution in a container scope (null in the root)
            writer.WriteLine("private global::Microsoft.Extensions.DependencyInjection.IServiceScope? _fallbackScope;");
        }

        // Root container reference used by scopes to resolve lazily created singletons (null in the root)
        if(HasRootResolvedSingletons(groups))
        {
            writer.WriteLine($"private readonly {container.ClassName}? _root;");
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
            writer.WriteLine($"private {typeName} {fieldName} = null!;");
            return;
        }

        writer.WriteLine($"private {typeName}? {fieldName};");
        WriteSyncField(writer, strategy, fieldName);
    }

    /// <summary>
    /// Writes the synchronization field guarding <paramref name="fieldName"/> for <paramref name="strategy"/>.
    /// </summary>
    private static void WriteSyncField(SourceWriter writer, ThreadSafeStrategy strategy, string fieldName)
    {
        switch(strategy)
        {
            case ThreadSafeStrategy.Lock:
                writer.WriteLine($"private readonly global::System.Threading.Lock {fieldName}Lock = new();");
                break;
            case ThreadSafeStrategy.SemaphoreSlim:
                writer.WriteLine($"private readonly global::System.Threading.SemaphoreSlim {fieldName}Semaphore = new(1, 1);");
                break;
            case ThreadSafeStrategy.SpinLock:
                // SpinLock must NOT be readonly because Enter/Exit mutate it
                writer.WriteLine($"private global::System.Threading.SpinLock {fieldName}SpinLock = new(false);");
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
            writer.WriteLine($"public {container.ClassName}() : this((global::System.IServiceProvider?)null) {{ }}");
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
            writer.WriteLine($"public {container.ClassName}(global::System.IServiceProvider? fallbackProvider)");
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
            // Root fallback provider; the matching fallback scope is created lazily by GetFallbackServiceProvider()
            writer.WriteLine("_fallbackProvider = parent._fallbackProvider;");
        }
        writer.WriteLine("_isRootScope = false;");

        if(HasRootResolvedSingletons(groups))
        {
            writer.WriteLine("_root = parent._root ?? parent;");
        }

        // Copy singleton references from parent (already filtered for non-open-generics)
        foreach(var entry in groups.SingletonEntries)
        {
            entry.WriteCopyFromParent(writer);
        }

        // Create scopes for imported modules (so their scoped services are properly isolated)
        foreach(var module in container.ImportedModules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            writer.WriteLine($"{fieldName} = ({module.Name})parent.{fieldName}.CreateScope().ServiceProvider;");
        }

        // Initialize Lazy/Func wrapper fields (each scope gets its own wrappers).
        // Must precede eager initialization because eager services may inject these fields.
        WriteWrapperInitializations(writer, groups);

        // Initialize eager scoped services
        if(groups.ScopedEntries.Any(static entry => entry.IsEagerlyInitialized))
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize eager scoped services");
            foreach(var entry in groups.ScopedEntries)
            {
                entry.WriteEagerInit(writer);
            }
        }

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

        // Initialize Lazy/Func wrapper fields.
        // Must precede eager initialization because eager services may inject these fields.
        WriteWrapperInitializations(writer, groups);

        // Initialize eager singleton services
        if(groups.SingletonEntries.Any(static entry => entry.IsEagerlyInitialized))
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize eager singletons");
            foreach(var entry in groups.SingletonEntries)
            {
                entry.WriteEagerInit(writer);
            }
        }
    }

    private static void WriteWrapperInitializations(
        SourceWriter writer,
        ContainerRegistrationGroups groups)
    {
        var lazyEntries = groups.LazyWrapperEntries;
        var funcEntries = groups.FuncWrapperEntries;

        if(lazyEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize Lazy wrapper fields");
            foreach(var entry in lazyEntries)
            {
                entry.WriteInit(writer);
            }
        }

        if(funcEntries.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize Func wrapper fields");
            foreach(var entry in funcEntries)
            {
                entry.WriteInit(writer);
            }
        }
    }

    /// <summary>
    /// Whether any singleton is created on demand, in which case scopes must resolve it through the root container.
    /// </summary>
    private static bool HasRootResolvedSingletons(ContainerRegistrationGroups groups) =>
        groups.SingletonEntries.Any(static entry => entry.DelegatesToRoot);


    /// <summary>
    /// Gets the field name for an imported module.
    /// </summary>
    private static string GetModuleFieldName(string moduleName)
    {
        var baseName = GetSafeIdentifier(moduleName);
        return $"_{char.ToLowerInvariant(baseName[0])}{baseName[1..]}";
    }
}