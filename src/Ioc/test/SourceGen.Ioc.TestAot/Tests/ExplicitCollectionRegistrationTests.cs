namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// A collection type registered explicitly (e.g. <c>IEnumerable&lt;T&gt;</c> as a service type) wins over the collection
/// resolver the container generates for several registrations of <c>T</c>, like <c>MS.DI</c>, which takes the exact
/// registration of <c>IEnumerable&lt;T&gt;</c> before it builds a collection.
/// </summary>
public sealed class ExplicitCollectionRegistrationTests
{
    private sealed class Handler : IExplicitCollectionHandler;

    private static IServiceProvider CreateContainer(string kind) => kind switch
    {
        "switch" => new ExplicitCollectionSwitchContainer(),
        "import" => new ExplicitCollectionImporter(),
        _ => new ExplicitCollectionContainer(),
    };

    [Test]
    public async Task MsDi_ExplicitEnumerableRegistration_WinsOverCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExplicitCollectionHandler, Handler>();
        services.AddSingleton<IExplicitCollectionHandler, Handler>();
        services.AddSingleton<IEnumerable<IExplicitCollectionHandler>>(ExplicitHandlerList.Default);
        services.AddKeyedSingleton<IExplicitCollectionHandler, Handler>("a");
        services.AddKeyedSingleton<IExplicitCollectionHandler, Handler>("a");
        services.AddKeyedSingleton<IEnumerable<IExplicitCollectionHandler>>("a", ExplicitHandlerList.Keyed);
        await using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetServices<IExplicitCollectionHandler>()).IsSameReferenceAs(ExplicitHandlerList.Default);
        await Assert.That(provider.GetKeyedServices<IExplicitCollectionHandler>("a")).IsSameReferenceAs(ExplicitHandlerList.Keyed);
    }

    [Test]
    [Arguments("dictionary")]
    [Arguments("switch")]
    [Arguments("import")]
    public async Task GetService_ExplicitCollectionRegistrations_WinOverGeneratedCollections(string kind)
    {
        var provider = CreateContainer(kind);
        await using var disposable = (IAsyncDisposable)provider;
        var keyed = (IKeyedServiceProvider)provider;

        await Assert.That(provider.GetService(typeof(IEnumerable<IExplicitCollectionHandler>))).IsSameReferenceAs(ExplicitHandlerList.Default);
        await Assert.That(provider.GetService(typeof(IReadOnlyList<IExplicitCollectionHandler>))).IsSameReferenceAs(ExplicitHandlerList.Default);
        await Assert.That(provider.GetServices<IExplicitCollectionHandler>()).IsSameReferenceAs(ExplicitHandlerList.Default);
        await Assert.That(keyed.GetKeyedService(typeof(IEnumerable<IExplicitCollectionHandler>), "a")).IsSameReferenceAs(ExplicitHandlerList.Keyed);
        await Assert.That(keyed.GetKeyedService(typeof(IReadOnlyList<IExplicitCollectionHandler>), "a")).IsSameReferenceAs(ExplicitHandlerList.Keyed);
        await Assert.That(provider.GetKeyedServices<IExplicitCollectionHandler>("a")).IsSameReferenceAs(ExplicitHandlerList.Keyed);
        await Assert.That(provider.GetKeyedServices<IExplicitCollectionHandler>(KeyedService.AnyKey)).IsSameReferenceAs(ExplicitHandlerList.AllKeys);

        // The collection types that are not registered explicitly keep the generated collection
        await Assert.That(((IExplicitCollectionHandler[])provider.GetService(typeof(IExplicitCollectionHandler[]))!).Length).IsEqualTo(2);
        await Assert.That(((IExplicitCollectionHandler[])keyed.GetKeyedService(typeof(IExplicitCollectionHandler[]), "a")!).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments("dictionary")]
    [Arguments("switch")]
    public async Task Consumer_ExplicitCollectionRegistrations_AreInjected(string kind)
    {
        var provider = CreateContainer(kind);
        await using var disposable = (IAsyncDisposable)provider;

        var consumer = provider.GetRequiredService<ExplicitCollectionConsumer>();

        await Assert.That(consumer.Enumerable).IsSameReferenceAs(ExplicitHandlerList.Default);
        await Assert.That(consumer.ReadOnlyList).IsSameReferenceAs(ExplicitHandlerList.Default);
        await Assert.That(consumer.KeyedEnumerable).IsSameReferenceAs(ExplicitHandlerList.Keyed);
        await Assert.That(consumer.Array.Length).IsEqualTo(2);
        await Assert.That(consumer.KeyedArray.Length).IsEqualTo(2);
    }
}
