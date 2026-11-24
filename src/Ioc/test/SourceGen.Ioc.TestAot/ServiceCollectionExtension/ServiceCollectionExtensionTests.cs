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
        // disposals"; Constitution, documented difference 21). The order is the reverse of creation.
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

    private sealed class Missing : IMissing;
}
