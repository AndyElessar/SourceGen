namespace SourceGen.Ioc.TestAot.Container;

/// <summary>
/// The service of a factory whose return type is an interface that is not disposable: the container tests the type of
/// the object when it disposes it (Analysis.Model.spec, "Disposal kind").
/// </summary>
public interface IFactoryHandle
{
    IClock Clock { get; }

    bool IsDisposed { get; }
}

public sealed class FactoryHandle(IClock clock) : IFactoryHandle, IDisposable
{
    public IClock Clock => clock;

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

/// <summary>The service of a keyed Scoped factory: it receives the key and a Scoped dependency.</summary>
public sealed class FactoryNote(string key, Session session)
{
    public string Key => key;

    public Session Session => session;
}

/// <summary>The service of a factory that returns <see langword="null"/>.</summary>
public sealed class NullProduct;

/// <summary>Factory registrations (Annotations.spec, "Factory registration").</summary>
public static class AotFactories
{
    public const string NoteKey = "note";

    [IocFactory(ServiceLifetime.Singleton)]
    public static IFactoryHandle CreateHandle(IClock clock) => new FactoryHandle(clock);

    [IocFactory(ServiceLifetime.Scoped, Key = NoteKey)]
    public static FactoryNote CreateNote([ServiceKey] string key, Session session) => new(key, session);

    [IocFactory(ServiceLifetime.Transient)]
    public static NullProduct CreateNull() => null!;
}
