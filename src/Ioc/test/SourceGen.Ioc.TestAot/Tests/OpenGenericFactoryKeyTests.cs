namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that the closed registrations of an open generic registration call the Factory and use the Key of each closed type.
/// </summary>
public sealed class OpenGenericFactoryKeyTests
{
    [Test]
    public async Task OpenGenericFactoryKey_StandaloneContainer_CallsFactoryOfClosedType()
    {
        using var container = new FactoryKeyNodeContainer();

        var consumer = container.GetRequiredService<FactoryKeyNodeConsumer>();

        await Assert.That(consumer.Node is FactoryKeyNode<string> { IsCreatedByFactory: true }).IsTrue();
        await Assert.That(consumer.Node.Repo).IsTypeOf<FactoryKeyRepo<string>>();
    }

    [Test]
    public async Task OpenGenericFactoryKey_MsDi_CallsFactoryOfClosedTypeWithKey()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<FactoryKeyNodeConsumer>();

        await Assert.That(consumer.Node is FactoryKeyNode<string> { IsCreatedByFactory: true }).IsTrue();
        await Assert.That(consumer.Node.Repo).IsTypeOf<FactoryKeyRepo<string>>();
        await Assert.That(provider.GetKeyedService<IFactoryKeyNode<string>>(FactoryKeyNode<string>.Name)).IsSameReferenceAs(consumer.Node);
    }
}