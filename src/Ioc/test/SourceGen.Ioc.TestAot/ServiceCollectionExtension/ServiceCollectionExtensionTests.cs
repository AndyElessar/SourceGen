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
    public async Task AddTestAot_AsyncOnlyServiceInAScope_SyncDisposeThrowsAndDisposeAsyncDisposes()
    {
        using ServiceProvider provider = Build();
        Journal journal = provider.GetRequiredService<Journal>();
        IServiceScope first = provider.CreateScope();
        _ = first.ServiceProvider.GetRequiredService<AsyncResource>();
        AsyncServiceScope second = provider.CreateAsyncScope();
        _ = second.ServiceProvider.GetRequiredService<AsyncResource>();

        // Neither MS.DI nor the holder disposes the object synchronously (ServiceCollection.Output.spec, "Disposal"). MS.DI 10
        // throws at the first such object, before the holder, and MS.DI 11 collects the exceptions in an AggregateException:
        // one from MS.DI for the object that the factory returned, and one from the holder. DisposeAsync disposes it one
        // time by MS.DI and one time by the holder ("Number of disposals")
        Exception? thrown = await Assert.That(first.Dispose).Throws<Exception>();
        IReadOnlyList<Exception> errors = thrown is AggregateException aggregate ? aggregate.InnerExceptions : [thrown!];
        await Assert.That(errors.Count).IsEqualTo(thrown is AggregateException ? 2 : 1);
        await Assert.That(errors.All(static e => e is InvalidOperationException && e.Message.Contains("only implements IAsyncDisposable", StringComparison.Ordinal))).IsTrue();
        await Assert.That(journal.Disposed).DoesNotContain("AsyncResource");
        await second.DisposeAsync();

        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("AsyncResource,AsyncResource");
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

    [Test]
    public async Task AddTestAot_LazyFuncAndTask_PointToTheRegistrationOfTheService()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        WrapperHost host = scope.ServiceProvider.GetRequiredService<WrapperHost>();

        await Assert.That(host.Session.IsValueCreated).IsFalse();
        await Assert.That(host.Session.Value).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<Session>());
        await Assert.That(host.Sessions()).IsSameReferenceAs(host.Session.Value);
        await Assert.That(host.Jobs()).IsNotSameReferenceAs(host.Jobs());
        await Assert.That(host.Clock.Value).IsSameReferenceAs(provider.GetRequiredService<IClock>());
        IClock clock = host.ClockTask.Result;
        await Assert.That(clock).IsSameReferenceAs(provider.GetRequiredService<IClock>());
        await Assert.That(host.Store.Value).IsSameReferenceAs(provider.GetRequiredKeyedService<IStore>("a"));
    }

    [Test]
    public async Task AddTestAot_WrapperOfAServiceWithoutCandidate_TheSiteCreatesItAndResolvesTheServiceOfTheUser()
    {
        // ADR IocRewrite/0012: the user can register the service after the call
        FallbackTests.External external = new();
        ServiceCollection services = new();
        services.AddTestAot();
        services.AddSingleton<IExternal>(external);
        using ServiceProvider provider = services.BuildServiceProvider();
        using ServiceProvider without = Build();
        using IServiceScope scope = provider.CreateScope();
        using IServiceScope withoutScope = without.CreateScope();

        await Assert.That(scope.ServiceProvider.GetRequiredService<WrapperHost>().External!.Value).IsSameReferenceAs(external);
        await Assert.That(withoutScope.ServiceProvider.GetRequiredService<WrapperHost>().External!.Value).IsNull();
    }

    [Test]
    public async Task AddTestAot_CollectionOfWrappers_OneForEachRegistrationInTheirOrder()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        PluginWrapperHost host = scope.ServiceProvider.GetRequiredService<PluginWrapperHost>();
        Lazy<IPlugin>[] lazy = [.. host.Lazy];

        await Assert.That(lazy.Length).IsEqualTo(2);
        await Assert.That(lazy[0].Value).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<FirstPlugin>());
        await Assert.That(lazy[1].Value).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<SecondPlugin>());
        await Assert.That(host.Funcs.Length).IsEqualTo(2);
        IPlugin second = host.Tasks[1].Result;
        await Assert.That(second).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<SecondPlugin>());

        // A collection of Task<S> is registered as one service, also as IEnumerable, because Task<S> has no registration
        Task<IPlugin>[] tasks = [.. scope.ServiceProvider.GetRequiredService<IEnumerable<Task<IPlugin>>>()];
        await Assert.That(tasks.Length).IsEqualTo(2);
        await Assert.That(tasks[1].IsCompletedSuccessfully).IsTrue();
        IPlugin last = tasks[1].Result;
        await Assert.That(last).IsSameReferenceAs(scope.ServiceProvider.GetRequiredService<SecondPlugin>());
    }

    [Test]
    public async Task AddTestAot_CollectionOfWrappersWithAKeyEqualOnlyAtRunTime_GetsTheRegistrationOfThatKey()
    {
        // MS.DI compares the keys with Equals. The container matches by compile-time identity (SGIOC505)
        using ServiceProvider provider = Build();
        using AppContainer container = new();

        Lazy<IStore> store = provider.GetRequiredService<StoreWrapperAliasConsumer>().Stores.Single();

        await Assert.That(store.Value).IsSameReferenceAs(provider.GetRequiredKeyedService<IStore>("a"));
        await Assert.That(container.GetRequiredService<StoreWrapperAliasConsumer>().Stores.Count()).IsEqualTo(0);
    }

    [Test]
    public async Task AddTestAot_KeyValuePair_RegisteredWithANonGenericDescriptor()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        PairHost host = scope.ServiceProvider.GetRequiredService<PairHost>();
        IStore a = provider.GetRequiredKeyedService<IStore>("a");

        await Assert.That(host.Store.Key).IsEqualTo("a");
        await Assert.That(host.Store.Value).IsSameReferenceAs(a);
        await Assert.That(host.Lazy.Key).IsEqualTo("a");
        await Assert.That(host.Lazy.Value).IsSameReferenceAs(provider.GetRequiredKeyedService<Lazy<IStore>>("a"));
        await Assert.That(host.Lazy.Value.Value).IsSameReferenceAs(a);
        await Assert.That(host.Disk.Key).IsEqualTo(StoreKind.Disk);
        await Assert.That(host.Disk.Value).IsSameReferenceAs(scope.ServiceProvider.GetRequiredKeyedService<IStore>(StoreKind.Disk));
    }

    [Test]
    public async Task AddTestAot_CollectionOfPairsAndDictionary_RegisteredAsOneService()
    {
        using ServiceProvider provider = Build();
        using IServiceScope scope = provider.CreateScope();

        KeyedCollectionHost host = scope.ServiceProvider.GetRequiredService<KeyedCollectionHost>();
        string[] keys = [.. host.Strings.Select(static p => p.Key).Order(StringComparer.Ordinal)];

        await Assert.That(string.Join(",", keys)).IsEqualTo("a,primary");
        await Assert.That(host.Records[new StoreKey("backup")]).IsSameReferenceAs(provider.GetRequiredKeyedService<IStore>(StoreKeys.Backup));
        await Assert.That(host.All.Count).IsEqualTo(5);
        await Assert.That(host.All[StoreKind.Disk].Value).IsSameReferenceAs(scope.ServiceProvider.GetRequiredKeyedService<IStore>(StoreKind.Disk));
        await Assert.That(host.OnlyA.Length).IsEqualTo(1);
        await Assert.That(host.OnlyA[0].Value).IsSameReferenceAs(provider.GetRequiredKeyedService<IStore>("a"));

        // MS.DI cannot collect a value type with NativeAOT: the collection is registered as IEnumerable too
        await Assert.That(scope.ServiceProvider.GetRequiredService<IEnumerable<KeyValuePair<string, IStore>>>().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task AddTestAot_FuncWithArguments_TheCallerSuppliesTheMappedParameter()
    {
        using ServiceProvider provider = Build();

        ReportHost host = provider.GetRequiredService<ReportHost>();
        Report report = host.Reports("first");
        Task<Report> task = host.ReportTasks("task");

        await Assert.That(report.Title).IsEqualTo("first");
        await Assert.That(report.Clock).IsSameReferenceAs(provider.GetRequiredService<IClock>());
        await Assert.That(host.Reports("second")).IsNotSameReferenceAs(report);
        await Assert.That(task.Result.Title).IsEqualTo("task");
    }

    private sealed class Missing : IMissing;
}
