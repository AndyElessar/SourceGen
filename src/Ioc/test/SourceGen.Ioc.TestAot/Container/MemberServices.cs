[assembly: SourceGen.Ioc.IocRegisterFor(typeof(SourceGen.Ioc.TestAot.Container.NotOwned), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient)]
[assembly: SourceGen.Ioc.IocInjectMember(typeof(SourceGen.Ioc.TestAot.Container.NotOwned), nameof(SourceGen.Ioc.TestAot.Container.NotOwned.Initialize))]
[assembly: SourceGen.Ioc.IocInjectMember(typeof(SourceGen.Ioc.TestAot.Container.NotOwned), nameof(SourceGen.Ioc.TestAot.Container.NotOwned.Clock))]

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
    public IMissing? Missing { get; set; } = null;

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

/// <summary>
/// A type without SourceGen.Ioc attributes, as a type that the user does not own: its members are injected with
/// <c>IocInjectMemberAttribute</c> (Annotations.spec, "Types that the user does not own").
/// </summary>
public sealed class NotOwned
{
    /// <summary>Gets the injection steps, in the order in which they ran.</summary>
    public List<string> Steps { get; } = [];

    public IClock Clock { get; set; } = null!;

    public void Initialize(Session session) => Steps.Add($"{nameof(Initialize)}:{Clock is not null}:{session is not null}");
}