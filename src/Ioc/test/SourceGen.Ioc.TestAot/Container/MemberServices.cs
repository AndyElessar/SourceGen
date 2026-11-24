namespace SourceGen.Ioc.TestAot.Container;

/// <summary>A base class with injected members: they come before the members of the derived type.</summary>
public abstract class InjectedBase
{
    /// <summary>Gets the injection steps, in the order in which they ran.</summary>
    public List<string> Steps { get; } = [];

    [IocInject]
    public IClock BaseClock { get; set; } = null!;

    [IocInject]
    public void InitializeBase(IClock clock) => Steps.Add($"{nameof(InitializeBase)}:{ReferenceEquals(clock, BaseClock)}");
}

/// <summary>
/// Member injection (Annotations.spec, "Injected members" and "Order"). The injection method throws when
/// <see cref="FailInitialize"/> is set on the thread, for the failure during creation.
/// </summary>
[IocRegister(ServiceLifetime.Scoped)]
public sealed class InjectedScoped(Journal journal) : InjectedBase, IDisposable
{
    [ThreadStatic]
    public static bool FailInitialize;

    [IocInject]
    public Repository? Repository;

    [IocInject]
    public required Session Session { get; init; }

    [IocInject]
    public IMissing? Missing { get; set; }

    /// <summary>No service provides <see cref="string"/>, so the initializer stays.</summary>
    [IocInject]
    public string Label { get; set; } = "initial";

    [IocInject]
    public void Initialize(IClock clock, Session session)
    {
        Steps.Add($"{nameof(Initialize)}:{Repository is not null}:{ReferenceEquals(session, Session)}:{ReferenceEquals(clock, BaseClock)}");
        if(FailInitialize)
        {
            throw new InvalidOperationException("Initialize failed.");
        }
    }

    public void Dispose() => journal.OnDisposed(nameof(InjectedScoped));
}
