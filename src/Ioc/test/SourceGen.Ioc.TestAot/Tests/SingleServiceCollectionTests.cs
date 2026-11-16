namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Collections of services that the declaring compilation never requests as a collection, so the container has no
/// collection resolver for them (issues #157 and #146): the container serves them from the single-service resolver.
/// The containers are declared in SourceGen.Ioc.TestCase, which does not see the requests made here.
/// </summary>
public sealed class SingleServiceCollectionTests
{
    private static IServiceProvider CreateContainer(bool useSwitchStatement) =>
        useSwitchStatement ? new SingleServiceCollectionSwitchModule() : new SingleServiceCollectionModule();

    // A fallback provider that does not contain the container's registrations, like the one built from the user's IServiceCollection
    private static IServiceProvider CreateContainerWithFallback(bool useSwitchStatement, IServiceProvider fallbackProvider) =>
        useSwitchStatement
            ? new SingleServiceCollectionSwitchModule(fallbackProvider)
            : new SingleServiceCollectionModule(fallbackProvider);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_SingleRegistrationWithoutCollectionResolver_ReturnsContainerInstance(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IServiceProvider)container;

        var services = provider.GetServices<ISingleCollectionService>().ToList();

        await Assert.That(services.Count).IsEqualTo(1);
        await Assert.That(services[0]).IsSameReferenceAs(provider.GetRequiredService<ISingleCollectionService>());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetService_EnumerableOfSingleRegistration_ReturnsArrayOfContainerInstance(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IServiceProvider)container;

        var services = provider.GetService(typeof(IEnumerable<ISingleCollectionService>));

        await Assert.That(services).IsTypeOf<ISingleCollectionService[]>();
        await Assert.That(((ISingleCollectionService[])services!).Single()).IsSameReferenceAs(provider.GetRequiredService<ISingleCollectionService>());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_SingleRegistrationWithFallbackProvider_ReturnsContainerInstance(bool useSwitchStatement)
    {
        await using var fallbackProvider = new ServiceCollection().BuildServiceProvider();
        var provider = CreateContainerWithFallback(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;

        var services = provider.GetServices<ISingleCollectionService>().ToList();

        await Assert.That(services.Count).IsEqualTo(1);
        await Assert.That(services[0]).IsSameReferenceAs(provider.GetRequiredService<ISingleCollectionService>());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_SingleScopedRegistrationInScope_ReturnsScopeInstance(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        using var scope = ((IServiceProvider)container).CreateScope();

        var services = scope.ServiceProvider.GetServices<ISingleCollectionService>().ToList();

        await Assert.That(services.Count).IsEqualTo(1);
        await Assert.That(services[0]).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<ISingleCollectionService>());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_MultipleRegistrations_ReturnsAllRegistrations(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IServiceProvider)container;

        var services = provider.GetServices<IMultiCollectionService>().ToList();

        await Assert.That(services.Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServices_SingleKeyedRegistration_ReturnsKeyedInstance(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IServiceProvider)container;

        var services = provider.GetKeyedServices<IKeyedSingleCollectionService>("alpha").ToList();
        var nonGeneric = ((IKeyedServiceProvider)provider).GetKeyedService(typeof(IEnumerable<IKeyedSingleCollectionService>), "alpha");

        await Assert.That(services.Count).IsEqualTo(1);
        await Assert.That(services[0]).IsSameReferenceAs(provider.GetRequiredKeyedService<IKeyedSingleCollectionService>("alpha"));
        await Assert.That(((IKeyedSingleCollectionService[])nonGeneric!).Single()).IsSameReferenceAs(services[0]);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedService_EnumerableWithUnregisteredKey_ReturnsNull(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IKeyedServiceProvider)container;

        var services = provider.GetKeyedService(typeof(IEnumerable<IKeyedSingleCollectionService>), "beta");

        await Assert.That(services).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedService_EnumerableWithAnyKey_ReturnsKeyedRegistrationsOnly(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IKeyedServiceProvider)container;

        // Like MS.DI, AnyKey requests the keyed registrations, not the non-keyed one
        var services = (IEnumerable<IKeyedSingleCollectionService>?)provider.GetKeyedService(typeof(IEnumerable<IKeyedSingleCollectionService>), KeyedService.AnyKey);

        await Assert.That(services).IsNotNull();
        await Assert.That(services!.Single().Name).IsEqualTo("Alpha");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task KeyedEnumerableDependency_SingleKeyedRegistration_ContainsKeyedInstance(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IServiceProvider)container;

        var consumer = provider.GetRequiredService<KeyedSingleCollectionConsumer>();

        await Assert.That(consumer.Services.Single()).IsSameReferenceAs(provider.GetRequiredKeyedService<IKeyedConsumedCollectionService>("alpha"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetService_EnumerableOfAsyncInitRegistration_ReturnsNull(bool useSwitchStatement)
    {
        await using var container = (IAsyncDisposable)CreateContainer(useSwitchStatement);
        var provider = (IServiceProvider)container;

        await Assert.That(provider.GetService(typeof(IEnumerable<IAsyncSingleCollectionService>))).IsNull();
        await Assert.That(provider.GetService(typeof(IEnumerable<Task<IAsyncSingleCollectionService>>))).IsNull();
    }

    [Test]
    public async Task GenericGetService_EnumerableOfSingleRegistration_DictionaryMode_ReturnsContainerInstance()
    {
        await using var container = new SingleServiceCollectionModule();

        await Assert.That(container.GetRequiredService<IEnumerable<ISingleCollectionService>>().Single()).IsSameReferenceAs(container.GetRequiredService<ISingleCollectionService>());
        await Assert.That(container.GetService<IEnumerable<ISingleCollectionService>>()!.Single()).IsSameReferenceAs(container.GetRequiredService<ISingleCollectionService>());
        await Assert.That(container.GetKeyedService<IEnumerable<IKeyedSingleCollectionService>>("alpha")!.Single().Name).IsEqualTo("Alpha");
        await Assert.That(container.GetRequiredKeyedService<IEnumerable<IKeyedSingleCollectionService>>("alpha").Single().Name).IsEqualTo("Alpha");
    }

    [Test]
    public async Task GenericGetService_EnumerableOfSingleRegistration_SwitchMode_ReturnsContainerInstance()
    {
        await using var container = new SingleServiceCollectionSwitchModule();

        await Assert.That(container.GetRequiredService<IEnumerable<ISingleCollectionService>>().Single()).IsSameReferenceAs(container.GetRequiredService<ISingleCollectionService>());
        await Assert.That(container.GetRequiredKeyedService<IEnumerable<IKeyedSingleCollectionService>>("alpha").Single().Name).IsEqualTo("Alpha");
    }

    [Test]
    public async Task GenericGetServices_DictionaryMode_ReturnsContainerInstances()
    {
        await using var container = new SingleServiceCollectionModule();

        await Assert.That(container.GetServices<ISingleCollectionService>().Single()).IsSameReferenceAs(container.GetRequiredService<ISingleCollectionService>());
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>("alpha").Single().Name).IsEqualTo("Alpha");
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>(KeyedService.AnyKey).Single().Name).IsEqualTo("Alpha");
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>(null).Single().Name).IsEqualTo("Default");
        await Assert.That(container.GetServices<IAsyncSingleCollectionService>()).IsEmpty();
        await Assert.That(container.GetServices<Task<IAsyncSingleCollectionService>>()).IsEmpty();
        await Assert.That(container.GetServices<IMultiCollectionService>().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task GenericGetServices_SwitchMode_ReturnsContainerInstances()
    {
        await using var container = new SingleServiceCollectionSwitchModule();

        await Assert.That(container.GetServices<ISingleCollectionService>().Single()).IsSameReferenceAs(container.GetRequiredService<ISingleCollectionService>());
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>("alpha").Single().Name).IsEqualTo("Alpha");
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>(KeyedService.AnyKey).Single().Name).IsEqualTo("Alpha");
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>(null).Single().Name).IsEqualTo("Default");
        await Assert.That(container.GetServices<IAsyncSingleCollectionService>()).IsEmpty();
        await Assert.That(container.GetServices<Task<IAsyncSingleCollectionService>>()).IsEmpty();
        await Assert.That(container.GetServices<IMultiCollectionService>().Count()).IsEqualTo(2);
    }
}
