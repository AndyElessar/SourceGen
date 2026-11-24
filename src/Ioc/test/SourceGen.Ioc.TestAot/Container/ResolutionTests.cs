namespace SourceGen.Ioc.TestAot.Container;

/// <summary>Resolution by lifetime, the built-in services, and the partial accessors (Container.Output.spec).</summary>
public class ResolutionTests
{
    [Test]
    public async Task Singleton_ResolvedFromRootAndScope_ReturnsSameInstance()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        Repository fromRoot = root.GetRequiredService<Repository>();
        Repository fromScope = scope.GetRequiredService<Repository>();

        await Assert.That(fromScope).IsSameReferenceAs(fromRoot);
        await Assert.That(fromRoot.Clock).IsSameReferenceAs(root.GetRequiredService<IClock>());
    }

    [Test]
    public async Task Singleton_ResolvedByEachServiceType_ReturnsSameInstance()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        SystemClock clock = scope.GetRequiredService<SystemClock>();

        await Assert.That(root.GetRequiredService<IClock>()).IsSameReferenceAs(clock);
        await Assert.That(scope.GetRequiredService<IClock>()).IsSameReferenceAs(clock);
        await Assert.That(clock.Now).IsEqualTo(DateTimeOffset.UnixEpoch);
    }

    [Test]
    public async Task Scoped_ResolvedFromTwoScopes_ReturnsOneInstanceForEachScope()
    {
        using AppContainer root = new();
        using AppContainer first = root.CreateScope();
        using AppContainer second = root.CreateScope();

        UnitOfWork unitOfWork = first.GetRequiredService<UnitOfWork>();

        await Assert.That(first.GetRequiredService<Session>()).IsSameReferenceAs(unitOfWork.Session);
        await Assert.That(second.GetRequiredService<Session>()).IsNotSameReferenceAs(unitOfWork.Session);
        await Assert.That(unitOfWork.Session.Repository).IsSameReferenceAs(second.GetRequiredService<Session>().Repository);
    }

    [Test]
    public async Task Transient_ResolvedTwice_ReturnsNewInstances()
    {
        using AppContainer root = new();

        Formatter first = root.GetRequiredService<Formatter>();
        Formatter second = root.GetRequiredService<Formatter>();

        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(first.Format()).IsEqualTo("1970-01-01T00:00:00.0000000+00:00");
    }

    [Test]
    public async Task Scoped_ResolvedFromTheRoot_ThrowsInvalidOperationException()
    {
        using AppContainer root = new();

        InvalidOperationException? direct = await Assert.That(() => root.GetService<Session>()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => root.GetService<SessionReader>()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => root.GetServices<Session>().Count()).ThrowsExactly<InvalidOperationException>();
        await Assert.That(() => root.GetSession()).ThrowsExactly<InvalidOperationException>();

        await Assert.That(direct!.Message).IsEqualTo("Cannot resolve scoped service 'SourceGen.Ioc.TestAot.Container.Session' from the root container.");
    }

    [Test]
    public async Task Transient_ThatNeedsAScopedServiceOnAScope_GetsTheScopedInstance()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        SessionReader reader = scope.GetRequiredService<SessionReader>();

        await Assert.That(reader.Session).IsSameReferenceAs(scope.GetRequiredService<Session>());
    }

    [Test]
    public async Task Collection_OneRegistration_ContainsTheSameInstance()
    {
        using AppContainer root = new();

        IClock[] clocks = [.. root.GetServices<IClock>()];

        await Assert.That(clocks.Length).IsEqualTo(1);
        await Assert.That(clocks[0]).IsSameReferenceAs(root.GetRequiredService<IClock>());
    }

    [Test]
    public async Task BuiltInServices_ResolvedOnAScope_AreTheScopeAndTheRoot()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        await Assert.That(scope.GetRequiredService<IServiceProvider>()).IsSameReferenceAs(scope);
        await Assert.That(scope.GetRequiredService<AppContainer>()).IsSameReferenceAs(scope);
        await Assert.That(scope.GetRequiredService<IServiceScopeFactory>()).IsSameReferenceAs(root);
        await Assert.That(root.GetRequiredService<IServiceProviderIsService>().IsService(typeof(Formatter))).IsTrue();
        await Assert.That(root.IsService(typeof(string))).IsFalse();
        await Assert.That(root.GetService(typeof(string))).IsNull();
    }

    [Test]
    public async Task PartialAccessors_OnAScope_UseTheResolvers()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        await Assert.That(scope.Clock).IsSameReferenceAs(root.GetRequiredService<IClock>());
        await Assert.That(scope.GetSession()).IsSameReferenceAs(scope.GetRequiredService<Session>());
        await Assert.That(scope.Provider).IsSameReferenceAs(scope);
    }

    [Test]
    public async Task CreateScope_OnAScopeOrTheFactory_CreatesAScopeOfTheSameRoot()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        using AppContainer nested = scope.CreateScope();
        using IServiceScope fromFactory = root.GetRequiredService<IServiceScopeFactory>().CreateScope();
        Session session = fromFactory.ServiceProvider.GetRequiredService<Session>();

        await Assert.That(nested.GetRequiredService<IServiceScopeFactory>()).IsSameReferenceAs(root);
        await Assert.That(nested.GetRequiredService<Session>()).IsNotSameReferenceAs(scope.GetRequiredService<Session>());
        await Assert.That(session.Repository).IsSameReferenceAs(root.GetRequiredService<Repository>());
    }

    [Test]
    public async Task OptionalDependency_Missing_GetsNullOrTheDefaultValue()
    {
        using AppContainer root = new();

        OptionalConsumer consumer = root.GetRequiredService<OptionalConsumer>();

        await Assert.That(consumer.Clock).IsSameReferenceAs(root.Clock);
        await Assert.That(consumer.Missing).IsNull();
        await Assert.That(consumer.Label).IsEqualTo("none");
        await Assert.That(consumer.Retries).IsEqualTo(3);
        await Assert.That(consumer.Level).IsEqualTo(Level.High);
        await Assert.That(consumer.Limit).IsNull();
        await Assert.That(root.Missing).IsNull();
    }
}
