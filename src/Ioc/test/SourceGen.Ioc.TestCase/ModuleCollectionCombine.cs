namespace SourceGen.Ioc.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected: both modules register the same services

// Modules imported (from another assembly) by containers that register the same service type: collections resolved
// through the importer combine the module's services with the importer's, like MS.DI (see Container.ImportModule).

/// <summary>Service registered both by the modules below and by the containers importing them.</summary>
public interface ICombinedHandler
{
    string Name { get; }
}

internal sealed class ModuleHandlerA : ICombinedHandler
{
    public string Name => "ModuleA";
}

internal sealed class ModuleHandlerB : ICombinedHandler
{
    public string Name => "ModuleB";
}

internal sealed class ModuleKeyHandlerA : ICombinedHandler
{
    public string Name => "ModuleKeyA";
}

internal sealed class ModuleKeyHandlerK1 : ICombinedHandler
{
    public string Name => "ModuleK1";
}

internal sealed class ModuleKeyHandlerK2 : ICombinedHandler
{
    public string Name => "ModuleK2";
}

/// <summary>Requests a collection of <c>Lazy&lt;ICombinedHandler&gt;</c>, so <see cref="CombinedHandlerModule"/> generates one.</summary>
public sealed class CombinedHandlerModuleConsumer(IEnumerable<Lazy<ICombinedHandler>> handlers)
{
    public IEnumerable<Lazy<ICombinedHandler>> Handlers { get; } = handlers;
}

/// <summary>
/// Module with two non-keyed registrations, one under <c>"a"</c> and two under <c>"k"</c>. It generates a collection of
/// <c>Lazy&lt;ICombinedHandler&gt;</c> (requested by its consumer), but none of <c>Func&lt;ICombinedHandler&gt;</c>.
/// </summary>
[IocRegisterFor<CombinedHandlerModuleConsumer>(ServiceLifetime.Transient)]
[IocRegisterFor<ModuleHandlerA>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ModuleHandlerB>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ModuleKeyHandlerA>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "a")]
[IocRegisterFor<ModuleKeyHandlerK1>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocRegisterFor<ModuleKeyHandlerK2>(ServiceLifetime.Transient, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocContainer(ExplicitOnly = true)]
public sealed partial class CombinedHandlerModule;

/// <summary>Module with one non-keyed registration and one under <c>"k"</c>, which get no collection resolver.</summary>
[IocRegisterFor<ModuleHandlerA>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)])]
[IocRegisterFor<ModuleKeyHandlerK1>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ICombinedHandler)], Key = "k")]
[IocContainer(ExplicitOnly = true)]
public sealed partial class SingleCombinedHandlerModule;
