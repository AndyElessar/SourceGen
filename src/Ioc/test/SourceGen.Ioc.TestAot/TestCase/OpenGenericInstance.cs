namespace SourceGen.Ioc.TestAot.TestCase;

public interface IInstanceNode<T>;

/// <summary>Open generic registration whose instance is a static member of each closed type.</summary>
[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IInstanceNode<>)], Instance = nameof(Default))]
public sealed class InstanceNode<T> : IInstanceNode<T>
{
    public static readonly InstanceNode<T> Default = new();
}

/// <summary>Consumer that closes the open generic registration for <see cref="string"/>.</summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class InstanceNodeConsumer(IInstanceNode<string> node)
{
    public IInstanceNode<string> Node => node;
}

[IocContainer]
public sealed partial class InstanceNodeContainer;
