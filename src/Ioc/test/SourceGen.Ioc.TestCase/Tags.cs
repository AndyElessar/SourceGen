namespace SourceGen.Ioc.TestCase;

/// <summary>Interface for tagged service testing.</summary>
public interface ITaggedService
{
    string ServiceName { get; }
}

internal sealed class TaggedServiceA : ITaggedService
{
    public string ServiceName => "TaggedServiceA";
}

internal sealed class TaggedServiceB : ITaggedService
{
    public string ServiceName => "TaggedServiceB";
}

[IocRegisterFor<TaggedServiceA>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ITaggedService)], Tags = ["groupA"])]
[IocRegisterFor<TaggedServiceB>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ITaggedService)], Tags = ["groupB"])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class TagsModule;
