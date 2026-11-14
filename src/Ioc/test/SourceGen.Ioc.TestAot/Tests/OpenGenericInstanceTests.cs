namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that the closed registrations of an open generic registration with an instance use the instance of each closed type.
/// </summary>
public sealed class OpenGenericInstanceTests
{
    [Test]
    public async Task OpenGenericInstance_StandaloneContainer_ResolvesInstanceOfClosedType()
    {
        using var container = new InstanceNodeContainer();

        var consumer = container.GetRequiredService<InstanceNodeConsumer>();

        await Assert.That(consumer.Node).IsSameReferenceAs(InstanceNode<string>.Default);
        await Assert.That(container.GetRequiredService<InstanceNode<string>>()).IsSameReferenceAs(InstanceNode<string>.Default);
    }

    [Test]
    public async Task OpenGenericInstance_MsDi_ResolvesInstanceOfClosedType()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<InstanceNodeConsumer>();

        await Assert.That(consumer.Node).IsSameReferenceAs(InstanceNode<string>.Default);

        // A closed type requested from the service provider is discovered at compile time too
        await Assert.That(provider.GetRequiredService<IInstanceNode<int>>()).IsSameReferenceAs(InstanceNode<int>.Default);
    }
}
