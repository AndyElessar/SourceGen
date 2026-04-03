namespace SourceGen.Ioc.TestCase;

/// <summary>Processor interface for keyed collection testing.</summary>
public interface IProcessor
{
    string ProcessorName { get; }
}

internal sealed class ProcessorAlpha : IProcessor
{
    public string ProcessorName => "Alpha";
}

internal sealed class ProcessorBeta : IProcessor
{
    public string ProcessorName => "Beta";
}

/// <summary>Registry that receives all processors via keyed dictionary injection.</summary>
public sealed class ProcessorRegistry(IDictionary<string, IProcessor> processors)
{
    public IDictionary<string, IProcessor> Processors => processors;
}

[IocRegisterFor<ProcessorAlpha>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IProcessor)], Key = "alpha")]
[IocRegisterFor<ProcessorBeta>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IProcessor)], Key = "beta")]
[IocRegisterFor<ProcessorRegistry>(ServiceLifetime.Transient)]
[IocDiscover<IDictionary<string, IProcessor>>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class KeyedCollectionModule;
