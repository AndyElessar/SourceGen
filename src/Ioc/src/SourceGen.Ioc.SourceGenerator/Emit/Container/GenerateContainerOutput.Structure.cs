using System;
using System.Collections.Generic;
using System.Linq;
using static SourceGen.Ioc.SourceGenerator.Models.Constants;

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

        writer.WriteLine(isEager
            ? $"private {typeName} {fieldName} = null!;"
            : $"private {typeName}? {fieldName};");

        if(isEager)
            return;

        // SpinLock must NOT be readonly because Enter/Exit mutate it
        var syncFieldDeclaration = strategy switch
        {
            ThreadSafeStrategy.Lock => $"private readonly Lock {fieldName}Lock = new();",
            ThreadSafeStrategy.SemaphoreSlim => $"private readonly SemaphoreSlim {fieldName}Semaphore = new(1, 1);",
            ThreadSafeStrategy.SpinLock => $"private SpinLock {fieldName}SpinLock = new(false);",
            _ => null
        };

        if(syncFieldDeclaration is not null)
        {
            writer.WriteLine(syncFieldDeclaration);
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
        foreach(var entry in groups.SingletonEntries)
        {
            if(!TryGetServiceFieldName(entry, out var fieldName))
                continue;

            writer.WriteLine($"{fieldName} = parent.{fieldName};");
        }

        // Create scopes for imported modules (so their scoped services are properly isolated)
        foreach(var module in container.ImportedModules)
        {
            var fieldName = GetModuleFieldName(module.Name);
            writer.WriteLine($"{fieldName} = ({module.Name})parent.{fieldName}.CreateScope().ServiceProvider;");
        }

        // Initialize eager scoped services
        if(groups.ScopedEntries.Any(static entry => entry is EagerContainerEntry or AsyncContainerEntry))
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize eager scoped services");
            foreach(var entry in groups.ScopedEntries)
            {
                entry.WriteEagerInit(writer);
            }
        }

        // Initialize Lazy/Func wrapper fields (each scope gets its own wrappers)
        WriteWrapperInitializations(writer, groups.WrapperEntries);

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

        // Initialize eager singleton services
        if(groups.SingletonEntries.Any(static entry => entry is EagerContainerEntry or AsyncContainerEntry))
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize eager singletons");
            foreach(var entry in groups.SingletonEntries)
            {
                entry.WriteEagerInit(writer);
            }
        }

        // Initialize Lazy/Func wrapper fields
        WriteWrapperInitializations(writer, groups.WrapperEntries);
    }

    private static void WriteWrapperInitializations(
        SourceWriter writer,
        ImmutableEquatableArray<ContainerEntry> wrapperEntries)
    {
        var lazyEntries = wrapperEntries.OfType<LazyWrapperContainerEntry>().ToList();
        var funcEntries = wrapperEntries.OfType<FuncWrapperContainerEntry>().ToList();

        if(lazyEntries.Count == 0 && funcEntries.Count == 0)
            return;

        if(lazyEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize Lazy wrapper fields");
            foreach(var entry in lazyEntries)
            {
                entry.WriteInit(writer);
            }
        }

        if(funcEntries.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("// Initialize Func wrapper fields");
            foreach(var entry in funcEntries)
            {
                entry.WriteInit(writer);
            }
        }
    }

    private static bool TryGetServiceFieldName(ContainerEntry entry, out string fieldName)
    {
        switch(entry)
        {
            case EagerContainerEntry eager:
                fieldName = eager.FieldName;
                return true;
            case LazyThreadSafeContainerEntry lazy:
                fieldName = lazy.FieldName;
                return true;
            case AsyncContainerEntry asyncEntry:
                fieldName = asyncEntry.FieldName;
                return true;
            default:
                fieldName = string.Empty;
                return false;
        }
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