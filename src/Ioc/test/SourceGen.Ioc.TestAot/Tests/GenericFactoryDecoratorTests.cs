namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that a generic factory creates the service type's instance, which decorators wrap and Lazy wrappers share,
/// while the implementation type resolves its own constructed registration.
/// </summary>
public sealed class GenericFactoryDecoratorTests
{
    private const string DecoratedDescription = "LoggingGenericFactoryHandler(FactoryCreatedHandler)";

    [Test]
    public async Task GenericFactoryWithDecorator_StandaloneContainer_WrapsServiceTypeFactoryInstance()
    {
        using var container = new GenericFactoryContainer();

        var consumer = container.GetRequiredService<GenericFactoryHandlerConsumer>();

        await Assert.That(consumer.Handler.Describe()).IsEqualTo(DecoratedDescription);
        await Assert.That(consumer.Lazy.Value).IsSameReferenceAs(consumer.Handler);
        await Assert.That(consumer.Implementation.Describe()).IsEqualTo("GenericFactoryHandler");
    }

    [Test]
    public async Task GenericFactoryWithDecorator_MsDi_WrapsServiceTypeFactoryInstance()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<GenericFactoryHandlerConsumer>();

        await Assert.That(consumer.Handler.Describe()).IsEqualTo(DecoratedDescription);
        await Assert.That(consumer.Lazy.Value).IsSameReferenceAs(consumer.Handler);
        await Assert.That(consumer.Implementation.Describe()).IsEqualTo("GenericFactoryHandler");
    }
}
