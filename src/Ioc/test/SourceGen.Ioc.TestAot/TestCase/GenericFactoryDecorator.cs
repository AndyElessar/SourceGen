namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>Service type closed per request type by a generic factory.</summary>
public interface IGenericFactoryHandler<TResponse>
{
    string Describe();
}

/// <summary>Implementation type; its own registration is constructed, not created by the factory.</summary>
[IocRegister]
public sealed class GenericFactoryHandler<T> : IGenericFactoryHandler<Task<T>>
{
    public string Describe() => nameof(GenericFactoryHandler<T>);
}

/// <summary>Instance the generic factory creates for the service type.</summary>
public sealed class FactoryCreatedHandler<T> : IGenericFactoryHandler<Task<T>>
{
    public string Describe() => nameof(FactoryCreatedHandler<T>);
}

public sealed class LoggingGenericFactoryHandler<T>(IGenericFactoryHandler<T> inner) : IGenericFactoryHandler<T>
{
    public IGenericFactoryHandler<T> Inner => inner;

    public string Describe() => $"{nameof(LoggingGenericFactoryHandler<T>)}({inner.Describe()})";
}

public sealed class GenericFactoryEntity;

[IocRegisterDefaults(typeof(IGenericFactoryHandler<>),
    ServiceLifetime.Singleton,
    Factory = nameof(Create),
    Decorators = [typeof(LoggingGenericFactoryHandler<>)])]
public static class GenericHandlerFactory
{
    [IocGenericFactory(typeof(IGenericFactoryHandler<Task<int>>), typeof(int))]
    public static IGenericFactoryHandler<Task<T>> Create<T>() => new FactoryCreatedHandler<T>();
}

/// <summary>Consumer of the decorated service type, its Lazy wrapper, and the implementation type.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class GenericFactoryHandlerConsumer(
    IGenericFactoryHandler<Task<GenericFactoryEntity>> handler,
    Lazy<IGenericFactoryHandler<Task<GenericFactoryEntity>>> lazy,
    GenericFactoryHandler<GenericFactoryEntity> implementation)
{
    public IGenericFactoryHandler<Task<GenericFactoryEntity>> Handler => handler;
    public Lazy<IGenericFactoryHandler<Task<GenericFactoryEntity>>> Lazy => lazy;
    public GenericFactoryHandler<GenericFactoryEntity> Implementation => implementation;
}

[IocContainer]
public sealed partial class GenericFactoryContainer;
