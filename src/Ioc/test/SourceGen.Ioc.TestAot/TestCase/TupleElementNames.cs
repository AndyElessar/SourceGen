namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>Service type whose tuple type argument is written with different element names.</summary>
public interface ITupleNamesHandler<T>;

public sealed class TupleNamesEntity;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(ITupleNamesHandler<(TupleNamesEntity X, string Y)>)])]
public sealed class TupleNamesNamedHandler : ITupleNamesHandler<(TupleNamesEntity X, string Y)>;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(ITupleNamesHandler<(TupleNamesEntity, string)>)])]
public sealed class TupleNamesUnnamedHandler : ITupleNamesHandler<(TupleNamesEntity, string)>;

/// <summary>Consumer whose dependencies name the tuple elements differently from the registrations.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class TupleNamesConsumer(
    ITupleNamesHandler<(TupleNamesEntity A, string B)> handler,
    IEnumerable<ITupleNamesHandler<(TupleNamesEntity, string)>> handlers)
{
    public ITupleNamesHandler<(TupleNamesEntity A, string B)> Handler => handler;

    public IEnumerable<ITupleNamesHandler<(TupleNamesEntity, string)>> Handlers => handlers;
}

/// <summary>Open generic service type closed for dependencies that differ only in tuple element names.</summary>
public interface ITupleNamesGenericHandler<T>;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(ITupleNamesGenericHandler<>)])]
public sealed class TupleNamesGenericHandler<T> : ITupleNamesGenericHandler<T>;

[IocRegister(ServiceLifetime.Transient)]
public sealed class TupleNamesGenericConsumer(
    Lazy<ITupleNamesGenericHandler<(TupleNamesEntity A, string B)>> lazy,
    Func<ITupleNamesGenericHandler<(TupleNamesEntity A, string B)>> func)
{
    public ITupleNamesGenericHandler<(TupleNamesEntity A, string B)> Lazy => lazy.Value;

    public ITupleNamesGenericHandler<(TupleNamesEntity A, string B)> Func => func();
}

[IocRegister(ServiceLifetime.Transient)]
public sealed class TupleNamesGenericOtherConsumer(
    Lazy<ITupleNamesGenericHandler<(TupleNamesEntity, string)>> lazy,
    Func<ITupleNamesGenericHandler<(TupleNamesEntity, string)>> func)
{
    public ITupleNamesGenericHandler<(TupleNamesEntity, string)> Lazy => lazy.Value;

    public ITupleNamesGenericHandler<(TupleNamesEntity, string)> Func => func();
}

[IocContainer]
public sealed partial class TupleNamesContainer;
