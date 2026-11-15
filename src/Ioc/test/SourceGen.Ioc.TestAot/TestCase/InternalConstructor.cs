namespace SourceGen.Ioc.TestAot.TestCase;

public interface IInternalCtorDependency;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInternalCtorDependency)])]
public sealed class InternalCtorDependency : IInternalCtorDependency;

/// <summary>Closed registration whose only constructor is internal, which MS.DI cannot activate.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class InternalCtorService
{
    internal InternalCtorService(IInternalCtorDependency dependency) => Dependency = dependency;

    public IInternalCtorDependency Dependency { get; }
}

public interface IInternalCtorRepo<T>;

/// <summary>Open generic registration whose only constructor is internal, which MS.DI cannot activate.</summary>
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInternalCtorRepo<>)])]
public sealed class InternalCtorRepo<T> : IInternalCtorRepo<T>
{
    internal InternalCtorRepo() { }
}

/// <summary>Consumer that closes the open generic registration for <see cref="string"/>.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class InternalCtorRepoConsumer(IInternalCtorRepo<string> repo)
{
    public IInternalCtorRepo<string> Repo => repo;
}

[IocContainer]
public sealed partial class InternalCtorContainer;
