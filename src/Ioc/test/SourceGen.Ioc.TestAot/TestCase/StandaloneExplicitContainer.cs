namespace SourceGen.Ioc.TestAot.TestCase;

public interface IStandaloneFirst;

public interface IStandaloneSecond;

public interface IStandaloneThird;

internal sealed class StandaloneService : IStandaloneFirst, IStandaloneSecond, IStandaloneThird;

public interface IStandaloneRepository<T>;

internal sealed class StandaloneRepository<T> : IStandaloneRepository<T>;

public sealed record StandaloneUser;

public sealed record StandaloneOrder;

public sealed record StandaloneInvoice;

/// <summary>Depends on a closed form of an open generic registration of <see cref="StandaloneExplicitContainer"/>.</summary>
public sealed class StandaloneRepositoryConsumer(IStandaloneRepository<StandaloneUser> repository)
{
    public IStandaloneRepository<StandaloneUser> Repository { get; } = repository;
}

/// <summary>
/// Standalone ExplicitOnly container in an assembly whose other containers depend on a fallback provider.
/// SGIOC018 only checks this container's own registrations, so their unresolved dependencies are not reported here.
/// Its open generic registration is closed for its own dependencies, for [IocDiscover] on the container
/// (<see cref="StandaloneOrder"/>), and for GetService calls of the compilation (<see cref="StandaloneInvoice"/>).
/// </summary>
[IocRegisterFor<StandaloneService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IStandaloneFirst), typeof(IStandaloneSecond)],
    RegisterAllInterfaces = true)]
[IocRegisterFor(typeof(StandaloneRepository<>), ServiceLifetime.Singleton, ServiceTypes = [typeof(IStandaloneRepository<>)])]
[IocRegisterFor<StandaloneRepositoryConsumer>(ServiceLifetime.Singleton)]
[IocDiscover<IStandaloneRepository<StandaloneOrder>>]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class StandaloneExplicitContainer;
