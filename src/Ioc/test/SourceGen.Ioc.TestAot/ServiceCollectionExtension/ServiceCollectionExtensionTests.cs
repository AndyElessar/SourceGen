using SourceGen.Ioc.TestAot.Container;

namespace SourceGen.Ioc.TestAot.ServiceCollectionExtension;

/// <summary>The service collection extension with MS.DI (ServiceCollection.Output.spec).</summary>
public class ServiceCollectionExtensionTests
{
    private static ServiceProvider Build()
    {
        ServiceCollection services = new();
        services.AddTestAot();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Test]
    public async Task AddTestAot_ThreeLifetimes_ResolveAsRegistered()
    {
        using ServiceProvider provider = Build();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();

        UnitOfWork unitOfWork = first.ServiceProvider.GetRequiredService<UnitOfWork>();

        await Assert.That(first.ServiceProvider.GetRequiredService<Session>()).IsSameReferenceAs(unitOfWork.Session);
        await Assert.That(second.ServiceProvider.GetRequiredService<Session>()).IsNotSameReferenceAs(unitOfWork.Session);
        await Assert.That(unitOfWork.Session.Repository.Clock).IsSameReferenceAs(provider.GetRequiredService<IClock>());
        await Assert.That(provider.GetRequiredService<Formatter>()).IsNotSameReferenceAs(provider.GetRequiredService<Formatter>());
    }

    [Test]
    public async Task AddTestAot_SingletonWithTwoServiceTypes_SharesOneInstance()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        SystemClock clock = scope.ServiceProvider.GetRequiredService<SystemClock>();

        await Assert.That(provider.GetRequiredService<IClock>()).IsSameReferenceAs(clock);
        await Assert.That(scope.ServiceProvider.GetRequiredService<IClock>()).IsSameReferenceAs(clock);
        await Assert.That(clock.Now).IsEqualTo(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public async Task AddTestAot_ProviderDisposed_DisposesByTheHolderAndByEachResolvedServiceType()
    {
        ServiceProvider provider = Build();
        Journal journal = provider.GetRequiredService<Journal>();
        IServiceScope scope = provider.CreateScope();
        UnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<UnitOfWork>();

        scope.Dispose();
        Repository repository = provider.GetRequiredService<Repository>();
        provider.Dispose();

        // MS.DI tracks the holder and each object that a factory delegate returns, so an instance is disposed one time
        // by its holder and one time by each service type that was resolved (ServiceCollection.Output.spec, "Number of
        // disposals"; Constitution, "Documented differences from MS.DI"). The order is the reverse of creation.
        await Assert.That(unitOfWork.Session.Repository).IsSameReferenceAs(repository);
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("UnitOfWork,UnitOfWork,Session,Session,Repository,Repository,SystemClock,SystemClock");
    }

    [Test]
    public async Task AddTestAot_CalledTwice_ThrowsAndLeavesServicesUnchanged()
    {
        ServiceCollection services = new();
        services.AddTestAot();
        int count = services.Count;

        InvalidOperationException? thrown = await Assert.That(() => services.AddTestAot()).ThrowsExactly<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("AddTestAot");
        await Assert.That(thrown.Message).Contains("'SourceGen.Ioc.TestAot'");
        await Assert.That(services.Count).IsEqualTo(count);
    }

    [Test]
    public async Task AddTestAot_OptionalDependencyMissing_GetsNullOrTheDefaultValue()
    {
        using ServiceProvider provider = Build();

        OptionalConsumer consumer = provider.GetRequiredService<OptionalConsumer>();

        await Assert.That(consumer.Clock).IsSameReferenceAs(provider.GetRequiredService<IClock>());
        await Assert.That(consumer.Missing).IsNull();
        await Assert.That(consumer.Label).IsEqualTo("none");
        await Assert.That(consumer.Retries).IsEqualTo(3);
        await Assert.That(consumer.Level).IsEqualTo(Level.High);
        await Assert.That(consumer.Limit).IsNull();
    }

    [Test]
    public async Task AddTestAot_OptionalDependencyRegisteredByTheUser_GetsTheService()
    {
        ServiceCollection services = new();
        services.AddTestAot();
        Missing missing = new();
        services.AddSingleton<IMissing>(missing);
        services.AddSingleton(typeof(int), (object)5);
        services.AddSingleton(typeof(int?), (object)7);
        services.AddSingleton<string>("label");
        using ServiceProvider provider = services.BuildServiceProvider();

        OptionalConsumer consumer = provider.GetRequiredService<OptionalConsumer>();

        await Assert.That(consumer.Missing).IsSameReferenceAs(missing);
        await Assert.That(consumer.Retries).IsEqualTo(5);
        await Assert.That(consumer.Limit).IsEqualTo(7);
        await Assert.That(consumer.Label).IsEqualTo("label");
        await Assert.That(consumer.Level).IsEqualTo(Level.High);
    }

    [Test]
    public async Task AddTestAot_KeyedRegistrations_ResolveByKey()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        IStore a = provider.GetRequiredKeyedService<IStore>(new string('a', 1));

        await Assert.That(provider.GetRequiredKeyedService<Store>("a")).IsSameReferenceAs(a);
        await Assert.That(provider.GetRequiredKeyedService<IStore>(new StoreKey("backup"))).IsNotSameReferenceAs(a);
        await Assert.That(provider.GetKeyedService<IStore>(1)).IsNull();
        await Assert.That(provider.GetRequiredKeyedService<IStore>(1L)).IsNotSameReferenceAs(a);
        await Assert.That(scope.ServiceProvider.GetKeyedServices<IStore>(KeyedService.AnyKey).Count()).IsEqualTo(5);
        await Assert.That(provider.GetService<IStore>()).IsNull();

        StoreConsumer consumer = provider.GetRequiredKeyedService<StoreConsumer>("a");
        await Assert.That(consumer.A).IsSameReferenceAs(a);
        await Assert.That(consumer.Inherited).IsSameReferenceAs(a);
        await Assert.That(consumer.Backup).IsSameReferenceAs(provider.GetRequiredKeyedService<IStore>(StoreKeys.Backup));
        await Assert.That(consumer.Key).IsEqualTo("a");
        await Assert.That(((Store)a).Key).IsEqualTo("a");
    }

    [Test]
    public async Task AddTestAot_InjectedMembers_AssignedInTheOrderOfTheSteps()
    {
        ServiceCollection services = new();
        services.AddTestAot();
        services.AddSingleton<string>("label");
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope scope = provider.CreateScope();

        InjectedScoped service = scope.ServiceProvider.GetRequiredService<InjectedScoped>();

        await Assert.That(service.Session).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<Session>());
        await Assert.That(service.Repository).IsSameReferenceAs(provider.GetRequiredService<Repository>());
        await Assert.That(service.BaseClock).IsSameReferenceAs(provider.GetRequiredService<IClock>());
        await Assert.That(service.Missing).IsNull();
        await Assert.That(service.Label).IsEqualTo("label");
        await Assert.That(string.Join(",", service.Steps)).IsEqualTo("InitializeBase:True,Initialize:True:True:True");
    }

    [Test]
    public async Task AddTestAot_InjectionMethodThrows_DisposesTheInstanceOneTime()
    {
        using ServiceProvider provider = Build();
        Journal journal = provider.GetRequiredService<Journal>();
        IServiceScope scope = provider.CreateScope();

        Exception? thrown = Container.MemberInjectionTests.CatchInitialize(() => scope.ServiceProvider.GetService<InjectedScoped>());

        // MS.DI does not wrap the exception, and the creation method disposed the instance that nobody owns
        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("Initialize failed.");
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("InjectedScoped");

        InjectedScoped service = scope.ServiceProvider.GetRequiredService<InjectedScoped>();
        scope.Dispose();

        await Assert.That(service.Steps.Count).IsEqualTo(2);
        await Assert.That(journal.Disposed.Count(static d => d == "InjectedScoped")).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task AddTestAot_CollectionDependency_GetsTheRegistrationsInTheirOrder()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        PluginHost host = scope.ServiceProvider.GetRequiredService<PluginHost>();
        IPlugin[] all = [.. host.All];

        // The other collection types are registered as Transient services, built from the collection of MS.DI
        await Assert.That(all.Length).IsEqualTo(2);
        await Assert.That(all[0]).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<FirstPlugin>());
        await Assert.That(all[1]).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<SecondPlugin>());
        await Assert.That(host.List[1]).IsSameReferenceAs(all[1]);
        await Assert.That(host.Array.Length).IsEqualTo(2);
        await Assert.That(host.Extra.Single()).IsSameReferenceAs(scope.ServiceProvider.GetRequiredKeyedService<IPlugin>("extra"));
        await Assert.That(host.Keyed.Single()).IsSameReferenceAs(host.Extra[0]);
        await Assert.That(scope.ServiceProvider.GetRequiredService<IReadOnlyList<IPlugin>>().Count).IsEqualTo(2);
    }

    private sealed class Missing : IMissing;
}
