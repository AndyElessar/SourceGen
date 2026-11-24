using System.Collections.Concurrent;
using SourceGen.Ioc;

// All registrations of an assembly are in the view of every container of that assembly, so these services work in
// each container below. A container with ExplicitOnly comes in a later slice.
[assembly: IocServiceCollectionExtensions(Name = "TestAot")]

namespace SourceGen.Ioc.TestAot.Container;

/// <summary>Records the creations and the disposals of one root container. It is a Singleton, so each root has its own.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class Journal
{
    /// <summary>The journal that the last constructor on this thread created, for a container whose constructor fails.</summary>
    [ThreadStatic]
    public static Journal? LastCreated;

    /// <summary>The names whose disposal throws, for the next journal on this thread.</summary>
    [ThreadStatic]
    public static string[]? NextFailOnDispose;

    private readonly ConcurrentQueue<string> disposed = new();
    private int slowCreated;
    private int scopedProbes;

    public Journal()
    {
        FailOnDispose = NextFailOnDispose ?? [];
        LastCreated = this;
    }

    /// <summary>Gets the names whose disposal throws.</summary>
    public string[] FailOnDispose { get; set; }

    /// <summary>Gets the disposed instances, in the order of disposal.</summary>
    public IReadOnlyList<string> Disposed => [.. disposed];

    public int SlowCreated => Volatile.Read(ref slowCreated);

    public int ScopedProbes => Volatile.Read(ref scopedProbes);

    public int NextSlow() => Interlocked.Increment(ref slowCreated);

    public void OnScopedProbe() => Interlocked.Increment(ref scopedProbes);

    public void OnDisposed(string name)
    {
        disposed.Enqueue(name);
        if(Array.IndexOf(FailOnDispose, name) >= 0)
        {
            throw new InvalidOperationException($"Disposal of {name} failed.");
        }
    }
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
public sealed class SystemClock(Journal journal) : IClock, IDisposable
{
    public DateTimeOffset Now => DateTimeOffset.UnixEpoch;

    public void Dispose() => journal.OnDisposed(nameof(SystemClock));
}

[IocRegister(ServiceLifetime.Singleton)]
public sealed class Repository(Journal journal, IClock clock) : IDisposable
{
    public IClock Clock => clock;

    public void Dispose() => journal.OnDisposed(nameof(Repository));
}

[IocRegister(ServiceLifetime.Scoped)]
public sealed class Session(Journal journal, Repository repository) : IDisposable
{
    public Repository Repository => repository;

    public void Dispose() => journal.OnDisposed(nameof(Session));
}

[IocRegister(ServiceLifetime.Scoped)]
public sealed class UnitOfWork(Session session, Journal journal) : IDisposable
{
    public Session Session => session;

    public void Dispose() => journal.OnDisposed(nameof(UnitOfWork));
}

/// <summary>A Transient that needs a Scoped service: resolving it from the root throws.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class SessionReader(Session session)
{
    public Session Session => session;
}

[IocRegister(ServiceLifetime.Transient)]
public sealed class Formatter(IClock clock)
{
    public string Format() => clock.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A Scoped service that implements only <see cref="IAsyncDisposable"/>.</summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class AsyncResource(Journal journal) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        journal.OnDisposed(nameof(AsyncResource));
        return default;
    }
}

/// <summary>A Scoped service that counts its creations, for eager resolution of Scoped services.</summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class ScopedProbe
{
    public ScopedProbe(Journal journal) => journal.OnScopedProbe();
}

/// <summary>A Singleton that takes some time to create, for concurrent first resolution.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class SlowSingleton : IDisposable
{
    private readonly Journal journal;

    public SlowSingleton(Journal journal)
    {
        this.journal = journal;
        Id = journal.NextSlow();
        Thread.Sleep(5);
    }

    public int Id { get; }

    public void Dispose() => journal.OnDisposed($"{nameof(SlowSingleton)}#{Id}");
}

/// <summary>A Singleton whose constructor throws when <see cref="Fail"/> is set on the thread.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class FragileSingleton
{
    [ThreadStatic]
    public static bool Fail;

    public FragileSingleton(Repository repository)
    {
        Repository = repository;
        if(Fail)
        {
            throw new InvalidOperationException("FragileSingleton failed.");
        }
    }

    public Repository Repository { get; }
}

/// <summary>A Scoped service whose constructor throws when <see cref="Fail"/> is set on the thread.</summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class FragileScoped
{
    [ThreadStatic]
    public static bool Fail;

    public FragileScoped(UnitOfWork unitOfWork)
    {
        UnitOfWork = unitOfWork;
        if(Fail)
        {
            throw new InvalidOperationException("FragileScoped failed.");
        }
    }

    public UnitOfWork UnitOfWork { get; }
}

/// <summary>A service that no registration of this assembly provides.</summary>
public interface IMissing;

public enum Level
{
    Low,
    High,
}

/// <summary>Optional dependencies: one that is registered, and others that get <see langword="null"/> or their default value.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class OptionalConsumer(IClock? clock, IMissing? missing, string label = "none", int retries = 3, Level level = Level.High, int? limit = null)
{
    public IClock? Clock => clock;

    public IMissing? Missing => missing;

    public string Label => label;

    public int Retries => retries;

    public Level Level => level;

    public int? Limit => limit;
}

/// <summary>The default options: <c>ThreadSafeStrategy.Lock</c> and eager Singletons.</summary>
[IocContainer(IntegrateServiceProvider = false)]
public partial class AppContainer
{
    public partial IClock Clock { get; }

    public partial Session GetSession();

    public partial IServiceProvider Provider { get; }

    public partial IMissing? Missing { get; }
}

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.None)]
public partial class LazyLockContainer;

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.None, ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange)]
public partial class CompareExchangeContainer;

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.None, ThreadSafeStrategy = ThreadSafeStrategy.None)]
public partial class NoLockContainer;

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.SingletonAndScoped)]
public partial class EagerContainer;
