namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>Service type created by a generic factory whose parameter uses the factory's type parameter.</summary>
public interface IFactoryParameterHandler<TResponse>
{
    IFactoryParameterRepository Repository { get; }
}

public interface IFactoryParameterRepository;

public interface IFactoryParameterRepository<T> : IFactoryParameterRepository;

/// <summary>Open generic dependency of the factory, closed for the factory's type argument.</summary>
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryParameterRepository<>)])]
public sealed class FactoryParameterRepository<T> : IFactoryParameterRepository<T>;

public sealed class FactoryParameterHandler<T>(IFactoryParameterRepository<T> repository) : IFactoryParameterHandler<Task<T>>
{
    public IFactoryParameterRepository Repository => repository;
}

[IocRegisterDefaults(typeof(IFactoryParameterHandler<>),
    ServiceLifetime.Singleton,
    Factory = nameof(Create))]
public static class FactoryParameterHandlerFactory
{
    [IocGenericFactory(typeof(IFactoryParameterHandler<Task<int>>), typeof(int))]
    public static IFactoryParameterHandler<Task<T>> Create<T>(IFactoryParameterRepository<T> repository) => new FactoryParameterHandler<T>(repository);
}

/// <summary>Consumer of the factory-created service type; only the factory depends on the repository.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class FactoryParameterConsumer(IFactoryParameterHandler<Task<GenericFactoryEntity>> handler)
{
    public IFactoryParameterHandler<Task<GenericFactoryEntity>> Handler => handler;
}

[IocContainer]
public sealed partial class FactoryParameterContainer;

public sealed class FactoryParameterClosedEntity;

/// <summary>
/// Closed registration that takes the generic factory of its default settings: its service type is created by the closed
/// factory, while the implementation type itself is constructed.
/// </summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class FactoryParameterClosedHandler : IFactoryParameterHandler<Task<FactoryParameterClosedEntity>>
{
    public IFactoryParameterRepository Repository => throw new NotSupportedException();
}
