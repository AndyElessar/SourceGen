namespace SourceGen.Ioc.TestCase;

/// <summary>Plugin interface for collection injection testing.</summary>
public interface IPlugin
{
    string Name { get; }
}

internal sealed class PluginA : IPlugin
{
    public string Name => "PluginA";
}

internal sealed class PluginB : IPlugin
{
    public string Name => "PluginB";
}

internal sealed class PluginC : IPlugin
{
    public string Name => "PluginC";
}

/// <summary>Service that receives a collection of plugins via constructor injection.</summary>
public sealed class PluginHost(IEnumerable<IPlugin> plugins)
{
    public IReadOnlyList<IPlugin> Plugins { get; } = [.. plugins];
}

[IocRegisterFor<PluginA>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
[IocRegisterFor<PluginB>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
[IocRegisterFor<PluginC>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
[IocRegisterFor<PluginHost>(ServiceLifetime.Transient)]
[IocDiscover<IEnumerable<IPlugin>>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class CollectionModule;
