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

[IocContainer]
public sealed partial class TupleNamesContainer;
