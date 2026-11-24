namespace SourceGen.Ioc.TestAot.Container;

/// <summary>Keyed registrations in the container (Container.Output.spec, "Lookup by Type").</summary>
public class KeyedTests
{
    public sealed class ExternalStore : IStore;

    [Test]
    public async Task GetKeyedService_KeyEqualAtRunTime_FindsTheRegistration()
    {
        using AppContainer root = new();

        IStore byValue = root.GetRequiredKeyedService<IStore>(new string('a', 1));
        IStore bySymbol = root.GetRequiredKeyedService<IStore>(new StoreKey("backup"));

        await Assert.That(root.GetRequiredKeyedService<IStore>("a")).IsSameReferenceAs(byValue);
        await Assert.That(root.GetRequiredKeyedService<Store>("a")).IsSameReferenceAs(byValue);
        await Assert.That(bySymbol).IsNotSameReferenceAs(byValue);
        await Assert.That(root.GetRequiredKeyedService<IStore>(StoreKeys.Primary)).IsNotSameReferenceAs(byValue);
        await Assert.That(root.GetRequiredKeyedService<IStore>(1L)).IsNotSameReferenceAs(byValue);
    }

    [Test]
    public async Task GetKeyedService_KeyOfAnotherTypeOrUnknown_FindsNothing()
    {
        using AppContainer root = new();

        // 1 (int) and 1L (long) are different keys, as in MS.DI
        await Assert.That(root.GetKeyedService<IStore>(1)).IsNull();
        await Assert.That(root.GetKeyedService<IStore>("missing")).IsNull();
        await Assert.That(root.GetService<IStore>()).IsNull();
        await Assert.That(() => root.GetRequiredKeyedService<IStore>("missing")).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GetKeyedService_ScopedKey_OneInstanceForEachScope()
    {
        using AppContainer root = new();
        using AppContainer first = root.CreateScope();
        using AppContainer second = root.CreateScope();

        IStore disk = first.GetRequiredKeyedService<IStore>(StoreKind.Disk);

        await Assert.That(first.GetRequiredKeyedService<IStore>(StoreKind.Disk)).IsSameReferenceAs(disk);
        await Assert.That(second.GetRequiredKeyedService<IStore>(StoreKind.Disk)).IsNotSameReferenceAs(disk);
        await Assert.That(() => root.GetKeyedService<IStore>(StoreKind.Disk)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task GetKeyedServices_ByKeyAndAnyKey_ReturnsTheKeyedRegistrations()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        IStore[] all = [.. scope.GetKeyedServices<IStore>(KeyedService.AnyKey)];
        IStore[] a = [.. scope.GetKeyedServices<IStore>("a")];

        await Assert.That(all.Length).IsEqualTo(5);
        await Assert.That(all.Distinct().Count()).IsEqualTo(5);
        await Assert.That(a.Length).IsEqualTo(1);
        await Assert.That(a[0]).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>("a"));
        await Assert.That(scope.GetKeyedServices<IStore>("missing").Count()).IsEqualTo(0);
        await Assert.That(scope.GetServices<IStore>().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task GetKeyedService_AnyKeyForOneService_Throws()
    {
        using AppContainer root = new();

        await Assert.That(() => root.GetKeyedService<IStore>(KeyedService.AnyKey)).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => root.GetKeyedService<SystemClock>(KeyedService.AnyKey)).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task IsKeyedService_RegisteredKey_True()
    {
        using AppContainer root = new();

        await Assert.That(root.IsKeyedService(typeof(IStore), "a")).IsTrue();
        await Assert.That(root.IsKeyedService(typeof(IStore), new StoreKey("backup"))).IsTrue();
        await Assert.That(root.IsKeyedService(typeof(IStore), "missing")).IsFalse();
        await Assert.That(root.IsService(typeof(IStore))).IsFalse();
    }

    [Test]
    public async Task FallbackProvider_KeyedCollection_MergesTheFallbackServicesFirst()
    {
        ExternalStore external = new();
        ServiceCollection services = new();
        services.AddKeyedSingleton<IStore>("a", external);
        services.AddKeyedSingleton<IStore>("only-fallback", external);
        using ServiceProvider provider = services.BuildServiceProvider();
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();

        IStore[] a = [.. root.GetKeyedServices<IStore>("a")];

        await Assert.That(root.GetRequiredKeyedService<IStore>("a")).IsTypeOf<Store>();
        await Assert.That(a.Length).IsEqualTo(2);
        await Assert.That(a[0]).IsSameReferenceAs(external);
        await Assert.That(a[1]).IsSameReferenceAs(root.GetRequiredKeyedService<IStore>("a"));
        await Assert.That(root.GetRequiredKeyedService<IStore>("only-fallback")).IsSameReferenceAs(external);
        await Assert.That(root.GetKeyedServices<IStore>("only-fallback").Single()).IsSameReferenceAs(external);
        // The Scoped StoreKind.Disk needs a scope
        await Assert.That(scope.GetKeyedServices<IStore>(KeyedService.AnyKey).Count()).IsEqualTo(7);
    }

    [Test]
    public async Task KeyedDependency_EachSite_GetsTheRegistrationOfItsKey()
    {
        using AppContainer root = new();

        StoreConsumer consumer = root.GetRequiredKeyedService<StoreConsumer>("a");

        await Assert.That(consumer.A).IsSameReferenceAs(root.GetRequiredKeyedService<IStore>("a"));
        await Assert.That(consumer.One).IsSameReferenceAs(root.GetRequiredKeyedService<IStore>(1L));
        await Assert.That(consumer.Backup).IsSameReferenceAs(root.GetRequiredKeyedService<IStore>(StoreKeys.Backup));
        await Assert.That(consumer.Inherited).IsSameReferenceAs(consumer.A);
        await Assert.That(consumer.Key).IsEqualTo("a");
    }

    [Test]
    public async Task ServiceKey_EachKey_GetsItsOwnKey()
    {
        using AppContainer root = new();

        await Assert.That(((Store)root.GetRequiredKeyedService<IStore>("a")).Key).IsEqualTo("a");
        await Assert.That(((Store)root.GetRequiredKeyedService<IStore>(1L)).Key).IsEqualTo(1L);
        await Assert.That(((Store)root.GetRequiredKeyedService<IStore>(StoreKeys.Backup)).Key).IsSameReferenceAs(StoreKeys.Backup);
    }

    [Test]
    public async Task PartialAccessor_WithKey_ReturnsTheKeyedRegistration()
    {
        using AppContainer root = new();

        await Assert.That(root.PrimaryStore).IsSameReferenceAs(root.GetRequiredKeyedService<IStore>(StoreKeys.Primary));
    }
}