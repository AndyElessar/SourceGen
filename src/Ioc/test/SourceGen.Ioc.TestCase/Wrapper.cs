namespace SourceGen.Ioc.TestCase;

/// <summary>Service that receives Lazy and Func wrapper dependencies.</summary>
public sealed class LazyPluginConsumer(Lazy<IPlugin> lazyPlugin, Func<IPlugin> pluginFactory)
{
    public Lazy<IPlugin> LazyPlugin => lazyPlugin;
    public Func<IPlugin> PluginFactory => pluginFactory;
}

[IocImportModule<CollectionModule>]
[IocRegisterFor<LazyPluginConsumer>(ServiceLifetime.Transient)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class WrapperModule;
