namespace SourceGen.Ioc.TestAot.Container;

/// <summary>Collections in the container (Container.Output.spec, "Collections and dictionaries").</summary>
public class CollectionTests
{
    public sealed class ExternalPlugin : IPlugin;

    [Test]
    public async Task CollectionDependency_EachCollectionType_GetsTheCandidatesInTheirOrdering()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        PluginHost host = scope.GetRequiredService<PluginHost>();
        IPlugin[] all = [.. host.All];

        await Assert.That(all.Length).IsEqualTo(2);
        await Assert.That(all[0]).IsSameReferenceAs(scope.GetRequiredService<FirstPlugin>());
        await Assert.That(all[1]).IsSameReferenceAs(scope.GetRequiredService<SecondPlugin>());
        await Assert.That(host.List.Count).IsEqualTo(2);
        await Assert.That(host.Array[1]).IsSameReferenceAs(all[1]);
        await Assert.That(host.Extra.Single()).IsSameReferenceAs(scope.GetRequiredKeyedService<IPlugin>("extra"));
        await Assert.That(host.Keyed.Single()).IsSameReferenceAs(host.Extra[0]);
    }

    [Test]
    public async Task Collection_ScopedElement_EachScopeCachesOne()
    {
        using AppContainer root = new();
        using AppContainer first = root.CreateScope();
        using AppContainer second = root.CreateScope();

        IEnumerable<IPlugin> plugins = first.GetServices<IPlugin>();

        await Assert.That(first.GetServices<IPlugin>()).IsSameReferenceAs(plugins);
        await Assert.That(first.GetRequiredService<IReadOnlyList<IPlugin>>()).IsSameReferenceAs(plugins);
        await Assert.That(first.GetRequiredService<PluginHost>().All).IsSameReferenceAs(plugins);
        await Assert.That(second.GetServices<IPlugin>()).IsNotSameReferenceAs(plugins);
        await Assert.That(() => root.GetServices<IPlugin>()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Collection_SingletonElements_TheRootCachesOne()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        IEnumerable<IClock> clocks = root.GetServices<IClock>();

        await Assert.That(scope.GetServices<IClock>()).IsSameReferenceAs(clocks);
        await Assert.That(clocks.Single()).IsSameReferenceAs(root.Clock);
    }

    [Test]
    public async Task Collection_UnknownKey_Empty()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        await Assert.That(scope.GetKeyedServices<IPlugin>("missing").Count()).IsEqualTo(0);
        await Assert.That(scope.GetKeyedService<IReadOnlyList<IPlugin>>("missing")).IsNotNull();
    }

    [Test]
    public async Task KeyedCollection_SiteKeyEqualToARegisteredKeyAtRunTime_ContainerStillWorks()
    {
        using AppContainer root = new();

        StoreAliasConsumer consumer = root.GetRequiredService<StoreAliasConsumer>();

        // The lookup by Type uses Equals, also for a collection type that only the site asks for. The site matches by
        // compile-time identity, so its own collection is empty
        IStore store = root.GetRequiredKeyedService<IStore>("a");
        await Assert.That(root.GetKeyedServices<IStore>("a").Single()).IsSameReferenceAs(store);
        await Assert.That(root.GetRequiredKeyedService<IReadOnlyList<IStore>>("a").Single()).IsSameReferenceAs(store);
        await Assert.That(root.GetRequiredKeyedService<IReadOnlyList<IStore>>(StoreAliases.A).Single()).IsSameReferenceAs(store);
        await Assert.That(consumer.Stores.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FallbackProvider_CollectionDependency_MergesTheFallbackServicesFirst()
    {
        ExternalPlugin external = new();
        ServiceCollection services = new();
        services.AddSingleton<IPlugin>(external);
        services.AddKeyedSingleton<IPlugin>("extra", external);
        using ServiceProvider provider = services.BuildServiceProvider();
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();

        PluginHost host = scope.GetRequiredService<PluginHost>();
        IPlugin[] all = [.. host.All];

        await Assert.That(all.Length).IsEqualTo(3);
        await Assert.That(all[0]).IsSameReferenceAs(external);
        await Assert.That(all[1]).IsSameReferenceAs(scope.GetRequiredService<FirstPlugin>());
        await Assert.That(host.Extra.Count).IsEqualTo(2);
        await Assert.That(host.Extra[0]).IsSameReferenceAs(external);
        await Assert.That(host.Keyed.Count).IsEqualTo(2);
    }
}
