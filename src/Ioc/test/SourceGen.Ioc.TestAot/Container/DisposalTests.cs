namespace SourceGen.Ioc.TestAot.Container;

/// <summary>
/// Disposal, eager resolution, failure during creation, and the use after disposal (Container.Output.spec,
/// "Disposal", "Eager resolution", "After disposal, and concurrency"). The tests do not run in parallel, because some
/// of them set the global dispose-exception callback.
/// </summary>
[NotInParallel(nameof(IocContainerGlobalOptions))]
public class DisposalTests
{
    /// <summary>Sets the callback, runs <paramref name="action"/>, and restores the callback.</summary>
    /// <returns>The exceptions that the callback received.</returns>
    private static List<Exception> WithCallback(Action action)
    {
        List<Exception> reported = [];
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = e =>
        {
            lock(reported)
            {
                reported.Add(e);
            }
        };
        try
        {
            action();
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }

        return reported;
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch(Exception e)
        {
            return e;
        }
    }

    [Test]
    public async Task Scope_Disposed_DisposesItsInstancesInReverseOrderOneTime()
    {
        using AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        AppContainer scope = root.CreateScope();
        UnitOfWork unitOfWork = scope.GetRequiredService<UnitOfWork>();

        scope.Dispose();
        scope.Dispose();

        await Assert.That(unitOfWork.Session).IsNotNull();
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("UnitOfWork,Session");
    }

    [Test]
    public async Task Root_Disposed_DisposesSingletonsInReverseOrderAndNotTheScopedOfALiveScope()
    {
        AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        using AppContainer scope = root.CreateScope();
        Session session = scope.GetRequiredService<Session>();

        root.Dispose();

        await Assert.That(session.Repository).IsNotNull();
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("SlowSingleton#1,Repository,SystemClock");
    }

    [Test]
    public async Task Scope_DisposedAlone_ThrowsObjectDisposedExceptionAndTheRootWorks()
    {
        using AppContainer root = new();
        AppContainer scope = root.CreateScope();

        scope.Dispose();

        await Assert.That(() => scope.GetService<Formatter>()).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(root.GetRequiredService<Formatter>().Format()).IsNotEmpty();
    }

    [Test]
    public async Task Dispose_OneFails_DisposesTheOthersAndThrowsTheSameExceptionWithoutTheCallback()
    {
        using AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        journal.FailOnDispose = ["UnitOfWork"];
        AppContainer scope = root.CreateScope();
        UnitOfWork unitOfWork = scope.GetRequiredService<UnitOfWork>();
        Exception? thrown = null;

        List<Exception> reported = WithCallback(() => thrown = Catch(scope.Dispose));

        await Assert.That(unitOfWork.Session).IsNotNull();
        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("Disposal of UnitOfWork failed.");
        await Assert.That(journal.Disposed).Contains("Session");
        await Assert.That(reported).IsEmpty();
    }

    [Test]
    public async Task Dispose_TwoFail_ThrowsAggregateException()
    {
        using AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        journal.FailOnDispose = ["UnitOfWork", "Session"];
        AppContainer scope = root.CreateScope();
        UnitOfWork unitOfWork = scope.GetRequiredService<UnitOfWork>();

        AggregateException? thrown = await Assert.That(scope.Dispose).Throws<AggregateException>();

        await Assert.That(unitOfWork.Session).IsNotNull();
        await Assert.That(thrown!.InnerExceptions.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Dispose_AsyncOnlyInstance_ThrowsAndContinuesAndDisposeAsyncWorks()
    {
        using AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        AppContainer first = root.CreateScope();
        AsyncResource firstResource = first.GetRequiredService<AsyncResource>();
        Session session = first.GetRequiredService<Session>();
        AppContainer second = root.CreateScope();
        AsyncResource secondResource = second.GetRequiredService<AsyncResource>();

        InvalidOperationException? thrown = await Assert.That(first.Dispose).ThrowsExactly<InvalidOperationException>();
        await second.DisposeAsync();

        await Assert.That(secondResource).IsNotSameReferenceAs(firstResource);
        await Assert.That(session.Repository).IsNotNull();
        await Assert.That(thrown!.Message).Contains("only implements IAsyncDisposable");
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("Session,AsyncResource");
    }

    [Test]
    public async Task RootDisposeAsync_TwoFail_DisposesAllInReverseOrderAndThrowsAggregateException()
    {
        AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        _ = root.GetRequiredService<Repository>();
        journal.FailOnDispose = ["Repository", "SystemClock"];

        AggregateException? thrown = await Assert.That(async () => await root.DisposeAsync()).ThrowsExactly<AggregateException>();

        await Assert.That(string.Join(",", thrown!.InnerExceptions.Select(static e => e.Message))).IsEqualTo("Disposal of Repository failed.,Disposal of SystemClock failed.");
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("SlowSingleton#1,Repository,SystemClock");
    }

    [Test]
    public async Task PublicMethods_AfterTheRootIsDisposed_ThrowObjectDisposedException()
    {
        AppContainer root = new();
        AppContainer scope = root.CreateScope();

        root.Dispose();

        await Assert.That(() => root.GetService<IClock>()).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(() => root.CreateScope()).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(() => root.Clock).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(() => scope.GetService<Formatter>()).ThrowsExactly<ObjectDisposedException>();
        await Assert.That(() => scope.GetSession()).ThrowsExactly<ObjectDisposedException>();
        scope.Dispose();
    }

    [Test]
    public async Task EagerResolve_DefaultAndNone_CreatesOnlyTheSingletonsOfTheDefault()
    {
        using AppContainer eager = new();
        using LazyLockContainer lazy = new();
        using AppContainer scope = eager.CreateScope();

        Journal eagerJournal = eager.GetRequiredService<Journal>();

        await Assert.That(eagerJournal.SlowCreated).IsEqualTo(1);
        await Assert.That(eagerJournal.ScopedProbes).IsEqualTo(0);
        await Assert.That(lazy.GetRequiredService<Journal>().SlowCreated).IsEqualTo(0);
        await Assert.That(scope.GetRequiredService<SlowSingleton>().Id).IsEqualTo(1);
    }

    [Test]
    public async Task EagerScoped_CreateScope_CreatesTheScopedServices()
    {
        await using EagerContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();

        await using EagerContainer scope = root.CreateScope();

        await Assert.That(journal.ScopedProbes).IsEqualTo(1);
        await Assert.That(scope.GetRequiredService<ScopedProbe>()).IsSameReferenceAs(scope.GetRequiredService<ScopedProbe>());
        await Assert.That(journal.ScopedProbes).IsEqualTo(1);
    }

    [Test]
    public async Task EagerSingleton_ConstructorFails_DisposesWhatIsCreatedInReverseOrderAndThrows()
    {
        // The flags are thread-static, and a test can continue on another thread after an await: set, use, and reset
        // them without an await in between
        Exception? thrown = null;
        Journal? journal = null;
        List<Exception> reported = WithCallback(() =>
        {
            FragileSingleton.Fail = true;
            Journal.NextFailOnDispose = ["SystemClock"];
            try
            {
                thrown = Catch(() => _ = new AppContainer());
            }
            finally
            {
                journal = Journal.LastCreated;
                FragileSingleton.Fail = false;
                Journal.NextFailOnDispose = null;
            }
        });

        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("FragileSingleton failed.");
        await Assert.That(string.Join(",", journal!.Disposed)).IsEqualTo("Repository,SystemClock");
        await Assert.That(string.Join(",", reported.Select(static e => e.Message))).IsEqualTo("Disposal of SystemClock failed.");
    }

    [Test]
    public async Task EagerScoped_ConstructorFails_DisposesWhatIsCreatedInReverseOrderAndThrows()
    {
        await using EagerContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        Exception? thrown;
        FragileScoped.Fail = true;
        try
        {
            thrown = Catch(() => _ = root.CreateScope());
        }
        finally
        {
            FragileScoped.Fail = false;
        }

        // AsyncResource implements only IAsyncDisposable: its DisposeAsync starts without a wait
        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("UnitOfWork,Session,AsyncResource");
    }

    [Test]
    public async Task Singleton_ConstructorFailsOnce_ResolvesOnTheNextRequest()
    {
        using LazyLockContainer root = new();
        Exception? thrown;
        FragileSingleton.Fail = true;
        try
        {
            thrown = Catch(() => _ = root.GetRequiredService<FragileSingleton>());
        }
        finally
        {
            FragileSingleton.Fail = false;
        }

        FragileSingleton fragile = root.GetRequiredService<FragileSingleton>();

        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(fragile.Repository).IsSameReferenceAs(root.GetRequiredService<Repository>());
        await Assert.That(root.GetRequiredService<FragileSingleton>()).IsSameReferenceAs(fragile);
    }
}
