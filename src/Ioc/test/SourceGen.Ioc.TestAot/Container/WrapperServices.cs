namespace SourceGen.Ioc.TestAot.Container;

/// <summary>A Transient service, for <c>Func&lt;T&gt;</c> and <c>Task&lt;T&gt;</c>.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class Job;

/// <summary>
/// <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> and <c>Task&lt;T&gt;</c> of a Singleton, a Scoped and a Transient service.
/// <see cref="IExternal"/> exists only in the fallback provider, so the site is optional for the containers without one.
/// </summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class WrapperHost(
    Lazy<IClock> clock,
    Lazy<Session> session,
    Func<Session> sessions,
    Func<Job> jobs,
    Task<IClock> clockTask,
    Func<Task<Job>> jobTasks,
    [IocInject(Key = "a")] Lazy<IStore> store,
    Lazy<IExternal>? external = null)
{
    public Lazy<IStore> Store => store;

    public Lazy<IClock> Clock => clock;

    public Lazy<Session> Session => session;

    public Func<Session> Sessions => sessions;

    public Func<Job> Jobs => jobs;

    public Task<IClock> ClockTask => clockTask;

    public Func<Task<Job>> JobTasks => jobTasks;

    public Lazy<IExternal>? External => external;
}

/// <summary>A Singleton whose constructor throws one time on this thread when the test asks for it.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class FlakyService
{
    [ThreadStatic]
    public static bool FailNextCreation;

    public FlakyService()
    {
        if(FailNextCreation)
        {
            FailNextCreation = false;
            throw new InvalidOperationException("The first creation fails.");
        }
    }
}

/// <summary>Holds the cached <c>Lazy&lt;T&gt;</c> of a Singleton.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class FlakyHost(Lazy<FlakyService> flaky)
{
    public Lazy<FlakyService> Flaky => flaky;
}

/// <summary>Two Singletons that depend on each other through <c>Lazy&lt;T&gt;</c>: a deferred edge is not a cycle.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class LazyCycleFirst(Lazy<LazyCycleSecond> second)
{
    public Lazy<LazyCycleSecond> Second => second;
}

[IocRegister(ServiceLifetime.Singleton)]
public sealed class LazyCycleSecond(LazyCycleFirst first)
{
    public LazyCycleFirst First => first;
}
