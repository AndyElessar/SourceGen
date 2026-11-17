namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Collections resolved through a container that imports modules combine the services of the modules (in import order)
/// with the container's own, after the fallback provider's, like <c>MS.DI</c> returns all registrations of a service type.
/// The modules (<see cref="CombinedHandlerModule"/>, <see cref="SingleCombinedHandlerModule"/>) are declared in
/// <c>SourceGen.Ioc.TestCase</c>.
/// </summary>
public sealed class ModuleCollectionCombineTests
{
    private const string AllNonKeyed = "ModuleA,ModuleB,ImporterX,ImporterY";
    private const string AllK = "ModuleK1,ModuleK2,ImporterK1,ImporterK2";
    private const string AllKeyed = "ModuleKeyA,ModuleK1,ModuleK2,ImporterKeyX,ImporterK1,ImporterK2";

    private sealed class NamedHandler(string name) : ICombinedHandler
    {
        public string Name => name;
    }

    private static string Names(object? services) =>
        services is IEnumerable<ICombinedHandler> enumerable ? string.Join(",", enumerable.Select(static s => s.Name)) : "<null>";

    private static ServiceProvider CreateFallbackProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICombinedHandler>(new NamedHandler("Fallback"));
        services.AddKeyedSingleton<ICombinedHandler>("k", new NamedHandler("FallbackK"));
        services.AddKeyedSingleton<ICombinedHandler>("g", new NamedHandler("FallbackG"));
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task MsDi_ModuleThenImporterRegistrations_ReturnsAllInRegistrationOrder()
    {
        // The module's registrations, then the importer's
        var services = new ServiceCollection();
        services.AddSingleton<ICombinedHandler>(new NamedHandler("ModuleA"));
        services.AddSingleton<ICombinedHandler>(new NamedHandler("ModuleB"));
        services.AddKeyedSingleton<ICombinedHandler>("a", new NamedHandler("ModuleKeyA"));
        services.AddKeyedSingleton<ICombinedHandler>("k", new NamedHandler("ModuleK1"));
        services.AddKeyedSingleton<ICombinedHandler>("k", new NamedHandler("ModuleK2"));
        services.AddSingleton<ICombinedHandler>(new NamedHandler("ImporterX"));
        services.AddSingleton<ICombinedHandler>(new NamedHandler("ImporterY"));
        services.AddKeyedSingleton<ICombinedHandler>("x", new NamedHandler("ImporterKeyX"));
        services.AddKeyedSingleton<ICombinedHandler>("k", new NamedHandler("ImporterK1"));
        services.AddKeyedSingleton<ICombinedHandler>("k", new NamedHandler("ImporterK2"));
        await using var provider = services.BuildServiceProvider();

        await Assert.That(Names(provider.GetServices<ICombinedHandler>())).IsEqualTo(AllNonKeyed);
        await Assert.That(Names(provider.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo(AllK);
        await Assert.That(Names(provider.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo(AllKeyed);
        await Assert.That(provider.GetRequiredService<ICombinedHandler>().Name).IsEqualTo("ImporterY");
    }

    [Test]
    public async Task GetServices_ModuleAndImporterWithTwoRegistrations_ReturnsModuleThenImporterServices()
    {
        await using var container = new CombinedHandlerImporter();

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo(AllNonKeyed);
        await Assert.That(Names(container.GetService(typeof(IEnumerable<ICombinedHandler>)))).IsEqualTo(AllNonKeyed);
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo(AllK);
        await Assert.That(Names(container.GetKeyedService(typeof(IEnumerable<ICombinedHandler>), "k"))).IsEqualTo(AllK);
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo(AllKeyed);
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("a"))).IsEqualTo("ModuleKeyA");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("x"))).IsEqualTo("ImporterKeyX");
        await Assert.That(container.GetRequiredService<ICombinedHandler>().Name).IsEqualTo("ImporterY");
        await Assert.That(container.GetRequiredKeyedService<ICombinedHandler>("k").Name).IsEqualTo("ImporterK2");
    }

    [Test]
    public async Task GetServices_ImporterWithOneRegistration_ReturnsModuleThenImporterServices()
    {
        await using var container = new CombinedHandlerSingleImporter();

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo("ModuleA,ModuleB,ImporterX");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo("ModuleK1,ModuleK2,ImporterK1");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo("ModuleKeyA,ModuleK1,ModuleK2,ImporterK1");
        await Assert.That(container.GetRequiredService<ICombinedHandler>().Name).IsEqualTo("ImporterX");
    }

    [Test]
    public async Task GetServices_ModuleWithOneRegistration_ReturnsModuleThenImporterServices()
    {
        await using var container = new SingleModuleCombinedHandlerImporter();

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo("ModuleA,ImporterX,ImporterY");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo("ModuleK1,ImporterK1,ImporterK2");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo("ModuleK1,ImporterK1,ImporterK2");
    }

    [Test]
    public async Task GetServices_ModuleAndImporterWithOneRegistration_ReturnsBoth()
    {
        await using var container = new SingleModuleCombinedHandlerSingleImporter();
        IServiceProvider provider = container;

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo("ModuleA,ImporterX");
        await Assert.That(Names(provider.GetService(typeof(IEnumerable<ICombinedHandler>)))).IsEqualTo("ModuleA,ImporterX");
        await Assert.That(Names(container.GetHandlers())).IsEqualTo("ModuleA,ImporterX");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo("ModuleK1,ImporterK1");
        await Assert.That(Names(container.GetKeyedService(typeof(IEnumerable<ICombinedHandler>), "k"))).IsEqualTo("ModuleK1,ImporterK1");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo("ModuleK1,ImporterK1");
        await Assert.That(container.GetRequiredService<ICombinedHandler>().Name).IsEqualTo("ImporterX");
        await Assert.That(container.GetRequiredKeyedService<ICombinedHandler>("k").Name).IsEqualTo("ImporterK1");
    }

    [Test]
    public async Task GetServices_WithFallbackProvider_ReturnsFallbackServicesOnceFirst()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new CombinedHandlerImporter(fallbackProvider);

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo($"Fallback,{AllNonKeyed}");
        await Assert.That(Names(container.GetService(typeof(IEnumerable<ICombinedHandler>)))).IsEqualTo($"Fallback,{AllNonKeyed}");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo($"FallbackK,{AllK}");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo($"FallbackK,FallbackG,{AllKeyed}");
    }

    [Test]
    public async Task GetServices_SingleRegistrationsWithFallbackProvider_ReturnsFallbackServicesOnceFirst()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new SingleModuleCombinedHandlerSingleImporter(fallbackProvider);
        IServiceProvider provider = container;

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo("Fallback,ModuleA,ImporterX");
        await Assert.That(Names(provider.GetService(typeof(IEnumerable<ICombinedHandler>)))).IsEqualTo("Fallback,ModuleA,ImporterX");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo("FallbackK,ModuleK1,ImporterK1");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo("FallbackK,FallbackG,ModuleK1,ImporterK1");
    }

    [Test]
    public async Task GetServices_Scope_ReturnsScopeInstances()
    {
        await using var container = new CombinedHandlerImporter();
        await using var scope = container.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var services = provider.GetServices<ICombinedHandler>().ToList();

        await Assert.That(Names(services)).IsEqualTo(AllNonKeyed);
        await Assert.That(services[3]).IsSameReferenceAs(provider.GetRequiredService<ICombinedHandler>());
        await Assert.That(services[1]).IsSameReferenceAs(provider.GetServices<ICombinedHandler>().ElementAt(1));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InjectedCollections_EqualGetServices(bool withFallbackProvider)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new CombinedHandlerImporter(withFallbackProvider ? fallbackProvider : null);
        await using var scope = container.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var all = Names(provider.GetServices<ICombinedHandler>());
        var keyed = Names(provider.GetKeyedServices<ICombinedHandler>("k"));
        var allKeyed = Names(provider.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey));

        var consumer = provider.GetRequiredService<CombinedHandlerConsumer>();
        var scopeContainer = (CombinedHandlerImporter)provider;

        await Assert.That(all).Contains(AllNonKeyed);
        await Assert.That(Names(consumer.Enumerable)).IsEqualTo(all);
        await Assert.That(Names(consumer.ReadOnlyList)).IsEqualTo(all);
        await Assert.That(Names(consumer.Array)).IsEqualTo(all);
        await Assert.That(Names(consumer.EnumerableFactory())).IsEqualTo(all);
        await Assert.That(Names(consumer.KeyedEnumerable)).IsEqualTo(keyed);
        await Assert.That(Names(consumer.KeyedArray)).IsEqualTo(keyed);
        await Assert.That(Names(consumer.AllKeyed)).IsEqualTo(allKeyed);
        await Assert.That(Names(scopeContainer.GetHandlers())).IsEqualTo(all);
        await Assert.That(Names(scopeContainer.HandlerArray)).IsEqualTo(all);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task InjectedCollections_OneSideWithOneRegistration_EqualGetServices(int containerIndex)
    {
        IServiceProvider container = containerIndex switch
        {
            0 => new CombinedHandlerSingleImporter(),
            1 => new SingleModuleCombinedHandlerImporter(),
            _ => new CombinedHandlerImporter(),
        };
        await using var disposable = (IAsyncDisposable)container;
        using var scope = container.CreateScope();
        var provider = scope.ServiceProvider;

        var consumer = provider.GetRequiredService<CombinedHandlerConsumer>();

        await Assert.That(Names(consumer.Enumerable)).IsEqualTo(Names(provider.GetServices<ICombinedHandler>()));
        await Assert.That(Names(consumer.Array)).IsEqualTo(Names(provider.GetServices<ICombinedHandler>()));
        await Assert.That(Names(consumer.KeyedEnumerable)).IsEqualTo(Names(provider.GetKeyedServices<ICombinedHandler>("k")));
        await Assert.That(Names(consumer.AllKeyed)).IsEqualTo(Names(provider.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey)));
    }

    [Test]
    public async Task GetServices_NestedImportOfSingleRegistrations_ReturnsAllServices()
    {
        await using var container = new NestedSingleCombinedHandlerImporter();
        IServiceProvider provider = container;

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo("ModuleA,ImporterX,ImporterY");
        await Assert.That(Names(provider.GetService(typeof(IEnumerable<ICombinedHandler>)))).IsEqualTo("ModuleA,ImporterX,ImporterY");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo("ModuleK1,ImporterK1,ImporterK2");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo("ModuleK1,ImporterK1,ImporterK2");
        await Assert.That(container.GetRequiredService<ICombinedHandler>().Name).IsEqualTo("ImporterY");
    }

    [Test]
    public async Task GetServices_NestedImportOfCombinedCollections_ReturnsAllServices()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new NestedCombinedHandlerImporter(fallbackProvider);

        await Assert.That(Names(container.GetServices<ICombinedHandler>())).IsEqualTo($"Fallback,{AllNonKeyed},ImporterY");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>("k"))).IsEqualTo($"FallbackK,{AllK}");
        await Assert.That(Names(container.GetKeyedServices<ICombinedHandler>(KeyedService.AnyKey))).IsEqualTo($"FallbackK,FallbackG,{AllKeyed}");
    }

    [Test]
    public async Task InjectedWrapperCollections_CombineTheWrapperCollectionsOfEachSource()
    {
        await using var container = new CombinedHandlerImporter();
        await using var scope = container.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var consumer = provider.GetRequiredService<CombinedHandlerConsumer>();

        // The module generates a Lazy<T> collection (its consumer requests one), which comes first; it has no Func<T> collection
        await Assert.That(Names(consumer.LazyHandlers.Select(static l => l.Value))).IsEqualTo(AllNonKeyed);
        await Assert.That(Names(consumer.LazyHandlers.Select(static l => l.Value))).IsEqualTo(Names(provider.GetServices<Lazy<ICombinedHandler>>().Select(static l => l.Value)));
        await Assert.That(Names(consumer.HandlerFactories.Select(static f => f()))).IsEqualTo("ImporterX,ImporterY");
        await Assert.That(Names(consumer.HandlerFactories.Select(static f => f()))).IsEqualTo(Names(provider.GetServices<Func<ICombinedHandler>>().Select(static f => f())));
    }

    [Test]
    public async Task GetServices_ContainerInfrastructureAndCollectionTypes_AreNotCombined()
    {
        await using var container = new CombinedHandlerImporter();

        // The container (and each module) registers itself as IServiceProvider / IServiceScopeFactory, and the generated
        // collection types of each source: only the importer's are returned, like MS.DI's single IServiceProvider
        await Assert.That(container.GetServices<IServiceProvider>().Single()).IsSameReferenceAs(container);
        await Assert.That(container.GetServices<IServiceScopeFactory>().Single()).IsSameReferenceAs(container);
        await Assert.That(container.GetServices<IReadOnlyList<ICombinedHandler>>().Count()).IsEqualTo(1);
        await Assert.That(container.GetServices<ICombinedHandler[]>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task GetServices_ExplicitEnumerableInModule_ReturnsExplicitRegistration()
    {
        await using var container = new ExplicitEnumerableModuleImporter();

        await Assert.That(container.GetServices<ICombinedHandler>()).IsSameReferenceAs(ExplicitCombinedHandlerList.Module);
        await Assert.That(container.GetService(typeof(IEnumerable<ICombinedHandler>))).IsSameReferenceAs(ExplicitCombinedHandlerList.Module);
    }

    [Test]
    public async Task GetServices_ExplicitEnumerableInImporter_ReturnsExplicitRegistration()
    {
        await using var container = new ExplicitEnumerableImporter();

        await Assert.That(container.GetServices<ICombinedHandler>()).IsSameReferenceAs(ExplicitCombinedHandlerList.Importer);
        await Assert.That(container.GetService(typeof(IEnumerable<ICombinedHandler>))).IsSameReferenceAs(ExplicitCombinedHandlerList.Importer);
    }
}
