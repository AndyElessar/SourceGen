namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Collections that the container injects (constructor parameters, injected members, partial accessors) and the
/// collections of imported modules' services merge the fallback provider's services like <c>GetServices&lt;T&gt;()</c>:
/// fallback services first, once, from the fallback scope of the container scope.
/// </summary>
public sealed class InjectedCollectionFallbackMergeTests
{
    private sealed class FallbackInjectedPlugin : IInjectedPlugin;

    private sealed class FallbackNonIntegratedModulePlugin : INonIntegratedModulePlugin;

    private sealed class FallbackIntegratedModulePlugin : IIntegratedModulePlugin;

    private static ServiceProvider CreateFallbackProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IInjectedPlugin, FallbackInjectedPlugin>();
        services.AddKeyedScoped<IInjectedPlugin, FallbackInjectedPlugin>("keyed");
        services.AddScoped<INonIntegratedModulePlugin, FallbackNonIntegratedModulePlugin>();
        services.AddScoped<IIntegratedModulePlugin, FallbackIntegratedModulePlugin>();
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateContainer(bool useSwitchStatement, IServiceProvider? fallbackProvider) =>
        useSwitchStatement
            ? new InjectedCollectionSwitchContainer(fallbackProvider)
            : new InjectedCollectionContainer(fallbackProvider);

    private static async Task AssertSameServices<T>(IEnumerable<T> actual, IReadOnlyList<T> expected)
        where T : class
    {
        var actualList = actual.ToList();
        await Assert.That(actualList.Count).IsEqualTo(expected.Count);
        for(var i = 0; i < expected.Count; i++)
        {
            await Assert.That(actualList[i]).IsSameReferenceAs(expected[i]);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Consumer_CollectionDependencies_MergeFallbackServicesLikeGetServices(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();

        var consumer = scope.ServiceProvider.GetRequiredService<InjectedPluginConsumer>();
        var expected = scope.ServiceProvider.GetServices<IInjectedPlugin>().ToList();
        var expectedKeyed = scope.ServiceProvider.GetKeyedServices<IInjectedPlugin>("keyed").ToList();

        await Assert.That(expected.Count).IsEqualTo(3);
        await Assert.That(expected[0]).IsTypeOf<FallbackInjectedPlugin>();
        await AssertSameServices(consumer.Enumerable, expected);
        await AssertSameServices(consumer.ReadOnlyCollection, expected);
        await AssertSameServices(consumer.Collection, expected);
        await AssertSameServices(consumer.ReadOnlyList, expected);
        await AssertSameServices(consumer.List, expected);
        await AssertSameServices(consumer.Array, expected);
        await AssertSameServices(consumer.LazyEnumerable.Value, expected);
        await AssertSameServices(consumer.EnumerableFactory(), expected);
        await AssertSameServices(consumer.InjectedProperty, expected);

        await Assert.That(expectedKeyed.Count).IsEqualTo(2);
        await Assert.That(expectedKeyed[0]).IsTypeOf<FallbackInjectedPlugin>();
        await AssertSameServices(consumer.KeyedEnumerable, expectedKeyed);
        await AssertSameServices(consumer.KeyedArray, expectedKeyed);

        // The fallback provider registers no Lazy<IInjectedPlugin>
        await Assert.That(consumer.LazyPlugins.Count()).IsEqualTo(2);
        await Assert.That(scope.ServiceProvider.GetServices<Lazy<IInjectedPlugin>>().Count()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetService_LazyWrapperOfServiceWithKeyedRegistration_ResolvesLastNonKeyedRegistration(bool useSwitchStatement)
    {
        // The keyed registration of IInjectedPlugin follows the non-keyed ones, which carry the Lazy<IInjectedPlugin> entries
        var provider = CreateContainer(useSwitchStatement, null);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();

        var lazy = scope.ServiceProvider.GetService(typeof(Lazy<IInjectedPlugin>)) as Lazy<IInjectedPlugin>;

        await Assert.That(lazy).IsNotNull();
        await Assert.That(lazy!.Value).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<IInjectedPlugin>());

        var factory = scope.ServiceProvider.GetService(typeof(Func<IInjectedPlugin>)) as Func<IInjectedPlugin>;
        await Assert.That(factory).IsNotNull();
        await Assert.That(factory!()).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<IInjectedPlugin>());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Consumer_TwoScopes_MergeTheFallbackServicesOfTheirOwnFallbackScope(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var fallback1 = scope1.ServiceProvider.GetRequiredService<InjectedPluginConsumer>().Array[0];
        var fallback2 = scope2.ServiceProvider.GetRequiredService<InjectedPluginConsumer>().Array[0];

        await Assert.That(fallback1).IsTypeOf<FallbackInjectedPlugin>();
        await Assert.That(fallback1).IsSameReferenceAs(scope1.ServiceProvider.GetServices<IInjectedPlugin>().First());
        await Assert.That(fallback2).IsSameReferenceAs(scope2.ServiceProvider.GetServices<IInjectedPlugin>().First());
        await Assert.That(fallback1).IsNotSameReferenceAs(fallback2);
    }

    [Test]
    public async Task PartialAccessors_DictionaryMode_MergeFallbackServices()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new InjectedCollectionContainer(fallbackProvider);

        var plugins = container.GetPlugins().ToList();

        await Assert.That(plugins.Count).IsEqualTo(3);
        await Assert.That(plugins[0]).IsTypeOf<FallbackInjectedPlugin>();
        await Assert.That(container.PluginArray.Length).IsEqualTo(3);
        await Assert.That(container.PluginArray[0]).IsTypeOf<FallbackInjectedPlugin>();
    }

    [Test]
    public async Task PartialAccessors_SwitchMode_MergeFallbackServices()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new InjectedCollectionSwitchContainer(fallbackProvider);

        var plugins = container.GetPlugins().ToList();

        await Assert.That(plugins.Count).IsEqualTo(3);
        await Assert.That(plugins[0]).IsTypeOf<FallbackInjectedPlugin>();
        await Assert.That(container.PluginArray.Length).IsEqualTo(3);
        await Assert.That(container.PluginArray[0]).IsTypeOf<FallbackInjectedPlugin>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Consumer_WithoutFallbackProvider_ContainsContainerServicesOnly(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement, null);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();

        var consumer = scope.ServiceProvider.GetRequiredService<InjectedPluginConsumer>();

        await Assert.That(consumer.Enumerable.Count()).IsEqualTo(2);
        await Assert.That(consumer.Array.Length).IsEqualTo(2);
        await Assert.That(consumer.KeyedArray.Length).IsEqualTo(1);
    }

    [Test]
    public async Task Importer_CollectionOfNonIntegratedModuleService_MergesImporterFallbackServices()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new PluginModuleImporter(fallbackProvider);

        var services = container.GetServices<INonIntegratedModulePlugin>().ToList();
        var nonGeneric = ((IEnumerable<INonIntegratedModulePlugin>)container.GetService(typeof(IEnumerable<INonIntegratedModulePlugin>))!).ToList();

        await Assert.That(services.Count).IsEqualTo(3);
        await Assert.That(services[0]).IsTypeOf<FallbackNonIntegratedModulePlugin>();
        await Assert.That(nonGeneric.Count).IsEqualTo(3);
        await Assert.That(nonGeneric[0]).IsTypeOf<FallbackNonIntegratedModulePlugin>();

        // Other collection types stay container-only, like the importer's own collections
        await Assert.That(((IReadOnlyList<INonIntegratedModulePlugin>)container.GetService(typeof(IReadOnlyList<INonIntegratedModulePlugin>))!).Count).IsEqualTo(2);
    }

    [Test]
    public async Task Importer_CollectionOfIntegratedModuleService_MergesFallbackServicesOnce()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new PluginModuleImporter(fallbackProvider);

        var services = container.GetServices<IIntegratedModulePlugin>().ToList();

        await Assert.That(services.Count).IsEqualTo(3);
        await Assert.That(services[0]).IsTypeOf<FallbackIntegratedModulePlugin>();
    }

    [Test]
    public async Task Importer_ScopeConsumer_MergesFallbackServicesOfImporterFallbackScope()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new PluginModuleImporter(fallbackProvider);
        using var scope1 = container.CreateScope();
        using var scope2 = container.CreateScope();

        var consumer1 = scope1.ServiceProvider.GetRequiredService<ModulePluginConsumer>();
        var consumer2 = scope2.ServiceProvider.GetRequiredService<ModulePluginConsumer>();
        var nonIntegrated = consumer1.NonIntegrated.ToList();

        await Assert.That(nonIntegrated.Count).IsEqualTo(3);
        await Assert.That(nonIntegrated[0]).IsSameReferenceAs(scope1.ServiceProvider.GetServices<INonIntegratedModulePlugin>().First());
        await Assert.That(nonIntegrated[0]).IsNotSameReferenceAs(consumer2.NonIntegrated.First());
        await Assert.That(consumer1.Integrated.Count).IsEqualTo(3);
        await Assert.That(consumer1.Integrated[0]).IsSameReferenceAs(scope1.ServiceProvider.GetServices<IIntegratedModulePlugin>().First());
        await Assert.That(consumer1.Integrated[0]).IsNotSameReferenceAs(consumer2.Integrated[0]);
    }

    [Test]
    public async Task NestedImporter_CollectionOfInnerModuleService_MergesFallbackServicesOnce()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new NestedPluginModuleImporter(fallbackProvider);
        using var scope = container.CreateScope();

        var services = scope.ServiceProvider.GetServices<IIntegratedModulePlugin>().ToList();

        await Assert.That(services.Count).IsEqualTo(3);
        await Assert.That(services[0]).IsTypeOf<FallbackIntegratedModulePlugin>();
        await Assert.That(services.Count(static s => s is FallbackIntegratedModulePlugin)).IsEqualTo(1);
    }

    [Test]
    public async Task Importer_WithoutFallbackProvider_ContainsModuleServicesOnly()
    {
        await using var container = new PluginModuleImporter();

        await Assert.That(container.GetServices<INonIntegratedModulePlugin>().Count()).IsEqualTo(2);
        await Assert.That(container.GetServices<IIntegratedModulePlugin>().Count()).IsEqualTo(2);
    }
}
