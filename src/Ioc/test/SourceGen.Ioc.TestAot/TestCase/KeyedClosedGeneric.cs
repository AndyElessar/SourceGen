namespace SourceGen.Ioc.TestAot.TestCase;

public interface IKeyedClosingHandler<T>;

/// <summary>Open generic registration closed for a value type argument.</summary>
[IocRegister(ServiceTypes = [typeof(IKeyedClosingHandler<>)])]
public sealed class KeyedClosingHandler<T> : IKeyedClosingHandler<T>;

/// <summary>A registration of the closed service type with another key, which does not keep the open generic from closing.</summary>
[IocRegister(Key = "other", ServiceTypes = [typeof(IKeyedClosingHandler<int>)])]
public sealed class OtherKeyIntHandler : IKeyedClosingHandler<int>;

/// <summary>Consumer of the non-keyed closed service type.</summary>
[IocRegister]
public sealed class KeyedClosingConsumer(IKeyedClosingHandler<int> handler)
{
    public IKeyedClosingHandler<int> Handler => handler;
}

public interface IKeyedOpenHandler<T>;

/// <summary>Keyed open generic registration closed for a keyed dependency.</summary>
[IocRegister(Key = "k", ServiceTypes = [typeof(IKeyedOpenHandler<>)])]
public sealed class KeyedOpenHandler<T> : IKeyedOpenHandler<T>;

/// <summary>A non-keyed registration of the closed service type, which does not keep the keyed open generic from closing.</summary>
[IocRegister(ServiceTypes = [typeof(IKeyedOpenHandler<int>)])]
public sealed class PlainIntHandler : IKeyedOpenHandler<int>;

/// <summary>Consumer of the keyed closed service type.</summary>
[IocRegister]
public sealed class KeyedOpenConsumer([IocInject("k")] IKeyedOpenHandler<int> handler)
{
    public IKeyedOpenHandler<int> Handler => handler;
}

[IocContainer]
public sealed partial class KeyedClosedGenericContainer;
