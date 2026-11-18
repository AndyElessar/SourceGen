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
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetService_ExplicitWrapperCollectionRegistrations_WinOverGeneratedCollections(bool useSwitchStatement)
    {
        IServiceProvider provider = useSwitchStatement ? new ExplicitWrapperCollectionSwitchContainer() : new ExplicitWrapperCollectionContainer();
        await using var disposable = (IAsyncDisposable)provider;

        await Assert.That(provider.GetService(typeof(IEnumerable<Lazy<IExplicitCollectionHandler>>))).IsSameReferenceAs(ExplicitWrapperCollections.Lazies);
        await Assert.That(provider.GetService(typeof(IEnumerable<Func<IExplicitCollectionHandler>>))).IsSameReferenceAs(ExplicitWrapperCollections.Funcs);
        await Assert.That(provider.GetService(typeof(IEnumerable<KeyValuePair<string, IExplicitCollectionHandler>>))).IsSameReferenceAs(ExplicitWrapperCollections.Pairs);
        await Assert.That(provider.GetService(typeof(IReadOnlyDictionary<string, IExplicitCollectionHandler>))).IsSameReferenceAs(ExplicitWrapperCollections.Dictionary);

        // The collection types that are not registered explicitly keep the generated collection
        await Assert.That(((Lazy<IExplicitCollectionHandler>[])provider.GetService(typeof(Lazy<IExplicitCollectionHandler>[]))!).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Consumer_ExplicitWrapperCollectionRegistrations_AreInjected(bool useSwitchStatement)
    {
        IServiceProvider provider = useSwitchStatement ? new ExplicitWrapperCollectionSwitchContainer() : new ExplicitWrapperCollectionContainer();
        await using var disposable = (IAsyncDisposable)provider;

        var consumer = provider.GetRequiredService<ExplicitWrapperCollectionConsumer>();

        await Assert.That(consumer.Lazies).IsSameReferenceAs(ExplicitWrapperCollections.Lazies);
        await Assert.That(consumer.Funcs).IsSameReferenceAs(ExplicitWrapperCollections.Funcs);
        await Assert.That(consumer.Pairs).IsSameReferenceAs(ExplicitWrapperCollections.Pairs);
        await Assert.That(consumer.Dictionary).IsSameReferenceAs(ExplicitWrapperCollections.Dictionary);
        await Assert.That(consumer.LazyArray.Length).IsEqualTo(2);
        await Assert.That(consumer.LazyDictionary.Value).IsSameReferenceAs(ExplicitWrapperCollections.Dictionary);
        await Assert.That(consumer.DictionaryFactory()).IsSameReferenceAs(ExplicitWrapperCollections.Dictionary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitBclCollectionInstances_WinForGetServiceAndInjection(bool useSwitchStatement)
    {
        IServiceProvider provider = useSwitchStatement ? new ExplicitBclCollectionSwitchContainer() : new ExplicitBclCollectionContainer();
        await using var disposable = (IAsyncDisposable)provider;

        await Assert.That(provider.GetService(typeof(List<Lazy<IExplicitBclCollectionHandler>>))).IsSameReferenceAs(ExplicitBclCollections.Lazies);
        await Assert.That(provider.GetService(typeof(IEnumerable<Lazy<IExplicitBclCollectionHandler>>))).IsSameReferenceAs(ExplicitBclCollections.Lazies);
        await Assert.That(provider.GetService(typeof(Dictionary<string, IExplicitBclCollectionHandler>))).IsSameReferenceAs(ExplicitBclCollections.Dictionary);
        await Assert.That(provider.GetService(typeof(IReadOnlyDictionary<string, IExplicitBclCollectionHandler>))).IsSameReferenceAs(ExplicitBclCollections.Dictionary);

        var consumer = provider.GetRequiredService<ExplicitBclCollectionConsumer>();

        await Assert.That(consumer.Lazies).IsSameReferenceAs(ExplicitBclCollections.Lazies);
        await Assert.That(consumer.Dictionary).IsSameReferenceAs(ExplicitBclCollections.Dictionary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitWrapperRegistrations_WinForGetServiceAndInjection(bool useSwitchStatement)
    {
        IServiceProvider provider = useSwitchStatement ? new ExplicitWrapperSwitchContainer() : new ExplicitWrapperContainer();
        await using var disposable = (IAsyncDisposable)provider;

        await Assert.That(provider.GetService(typeof(Lazy<IExplicitCollectionHandler>))).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That(provider.GetService(typeof(Func<IExplicitCollectionHandler>))).IsSameReferenceAs(ExplicitWrappers.Func);

        var consumer = provider.GetRequiredService<ExplicitWrapperConsumer>();

        await Assert.That(consumer.Lazy).IsSameReferenceAs(ExplicitWrappers.Lazy);
        // As object: Assert.That(Func<T>) would invoke the delegate
        await Assert.That((object)consumer.Func).IsSameReferenceAs(ExplicitWrappers.Func);
        await Assert.That((object)consumer.LazyFunc.Value).IsSameReferenceAs(ExplicitWrappers.Func);

        // Like MS.DI, a collection of a wrapper type registered explicitly contains the explicit registrations only
        await Assert.That(consumer.Lazies.Single()).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That(consumer.LazyArray.Single()).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That((object)consumer.Funcs.Single()).IsSameReferenceAs(ExplicitWrappers.Func);
        await Assert.That(((IEnumerable<Lazy<IExplicitCollectionHandler>>)provider.GetService(typeof(IEnumerable<Lazy<IExplicitCollectionHandler>>))!).Single()).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That((object)((IEnumerable<Func<IExplicitCollectionHandler>>)provider.GetService(typeof(IEnumerable<Func<IExplicitCollectionHandler>>))!).Single()).IsSameReferenceAs(ExplicitWrappers.Func);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetService_WrapperCollectionNotRequestedAtCompileTime_ContainsOnlyExplicitRegistration(bool useSwitchStatement)
    {
        IServiceProvider provider = useSwitchStatement ? new ExplicitSingleWrapperSwitchContainer() : new ExplicitSingleWrapperContainer();
        await using var disposable = (IAsyncDisposable)provider;

        var consumer = provider.GetRequiredService<ExplicitSingleWrapperConsumer>();
        await Assert.That(consumer.Lazy).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That((object)consumer.Func).IsSameReferenceAs(ExplicitWrappers.Func);

        await Assert.That(provider.GetServices<Lazy<IExplicitCollectionHandler>>().Single()).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That((object)provider.GetServices<Func<IExplicitCollectionHandler>>().Single()).IsSameReferenceAs(ExplicitWrappers.Func);
    }

    [Test]
    public async Task MsDi_ExplicitWrapperRegistrations_AreTheOnlyCollectionElements()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExplicitCollectionHandler, SecondExplicitCollectionHandler>();
        services.AddSingleton(ExplicitWrappers.Lazy);
        services.AddSingleton(ExplicitWrappers.Func);
        await using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetServices<Lazy<IExplicitCollectionHandler>>().Single()).IsSameReferenceAs(ExplicitWrappers.Lazy);
        await Assert.That((object)provider.GetServices<Func<IExplicitCollectionHandler>>().Single()).IsSameReferenceAs(ExplicitWrappers.Func);
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
