namespace SourceGen.Ioc.TestAot.Container;

/// <summary><c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> and <c>Task&lt;T&gt;</c> in the container (Container.Output.spec, "Wrapper types").</summary>
public class WrapperTests
{
    [Test]
    public async Task Lazy_CreatesTheServiceOnFirstUse()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        WrapperHost host = scope.GetRequiredService<WrapperHost>();

        await Assert.That(host.Session.IsValueCreated).IsFalse();
        await Assert.That(host.Session.Value).IsSameReferenceAs(scope.GetSession());
        await Assert.That(host.Clock.Value).IsSameReferenceAs(root.Clock);
    }

    [Test]
    public async Task Wrapper_SingletonService_TheRootCachesOne()
    {
        using AppContainer root = new();
        using AppContainer first = root.CreateScope();
        using AppContainer second = root.CreateScope();

        WrapperHost one = first.GetRequiredService<WrapperHost>();
        WrapperHost two = second.GetRequiredService<WrapperHost>();

        await Assert.That(two.Clock).IsSameReferenceAs(one.Clock);
        await Assert.That(ReferenceEquals(two.ClockTask, one.ClockTask)).IsTrue();
        await Assert.That(one.ClockTask.IsCompletedSuccessfully).IsTrue();
        IClock clock = one.ClockTask.Result;
        await Assert.That(clock).IsSameReferenceAs(root.Clock);
    }

    [Test]
    public async Task Wrapper_ScopedService_EachScopeCachesOne()
    {
        using AppContainer root = new();
        using AppContainer first = root.CreateScope();
        using AppContainer second = root.CreateScope();

        // Assert.That(Func<T>) calls the delegate, so the delegates are compared with ReferenceEquals
        Func<Session> sessions = first.GetRequiredService<WrapperHost>().Sessions;

        await Assert.That(ReferenceEquals(first.GetRequiredService<Func<Session>>(), sessions)).IsTrue();
        await Assert.That(ReferenceEquals(second.GetRequiredService<Func<Session>>(), sessions)).IsFalse();
        await Assert.That(sessions()).IsSameReferenceAs(first.GetSession());
    }

    [Test]
    public async Task Func_TransientService_EachCallCreatesOneAndEachInstanceCachesTheFunc()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        Func<Job> jobs = scope.GetRequiredService<WrapperHost>().Jobs;

        await Assert.That(jobs()).IsNotSameReferenceAs(jobs());
        await Assert.That(ReferenceEquals(scope.GetRequiredService<Func<Job>>(), jobs)).IsTrue();
        await Assert.That(ReferenceEquals(root.Jobs, jobs)).IsFalse();
        await Assert.That(ReferenceEquals(root.Jobs, root.Jobs)).IsTrue();
        Task<Job> job = scope.GetRequiredService<WrapperHost>().JobTasks();
        await Assert.That(job.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task Lazy_CachedAndTheCreationFails_TheNextValueCreatesItAgain()
    {
        // No eager resolution: the first creation happens in Value
        using LazyLockContainer root = new();
        Lazy<FlakyService> flaky = root.GetRequiredService<FlakyHost>().Flaky;

        FlakyService.FailNextCreation = true;
        await Assert.That(() => flaky.Value).ThrowsExactly<InvalidOperationException>();
        await Assert.That(flaky.Value).IsSameReferenceAs(root.GetRequiredService<FlakyService>());
    }

    [Test]
    public async Task Wrapper_KeyedSite_TheLookupByTypeAndKeyGivesTheSameWrapper()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        Lazy<IStore> store = scope.GetRequiredService<WrapperHost>().Store;

        await Assert.That(scope.GetRequiredKeyedService<Lazy<IStore>>("a")).IsSameReferenceAs(store);
        await Assert.That(store.Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>("a"));
    }

    [Test]
    public async Task FuncWithArguments_TheCallerSuppliesTheMappedParameter()
    {
        using AppContainer root = new();

        ReportHost host = root.GetRequiredService<ReportHost>();
        Report first = host.Reports("first");
        Report second = host.Reports("second");
        Task<Report> task = host.ReportTasks("task");

        await Assert.That(first.Title).IsEqualTo("first");
        await Assert.That(second.Title).IsEqualTo("second");
        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(first.Clock).IsSameReferenceAs(root.Clock);
        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(task.Result.Title).IsEqualTo("task");
        await Assert.That(ReferenceEquals(root.GetRequiredService<Func<string, Report>>(), host.Reports)).IsTrue();
    }

    [Test]
    public async Task FuncWithArguments_TheTargetResolvedDirectly_ThrowsForTheMappedParameter()
    {
        using AppContainer root = new();

        await Assert.That(() => root.GetRequiredService<Report>()).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task KeyValuePair_PairsTheKeyOfTheSiteWithTheService()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        PairHost host = scope.GetRequiredService<PairHost>();
        IStore a = scope.GetRequiredKeyedService<IStore>("a");

        await Assert.That(host.Store.Key).IsEqualTo("a");
        await Assert.That(host.Store.Value).IsSameReferenceAs(a);
        await Assert.That(host.Lazy.Key).IsEqualTo("a");
        await Assert.That(host.Lazy.Value).IsSameReferenceAs(scope.GetRequiredKeyedService<Lazy<IStore>>("a"));
        await Assert.That(host.Lazy.Value.Value).IsSameReferenceAs(a);
        await Assert.That(host.Disk.Key).IsEqualTo(StoreKind.Disk);
        await Assert.That(host.Disk.Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>(StoreKind.Disk));
    }

    [Test]
    public async Task KeyValuePair_LookupByTypeAndKey_CreatesThePair()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        KeyValuePair<string, IStore> pair = scope.GetRequiredKeyedService<KeyValuePair<string, IStore>>("a");

        await Assert.That(pair.Key).IsEqualTo("a");
        await Assert.That(pair.Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>("a"));
        await Assert.That(scope.GetKeyedService(typeof(KeyValuePair<string, IStore>), "missing")).IsNull();
    }

    [Test]
    public async Task KeyedCollection_ContainsTheRegistrationsOfTheKeyTypeEachWithItsKey()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        KeyedCollectionHost host = scope.GetRequiredService<KeyedCollectionHost>();
        string[] keys = [.. host.Strings.Select(static p => p.Key).Order(StringComparer.Ordinal)];

        await Assert.That(string.Join(",", keys)).IsEqualTo("a,primary");
        await Assert.That(host.Strings.Single(static p => p.Key == "a").Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>("a"));
        await Assert.That(host.Records.Count).IsEqualTo(1);
        await Assert.That(host.Records[new StoreKey("backup")]).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>(StoreKeys.Backup));
        await Assert.That(host.All.Count).IsEqualTo(5);
        await Assert.That(host.All[StoreKind.Disk].Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>(StoreKind.Disk));
        await Assert.That(host.All[1L].Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>(1L));
        await Assert.That(host.OnlyA.Length).IsEqualTo(1);
        await Assert.That(host.OnlyA[0].Value).IsSameReferenceAs(scope.GetRequiredKeyedService<IStore>("a"));
    }

    [Test]
    public async Task KeyedCollection_CachedByTheLifetimeOfTheElementsAndFoundByType()
    {
        using AppContainer root = new();
        using AppContainer first = root.CreateScope();
        using AppContainer second = root.CreateScope();

        KeyedCollectionHost one = first.GetRequiredService<KeyedCollectionHost>();
        KeyedCollectionHost two = second.GetRequiredService<KeyedCollectionHost>();

        // Only Singleton elements: the root caches one. A Scoped element: each scope caches one
        await Assert.That(two.Records).IsSameReferenceAs(one.Records);
        await Assert.That(two.All).IsNotSameReferenceAs(one.All);
        await Assert.That(first.GetRequiredService<IReadOnlyDictionary<StoreKey, IStore>>()).IsSameReferenceAs(one.Records);
        await Assert.That(first.GetRequiredService<IDictionary<object, Lazy<IStore>>>()).IsSameReferenceAs(one.All);
        await Assert.That(first.GetRequiredKeyedService<KeyValuePair<string, IStore>[]>("a")).IsSameReferenceAs(one.OnlyA);
    }

    [Test]
    public async Task Wrapper_ScopedServiceFromTheRoot_ThrowsWhenUsed()
    {
        using AppContainer root = new();

        Lazy<Session> session = root.GetRequiredService<Lazy<Session>>();

        await Assert.That(() => session.Value).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Lazy_RequiredServiceThatTheFallbackProviderDoesNotHave_ThrowsWhenUsed()
    {
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using FallbackContainer root = new(provider);

        Lazy<IExternal> external = root.LazyExternal;

        await Assert.That(root.GetService(typeof(Lazy<IExternal>))).IsNotNull();
        await Assert.That(() => external.Value).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task WrapperCollection_EachCandidateWrappedWithoutTheServicesOfTheFallbackProvider()
    {
        ServiceCollection services = new();
        services.AddSingleton<IPlugin>(new CollectionTests.ExternalPlugin());
        using ServiceProvider provider = services.BuildServiceProvider();
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();

        PluginWrapperHost host = scope.GetRequiredService<PluginWrapperHost>();
        Lazy<IPlugin>[] lazy = [.. host.Lazy];

        await Assert.That(lazy.Length).IsEqualTo(2);
        await Assert.That(lazy[0].IsValueCreated).IsFalse();
        await Assert.That(lazy[0].Value).IsSameReferenceAs(scope.GetRequiredService<FirstPlugin>());
        await Assert.That(lazy[1].Value).IsSameReferenceAs(scope.GetRequiredService<SecondPlugin>());
        await Assert.That(host.Funcs[1]()).IsSameReferenceAs(scope.GetRequiredService<SecondPlugin>());
        IPlugin first = host.Tasks[0].Result;
        await Assert.That(first).IsSameReferenceAs(scope.GetRequiredService<FirstPlugin>());

        // The element is a demand for Lazy<IPlugin> too: two candidates are an ambiguity, as for IPlugin
        await Assert.That(() => scope.GetService(typeof(Lazy<IPlugin>))).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Lazy_DeferredEdge_TwoSingletonsThatDependOnEachOther()
    {
        using AppContainer root = new();

        LazyCycleFirst first = root.GetRequiredService<LazyCycleFirst>();

        await Assert.That(first.Second.Value.First).IsSameReferenceAs(first);
    }

    [Test]
    public async Task Lazy_OptionalServiceOnlyInTheFallbackProvider_ResolvedFromTheFallbackOnFirstUse()
    {
        FallbackTests.External external = new();
        ServiceCollection services = new();
        services.AddSingleton<IExternal>(external);
        using ServiceProvider provider = services.BuildServiceProvider();
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();
        using AppContainer withoutFallback = new();
        using AppContainer withoutFallbackScope = withoutFallback.CreateScope();

        await Assert.That(scope.GetRequiredService<WrapperHost>().External!.Value).IsSameReferenceAs(external);
        await Assert.That(withoutFallbackScope.GetRequiredService<WrapperHost>().External).IsNull();
    }
}
