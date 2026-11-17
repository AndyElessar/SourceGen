namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Keyed collections with several registrations under one key, and <see cref="KeyedService.AnyKey"/> collections, which
/// must match <c>MS.DI</c>. The containers are declared in <c>SourceGen.Ioc.TestCase</c>, which never requests these
/// collections (except through its consumer), so they do not depend on demand detection.
/// </summary>
public sealed class KeyedMultiCollectionTests
{
    // Registration order of the containers: Alpha1 ("alpha"), Beta ("beta"), Alpha2 ("alpha"), Default (non-keyed),
    // Enum1 and Enum2 (KeyedMultiKind.First), Csharp1 and Csharp2 (KeyedMultiKeys.Csharp)
    private const string AllKeyed = "Alpha1,Beta,Alpha2,Enum1,Enum2,Csharp1,Csharp2";

    private sealed class FallbackKeyedMultiService(string name) : IKeyedMultiService
    {
        public string Name => name;
    }

    private static string Names(object? services) =>
        services is IEnumerable<IKeyedMultiService> enumerable ? string.Join(",", enumerable.Select(static s => s.Name)) : "<null>";

    private static IServiceProvider CreateContainer(bool useSwitchStatement, IServiceProvider? fallbackProvider = null) =>
        useSwitchStatement
            ? new KeyedMultiCollectionSwitchModule(fallbackProvider)
            : new KeyedMultiCollectionModule(fallbackProvider);

    private static ServiceProvider CreateFallbackProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKeyedMultiService>(new FallbackKeyedMultiService("FallbackDefault"));
        services.AddKeyedSingleton<IKeyedMultiService>("alpha", new FallbackKeyedMultiService("FallbackAlpha"));
        services.AddKeyedSingleton<IKeyedMultiService>("gamma", new FallbackKeyedMultiService("FallbackGamma"));
        return services.BuildServiceProvider();
    }

    /// <summary>The same registrations in <c>MS.DI</c>, as the reference behavior.</summary>
    private static ServiceProvider CreateMsDiProvider()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IKeyedMultiService>("alpha", new FallbackKeyedMultiService("Alpha1"));
        services.AddKeyedSingleton<IKeyedMultiService>("beta", new FallbackKeyedMultiService("Beta"));
        services.AddKeyedSingleton<IKeyedMultiService>("alpha", new FallbackKeyedMultiService("Alpha2"));
        services.AddSingleton<IKeyedMultiService>(new FallbackKeyedMultiService("Default"));
        services.AddKeyedSingleton<IKeyedMultiService>(KeyedMultiKind.First, new FallbackKeyedMultiService("Enum1"));
        services.AddKeyedSingleton<IKeyedMultiService>(KeyedMultiKind.First, new FallbackKeyedMultiService("Enum2"));
        services.AddKeyedSingleton<IKeyedMultiService>(KeyedMultiKeys.Csharp, new FallbackKeyedMultiService("Csharp1"));
        services.AddKeyedSingleton<IKeyedMultiService>(KeyedMultiKeys.Csharp, new FallbackKeyedMultiService("Csharp2"));
        return services.BuildServiceProvider();
    }

    #region MS.DI reference behavior

    [Test]
    public async Task MsDi_GetKeyedServicesAnyKey_ReturnsAllKeyedRegistrationsInRegistrationOrder()
    {
        await using var provider = CreateMsDiProvider();

        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo(AllKeyed);
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>("alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(null))).IsEqualTo("Default");
        await Assert.That(Names(provider.GetKeyedService(typeof(IEnumerable<IKeyedMultiService>), KeyedService.AnyKey))).IsEqualTo(AllKeyed);
    }

    [Test]
    public async Task MsDi_GetKeyedServicesAnyKey_ExcludesAnyKeyRegistrations()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IKeyedMultiService>("alpha", new FallbackKeyedMultiService("Alpha"));
        services.AddKeyedSingleton<IKeyedMultiService>(KeyedService.AnyKey, (_, key) => new FallbackKeyedMultiService($"Any:{key}"));
        await using var provider = services.BuildServiceProvider();

        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo("Alpha");
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>("other"))).IsEqualTo("");
    }

    [Test]
    public async Task MsDi_GetKeyedServiceAnyKey_SingleServiceThrows()
    {
        await using var provider = CreateMsDiProvider();

        await Assert.That(() => provider.GetKeyedService<IKeyedMultiService>(KeyedService.AnyKey)).Throws<InvalidOperationException>();
        await Assert.That(() => provider.GetKeyedService(typeof(IReadOnlyList<IKeyedMultiService>), KeyedService.AnyKey)).Throws<InvalidOperationException>();
    }

    #endregion

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServices_TwoRegistrationsUnderOneKey_ReturnsBothInRegistrationOrder(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();
        var keyed = (IKeyedServiceProvider)scope.ServiceProvider;

        await Assert.That(Names(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>("alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetKeyedService(typeof(IEnumerable<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetRequiredKeyedService(typeof(IEnumerable<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetKeyedService(typeof(IReadOnlyList<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetKeyedService(typeof(IReadOnlyCollection<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetKeyedService(typeof(ICollection<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetKeyedService(typeof(IList<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(keyed.GetKeyedService(typeof(IKeyedMultiService[]), "alpha"))).IsEqualTo("Alpha1,Alpha2");

        // The single service is the last registration, which is the last element of the collection
        var single = scope.ServiceProvider.GetRequiredKeyedService<IKeyedMultiService>("alpha");
        await Assert.That(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>("alpha").Last()).IsSameReferenceAs(single);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServices_EnumAndCsharpKeys_ReturnBothRegistrations(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement);
        await using var disposable = (IAsyncDisposable)provider;

        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(KeyedMultiKind.First))).IsEqualTo("Enum1,Enum2");
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(KeyedMultiKeys.Csharp))).IsEqualTo("Csharp1,Csharp2");
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>("beta"))).IsEqualTo("Beta");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GenericGetKeyedServices_TwoRegistrationsUnderOneKey_ReturnsBoth(bool useSwitchStatement)
    {
        if(useSwitchStatement)
        {
            await using var container = new KeyedMultiCollectionSwitchModule();
            await Assert.That(Names(container.GetKeyedServices<IKeyedMultiService>(KeyedMultiKind.First))).IsEqualTo("Enum1,Enum2");
            await Assert.That(Names(container.GetRequiredKeyedService<IEnumerable<IKeyedMultiService>>(KeyedMultiKind.First))).IsEqualTo("Enum1,Enum2");
            await Assert.That(Names(container.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo(AllKeyed);
        }
        else
        {
            await using var container = new KeyedMultiCollectionModule();
            await Assert.That(Names(container.GetKeyedServices<IKeyedMultiService>(KeyedMultiKind.First))).IsEqualTo("Enum1,Enum2");
            await Assert.That(Names(container.GetRequiredKeyedService<IEnumerable<IKeyedMultiService>>(KeyedMultiKind.First))).IsEqualTo("Enum1,Enum2");
            await Assert.That(Names(container.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo(AllKeyed);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task KeyedCollectionDependency_TwoRegistrationsUnderOneKey_ContainsBoth(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();

        var consumer = scope.ServiceProvider.GetRequiredService<KeyedMultiCollectionConsumer>();

        await Assert.That(Names(consumer.Enumerable)).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(consumer.ReadOnlyList)).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(consumer.Array)).IsEqualTo("Alpha1,Alpha2");
        await Assert.That(Names(consumer.Single)).IsEqualTo("Beta");
        await Assert.That(Names(consumer.AllKeyed)).IsEqualTo(AllKeyed);
        await Assert.That(consumer.Enumerable.Last()).IsSameReferenceAs(scope.ServiceProvider.GetRequiredKeyedService<IKeyedMultiService>("alpha"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServices_WithFallbackRegistrations_ReturnsFallbackThenContainerServices(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;
        using var scope = provider.CreateScope();

        await Assert.That(Names(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>("alpha"))).IsEqualTo("FallbackAlpha,Alpha1,Alpha2");
        await Assert.That(Names(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>(KeyedMultiKind.First))).IsEqualTo("Enum1,Enum2");
        await Assert.That(Names(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>("gamma"))).IsEqualTo("FallbackGamma");
        await Assert.That(Names(scope.ServiceProvider.GetRequiredService<KeyedMultiCollectionConsumer>().Enumerable)).IsEqualTo("FallbackAlpha,Alpha1,Alpha2");
        await Assert.That(Names(scope.ServiceProvider.GetRequiredService<KeyedMultiCollectionConsumer>().Array)).IsEqualTo("FallbackAlpha,Alpha1,Alpha2");
        await Assert.That(Names(scope.ServiceProvider.GetRequiredService<KeyedMultiCollectionConsumer>().AllKeyed)).IsEqualTo($"FallbackAlpha,FallbackGamma,{AllKeyed}");

        // Other collection types are not served by MS.DI and stay container-only, like the non-keyed collections
        await Assert.That(Names(((IKeyedServiceProvider)scope.ServiceProvider).GetKeyedService(typeof(IReadOnlyList<IKeyedMultiService>), "alpha"))).IsEqualTo("Alpha1,Alpha2");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServicesAnyKey_ReturnsAllKeyedServicesLikeMsDi(bool useSwitchStatement)
    {
        await using var msDi = CreateMsDiProvider();
        var provider = CreateContainer(useSwitchStatement);
        await using var disposable = (IAsyncDisposable)provider;
        var keyed = (IKeyedServiceProvider)provider;

        var expected = Names(msDi.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey));
        await Assert.That(expected).IsEqualTo(AllKeyed);
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo(expected);
        await Assert.That(Names(keyed.GetKeyedService(typeof(IEnumerable<IKeyedMultiService>), KeyedService.AnyKey))).IsEqualTo(expected);
        await Assert.That(Names(keyed.GetRequiredKeyedService(typeof(IEnumerable<IKeyedMultiService>), KeyedService.AnyKey))).IsEqualTo(expected);

        // AnyKey no longer means "non-keyed": the single service is not served by the container (MS.DI throws)
        await Assert.That(keyed.GetKeyedService(typeof(IKeyedMultiService), KeyedService.AnyKey)).IsNull();
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(null))).IsEqualTo("Default");
        await Assert.That(provider.GetRequiredKeyedService<IKeyedMultiService>(null).Name).IsEqualTo("Default");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetKeyedServicesAnyKey_WithFallback_ReturnsFallbackAnyKeyThenContainerServices(bool useSwitchStatement)
    {
        await using var fallbackProvider = CreateFallbackProvider();
        var provider = CreateContainer(useSwitchStatement, fallbackProvider);
        await using var disposable = (IAsyncDisposable)provider;

        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo($"FallbackAlpha,FallbackGamma,{AllKeyed}");
        await Assert.That(Names(provider.GetKeyedServices<IKeyedMultiService>(null))).IsEqualTo("FallbackDefault,Default");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IsKeyedService_KeyedCollections_ReturnsTrue(bool useSwitchStatement)
    {
        var provider = CreateContainer(useSwitchStatement);
        await using var disposable = (IAsyncDisposable)provider;
        var isKeyed = (IServiceProviderIsKeyedService)provider;

        await Assert.That(isKeyed.IsKeyedService(typeof(IEnumerable<IKeyedMultiService>), "alpha")).IsTrue();
        await Assert.That(isKeyed.IsKeyedService(typeof(IReadOnlyList<IKeyedMultiService>), "alpha")).IsTrue();
        await Assert.That(isKeyed.IsKeyedService(typeof(IKeyedMultiService[]), KeyedMultiKind.First)).IsTrue();
        await Assert.That(isKeyed.IsKeyedService(typeof(IEnumerable<IKeyedMultiService>), KeyedService.AnyKey)).IsTrue();
        await Assert.That(isKeyed.IsKeyedService(typeof(IKeyedMultiService), "alpha")).IsTrue();
        await Assert.That(isKeyed.IsKeyedService(typeof(IKeyedMultiService), null)).IsTrue();
        await Assert.That(isKeyed.IsKeyedService(typeof(IKeyedMultiService), KeyedService.AnyKey)).IsFalse();
        await Assert.That(isKeyed.IsKeyedService(typeof(IReadOnlyList<IKeyedMultiService>), "beta")).IsFalse();
    }

    [Test]
    public async Task ImportedModule_KeyedCollections_MergeFallbackServicesOnceThroughImporter()
    {
        await using var fallbackProvider = CreateFallbackProvider();
        await using var container = new KeyedMultiModuleImporter(fallbackProvider);
        using var scope = container.CreateScope();

        await Assert.That(Names(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>("alpha"))).IsEqualTo("FallbackAlpha,Alpha1,Alpha2");
        await Assert.That(Names(scope.ServiceProvider.GetKeyedServices<IKeyedMultiService>(KeyedService.AnyKey))).IsEqualTo("FallbackAlpha,FallbackGamma,Alpha1,Beta,Alpha2");
        await Assert.That(Names(scope.ServiceProvider.GetRequiredService<ImportedKeyedMultiCollectionConsumer>().Services)).IsEqualTo("FallbackAlpha,Alpha1,Alpha2");
        await Assert.That(Names(((IKeyedServiceProvider)scope.ServiceProvider).GetKeyedService(typeof(IKeyedMultiService[]), "alpha"))).IsEqualTo("Alpha1,Alpha2");
    }
}
