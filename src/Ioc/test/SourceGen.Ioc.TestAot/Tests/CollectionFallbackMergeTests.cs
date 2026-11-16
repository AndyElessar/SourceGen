namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Collections combine the fallback provider's registrations with the container's (fallback first), like
/// <c>MS.DI</c> combines all registrations of a service type: e.g. a single <c>IHostedService</c> registered in the
/// container next to the hosted services the host registered in the <c>IServiceCollection</c>.
/// Also covers the collection entries of switch-mode containers and <c>IsService</c> of <c>IEnumerable&lt;T&gt;</c>.
/// </summary>
public sealed class CollectionFallbackMergeTests
{
    private sealed class FallbackSingleCollectionService : ISingleCollectionService;

    private sealed class FallbackMultiCollectionService : IMultiCollectionService;

    private sealed class FallbackKeyedSingleCollectionService : IKeyedSingleCollectionService
    {
        public string Name => "Fallback";
    }

    private static ServiceProvider CreateFallbackProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISingleCollectionService, FallbackSingleCollectionService>();
        services.AddSingleton<IMultiCollectionService, FallbackMultiCollectionService>();
        services.AddKeyedSingleton<IKeyedSingleCollectionService, FallbackKeyedSingleCollectionService>("alpha");
        return services.BuildServiceProvider();
    }

    private static IServiceProvider CreateContainer(bool useSwitchStatement, IServiceProvider? fallbackProvider) =>
        useSwitchStatement
            ? new SingleServiceCollectionSwitchModule(fallbackProvider)
            : new SingleServiceCollectionModule(fallbackProvider);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_SingleRegistrationAndFallbackRegistration_ReturnsFallbackThenContainerInstance(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;
        var containerInstance = provider.GetRequiredService<ISingleCollectionService>();

        var services = provider.GetServices<ISingleCollectionService>().ToList();
        var nonGeneric = ((IEnumerable<ISingleCollectionService>)provider.GetService(typeof(IEnumerable<ISingleCollectionService>))!).ToList();

        await Assert.That(services.Count).IsEqualTo(2);
        await Assert.That(services[0]).IsTypeOf<FallbackSingleCollectionService>();
        await Assert.That(services[1]).IsSameReferenceAs(containerInstance);
        await Assert.That(nonGeneric.Count).IsEqualTo(2);
        await Assert.That(nonGeneric[1]).IsSameReferenceAs(containerInstance);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_MultipleRegistrationsAndFallbackRegistration_ReturnsAll(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;

        var services = provider.GetServices<IMultiCollectionService>().ToList();

        await Assert.That(services.Count).IsEqualTo(3);
        await Assert.That(services[0]).IsTypeOf<FallbackMultiCollectionService>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServices_KeyedRegistrationAndFallbackRegistration_ReturnsBoth(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;

        var names = provider.GetKeyedServices<IKeyedSingleCollectionService>("alpha").Select(static s => s.Name).ToList();

        await Assert.That(names.Count).IsEqualTo(2);
        await Assert.That(names[0]).IsEqualTo("Fallback");
        await Assert.That(names[1]).IsEqualTo("Alpha");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetServices_ScopeWithFallbackRegistration_ReturnsFallbackThenScopeInstance(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();

        var services = scope.ServiceProvider.GetServices<ISingleCollectionService>().ToList();

        await Assert.That(services.Count).IsEqualTo(2);
        await Assert.That(services[1]).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<ISingleCollectionService>());
    }

    [Test]
    public async Task GenericGetServices_DictionaryModeWithFallbackRegistration_ReturnsAll()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new SingleServiceCollectionModule(fallbackProvider);

        await Assert.That(container.GetServices<ISingleCollectionService>().Count()).IsEqualTo(2);
        await Assert.That(container.GetServices<IMultiCollectionService>().Count()).IsEqualTo(3);
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>("alpha").Count()).IsEqualTo(2);
        await Assert.That(container.GetRequiredService<IEnumerable<ISingleCollectionService>>().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task GenericGetServices_SwitchModeWithFallbackRegistration_ReturnsAll()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new SingleServiceCollectionSwitchModule(fallbackProvider);

        await Assert.That(container.GetServices<ISingleCollectionService>().Count()).IsEqualTo(2);
        await Assert.That(container.GetServices<IMultiCollectionService>().Count()).IsEqualTo(3);
        await Assert.That(container.GetKeyedServices<IKeyedSingleCollectionService>("alpha").Count()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetService_GeneratedCollectionTypes_ReturnAllContainerRegistrations(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement, null);
        await using var disposable = (IAsyncDisposable)provider;

        await Assert.That(((IReadOnlyList<IMultiCollectionService>)provider.GetService(typeof(IReadOnlyList<IMultiCollectionService>))!).Count).IsEqualTo(2);
        await Assert.That(((IReadOnlyCollection<IMultiCollectionService>)provider.GetService(typeof(IReadOnlyCollection<IMultiCollectionService>))!).Count).IsEqualTo(2);
        await Assert.That(((ICollection<IMultiCollectionService>)provider.GetService(typeof(ICollection<IMultiCollectionService>))!).Count).IsEqualTo(2);
        await Assert.That(((IList<IMultiCollectionService>)provider.GetService(typeof(IList<IMultiCollectionService>))!).Count).IsEqualTo(2);
        await Assert.That(((IMultiCollectionService[])provider.GetService(typeof(IMultiCollectionService[]))!).Length).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IsService_EnumerableOfResolvableElement_ReturnsTrue(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement, null);
        await using var disposable = (IAsyncDisposable)provider;
        var isService = (IServiceProviderIsService)provider;
        var isKeyedService = (IServiceProviderIsKeyedService)provider;

        await Assert.That(isService.IsService(typeof(IEnumerable<ISingleCollectionService>))).IsTrue();
        await Assert.That(isService.IsService(typeof(IEnumerable<IMultiCollectionService>))).IsTrue();
        await Assert.That(isService.IsService(typeof(IReadOnlyList<IMultiCollectionService>))).IsTrue();
        await Assert.That(isKeyedService.IsKeyedService(typeof(IEnumerable<IKeyedSingleCollectionService>), "alpha")).IsTrue();
        await Assert.That(isKeyedService.IsKeyedService(typeof(IEnumerable<IKeyedSingleCollectionService>), KeyedService.AnyKey)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IsService_EnumerableOfUnresolvableElement_ReturnsFalseWithoutFallback(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement, null);
        await using var disposable = (IAsyncDisposable)provider;
        var isService = (IServiceProviderIsService)provider;
        var isKeyedService = (IServiceProviderIsKeyedService)provider;

        await Assert.That(isService.IsService(typeof(IEnumerable<IAsyncSingleCollectionService>))).IsFalse();
        await Assert.That(isService.IsService(typeof(IEnumerable<Task<IAsyncSingleCollectionService>>))).IsFalse();
        await Assert.That(isKeyedService.IsKeyedService(typeof(IEnumerable<IKeyedSingleCollectionService>), "beta")).IsFalse();
        await Assert.That(isKeyedService.IsKeyedService(typeof(IEnumerable<ISingleCollectionService>), KeyedService.AnyKey)).IsFalse();
    }
}
