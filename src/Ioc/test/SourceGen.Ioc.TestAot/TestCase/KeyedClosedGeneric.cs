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

public static class KeyedClosingKeys
{
    public const string X = "x";
}

public interface IKeyedFallbackHandler<T>;

/// <summary>A registration with another value key, which cannot resolve key "x" (issue #217).</summary>
[IocRegister(Key = "y", ServiceTypes = [typeof(IKeyedFallbackHandler<>)])]
public sealed class ValueKeyFallbackHandler<T> : IKeyedFallbackHandler<T>;

/// <summary>A registration with a key given as C# code, which a value key without a same-key registration falls back to.</summary>
[IocRegister(Key = nameof(KeyedClosingKeys.X), KeyType = KeyType.Csharp, ServiceTypes = [typeof(IKeyedFallbackHandler<>)])]
public sealed class CodeKeyFallbackHandler<T> : IKeyedFallbackHandler<T>;

/// <summary>Consumer of a value key that only the C# code key registration resolves.</summary>
[IocRegister]
public sealed class KeyedFallbackConsumer([FromKeyedServices("x")] IKeyedFallbackHandler<int> handler)
{
    public IKeyedFallbackHandler<int> Handler => handler;
}

public interface IKeyedDictionaryHandler<T>;

/// <summary>A non-keyed registration, which a dictionary does not resolve.</summary>
[IocRegister(ServiceTypes = [typeof(IKeyedDictionaryHandler<>)])]
public sealed class PlainDictionaryHandler<T> : IKeyedDictionaryHandler<T>;

[IocRegister(Key = "a", ServiceTypes = [typeof(IKeyedDictionaryHandler<>)])]
public sealed class ADictionaryHandler<T> : IKeyedDictionaryHandler<T>;

[IocRegister(Key = "b", ServiceTypes = [typeof(IKeyedDictionaryHandler<>)])]
public sealed class BDictionaryHandler<T> : IKeyedDictionaryHandler<T>;

/// <summary>A dictionary resolves every keyed registration of its value type (issue #217).</summary>
[IocRegister]
public sealed class KeyedDictionaryConsumer(IDictionary<string, IKeyedDictionaryHandler<int>> handlers)
{
    public IDictionary<string, IKeyedDictionaryHandler<int>> Handlers => handlers;
}

public interface IKeyedInvocationHandler<T>;

/// <summary>A non-keyed registration declared first, which a keyed invocation does not close (issue #217).</summary>
[IocRegister(ServiceTypes = [typeof(IKeyedInvocationHandler<>)])]
public sealed class PlainInvocationHandler<T> : IKeyedInvocationHandler<T>;

/// <summary>The keyed registration a keyed invocation closes.</summary>
[IocRegister(Key = "k", ServiceTypes = [typeof(IKeyedInvocationHandler<>)])]
public sealed class KeyedInvocationHandler<T> : IKeyedInvocationHandler<T>;

[IocContainer]
public sealed partial class KeyedClosedGenericContainer;
