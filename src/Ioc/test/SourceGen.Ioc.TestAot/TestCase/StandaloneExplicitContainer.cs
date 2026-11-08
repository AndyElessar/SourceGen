namespace SourceGen.Ioc.TestAot.TestCase;

public interface IStandaloneFirst;

public interface IStandaloneSecond;

public interface IStandaloneThird;

internal sealed class StandaloneService : IStandaloneFirst, IStandaloneSecond, IStandaloneThird;

/// <summary>
/// Standalone ExplicitOnly container in an assembly whose other containers depend on a fallback provider.
/// SGIOC018 only checks this container's own registrations, so their unresolved dependencies are not reported here.
/// </summary>
[IocRegisterFor<StandaloneService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IStandaloneFirst), typeof(IStandaloneSecond)],
    RegisterAllInterfaces = true)]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class StandaloneExplicitContainer;
