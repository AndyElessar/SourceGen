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

/// <summary>A keyed service: one implementation type registered with several keys.</summary>
public interface IStore;

/// <summary>A key that is equal to another instance with the same name at run time (a symbol key).</summary>
public sealed record StoreKey(string Name);

public enum StoreKind
{
    Memory,
    Disk,
}

public static class StoreKeys
{
    public const string Primary = "primary";

    public static readonly StoreKey Backup = new("backup");
}

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = 1L)]
[IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IStore)], Key = StoreKind.Disk)]
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(StoreKeys.Primary))]
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(StoreKeys.Backup))]
public sealed class Store([ServiceKey] object key) : IStore
{
    public object Key => key;
}

/// <summary>Keys at dependency sites: a specified key, InheritKey, and no key.</summary>
[IocRegister(ServiceLifetime.Transient, Key = "a")]
public sealed class StoreConsumer(
    [IocInject(Key = "a")] IStore a,
    [FromKeyedServices(1L)] IStore one,
    [IocInject(KeyType = KeyType.Csharp, Key = nameof(StoreKeys.Backup))] IStore backup,
    [FromKeyedServices] IStore inherited,
    [ServiceKey] string key)
{
    public IStore A => a;

    public IStore One => one;

    public IStore Backup => backup;

    public IStore Inherited => inherited;

    public string Key => key;
}

/// <summary>A service that only the fallback provider supplies.</summary>
public interface IExternal
{
    bool IsDisposed { get; }
}

/// <summary>
/// A Scoped service with an optional dependency that only the fallback provider supplies. It records whether that
/// dependency was already disposed when the container disposed this service.
/// </summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class ExternalConsumer(IExternal? external) : IDisposable
{
    public IExternal? External => external;

    public bool? ExternalDisposedBefore { get; private set; }

    public void Dispose() => ExternalDisposedBefore = external?.IsDisposed;
}

/// <summary>
/// A Singleton with an optional dependency that only the fallback provider supplies. Eager resolution creates it before
/// <see cref="FragileSingleton"/> (ordering), so a failed constructor of the root has already used the fallback provider.
/// </summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class ExternalHolder(IExternal? external) : IDisposable
{
    public IExternal? External => external;

    public bool? ExternalDisposedBefore { get; private set; }

    public void Dispose() => ExternalDisposedBefore = external?.IsDisposed;
}

/// <summary>
/// The default <c>IntegrateServiceProvider = true</c>: a missing dependency goes to the fallback provider (SGIOC501 is an
/// Info and does not stop the build).
/// </summary>
[IocContainer]
public partial class FallbackContainer
{
    public partial IExternal External { get; }

    public partial IExternal? OptionalExternal { get; }

    public partial IClock Clock { get; }
}

/// <summary>The default options: <c>ThreadSafeStrategy.Lock</c> and eager Singletons.</summary>
[IocContainer(IntegrateServiceProvider = false)]
public partial class AppContainer
{
    public partial IClock Clock { get; }

    public partial Session GetSession();

    public partial IServiceProvider Provider { get; }

    public partial IMissing? Missing { get; }

    [IocInject(KeyType = KeyType.Csharp, Key = nameof(StoreKeys.Primary))]
    public partial IStore PrimaryStore { get; }
}

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.None)]
public partial class LazyLockContainer;

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.None, ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange)]
public partial class CompareExchangeContainer;

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.None, ThreadSafeStrategy = ThreadSafeStrategy.None)]
public partial class NoLockContainer;

[IocContainer(IntegrateServiceProvider = false, EagerResolveOptions = EagerResolveOptions.SingletonAndScoped)]
public partial class EagerContainer;
