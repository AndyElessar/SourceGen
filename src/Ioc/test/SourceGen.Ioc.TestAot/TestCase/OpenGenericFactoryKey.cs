namespace SourceGen.Ioc.TestAot.TestCase;

public interface IFactoryKeyRepo<T>;

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryKeyRepo<>)])]
public sealed class FactoryKeyRepo<T> : IFactoryKeyRepo<T>;

public interface IFactoryKeyNode<T>
{
    IFactoryKeyRepo<T> Repo { get; }
}

/// <summary>
/// Open generic registration whose Factory and C# Key are members of the implementation type, closed per closed type.
/// </summary>
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFactoryKeyNode<>)], Factory = nameof(Create), Key = nameof(Name), KeyType = KeyType.Csharp)]
public sealed class FactoryKeyNode<T>(IFactoryKeyRepo<T> repo) : IFactoryKeyNode<T>
{
    public const string Name = "factory-key-node";

    public IFactoryKeyRepo<T> Repo => repo;

    /// <summary>Whether the instance was created by <see cref="Create"/> instead of the constructor.</summary>
    public bool IsCreatedByFactory { get; private init; }

    public static FactoryKeyNode<T> Create(IFactoryKeyRepo<T> repo) => new(repo) { IsCreatedByFactory = true };
}

/// <summary>Consumer that closes the open generic registration for <see cref="string"/>.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class FactoryKeyNodeConsumer([FromKeyedServices(FactoryKeyNode<string>.Name)] IFactoryKeyNode<string> node)
{
    public IFactoryKeyNode<string> Node => node;
}

[IocContainer]
public sealed partial class FactoryKeyNodeContainer;