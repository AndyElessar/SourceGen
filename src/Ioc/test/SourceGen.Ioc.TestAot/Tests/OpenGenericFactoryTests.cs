namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that an open generic registration with a Factory is not registered open, so MS.DI never bypasses the factory, while
/// its closed types discovered at compile time call it.
/// </summary>
public sealed class OpenGenericFactoryTests
{
    [Test]
    public async Task OpenGenericFactory_MsDi_ClosedTypesCallFactoryWithoutOpenRegistration()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();

        await Assert.That(services.Any(static descriptor => descriptor.ServiceType == typeof(IFactoryNode<>) || descriptor.ServiceType == typeof(FactoryNode<>))).IsFalse();

        await using var provider = services.BuildServiceProvider();
        var consumer = provider.GetRequiredService<FactoryNodeConsumer>();

        await Assert.That(consumer.Node.IsCreatedByFactory).IsTrue();
        await Assert.That(provider.GetRequiredService<FactoryNode<string>>()).IsSameReferenceAs(consumer.Node);
    }
}
