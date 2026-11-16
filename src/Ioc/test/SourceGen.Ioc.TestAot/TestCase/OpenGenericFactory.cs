namespace SourceGen.Ioc.TestAot.TestCase;

public interface IFactoryNode<T>
{
    bool IsCreatedByFactory { get; }
}

/// <summary>
/// Open generic registration with a Factory: MS.DI would construct it without the factory, so it is not registered open
/// (SGIOC042) and only the closed types discovered at compile time are registered.
/// </summary>
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryNode<>)], Factory = nameof(Create))]
public sealed class FactoryNode<T> : IFactoryNode<T>
{
    /// <summary>Whether the instance was created by <see cref="Create"/> instead of the constructor.</summary>
    public bool IsCreatedByFactory { get; private init; }

    public static FactoryNode<T> Create() => new() { IsCreatedByFactory = true };
}

/// <summary>Consumer that closes the open generic registration for <see cref="string"/>.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class FactoryNodeConsumer(IFactoryNode<string> node)
{
    public IFactoryNode<string> Node => node;
}
