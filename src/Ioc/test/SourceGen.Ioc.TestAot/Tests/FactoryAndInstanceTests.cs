namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for factory and instance registration patterns.
/// Verifies that services created via Factory and Instance parameters
/// are resolved correctly in both standalone containers and MS.Extensions.DI.
/// </summary>
public sealed class FactoryAndInstanceTests
{
    #region Standalone Container Tests

    [Test]
    public async Task Factory_StandaloneContainer_ReturnsFactoryCreatedInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<IFactoryService>();

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.CreatedBy).IsEqualTo("FactoryServiceFactory");
        await Assert.That(service.DepName).IsEqualTo("FactoryDep");
        // Verify the dep was successfully resolved via the generator's factory dep path
        var dep = container.GetRequiredService<IFactoryDep>();
        await Assert.That(service.DepName).IsEqualTo(dep.Label);
    }

    [Test]
    public async Task FactoryWithDecorator_StandaloneContainer_WrapsSharedFactoryInstance()
    {
        using var container = new ContainerModule();

        var decorated = container.GetRequiredService<IDecoratedFactoryService>();
        var implementation = container.GetRequiredService<DecoratedFactoryService>();

        await Assert.That(decorated.Describe()).IsEqualTo("DecoratedFactoryServiceDecorator(DecoratedFactoryService)");
        await Assert.That(((DecoratedFactoryServiceDecorator)decorated).Inner).IsSameReferenceAs(implementation);
    }

    [Test]
    public async Task Instance_StandaloneContainer_ReturnsPredefinedInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<IInstanceService>();

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.Name).IsEqualTo("InstanceService");
    }

    [Test]
    public async Task Instance_StandaloneContainer_ReturnsSameInstanceOnEachResolve()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var instance1 = container.GetRequiredService<IInstanceService>();
        var instance2 = container.GetRequiredService<IInstanceService>();

        // Assert - static pre-created instance is always the same object
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }

    [Test]
    public async Task InstanceConsumer_StandaloneContainer_ResolvesInstanceDirectlyAndThroughWrappers()
    {
        using var container = new ContainerModule();

        var consumer = container.GetRequiredService<InstanceServiceConsumer>();
        var instance = container.GetRequiredService<IInstanceService>();

        await Assert.That(consumer.Direct).IsSameReferenceAs(instance);
        await Assert.That(consumer.Lazy.Value).IsSameReferenceAs(instance);
        await Assert.That(consumer.Factory()).IsSameReferenceAs(instance);
    }

    #endregion

    #region MS.Extensions.DI Integration Tests

    [Test]
    public async Task Factory_MsDi_ReturnsFactoryCreatedInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var service = provider.GetRequiredService<IFactoryService>();

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.CreatedBy).IsEqualTo("FactoryServiceFactory");
        await Assert.That(service.DepName).IsEqualTo("FactoryDep");
        // Verify the dep was successfully resolved via the generator's factory dep path
        var dep = provider.GetRequiredService<IFactoryDep>();
        await Assert.That(service.DepName).IsEqualTo(dep.Label);
    }

    [Test]
    public async Task FactoryWithDecorator_MsDi_WrapsSharedFactoryInstance()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        var decorated = provider.GetRequiredService<IDecoratedFactoryService>();
        var implementation = provider.GetRequiredService<DecoratedFactoryService>();

        await Assert.That(decorated.Describe()).IsEqualTo("DecoratedFactoryServiceDecorator(DecoratedFactoryService)");
        await Assert.That(((DecoratedFactoryServiceDecorator)decorated).Inner).IsSameReferenceAs(implementation);
    }

    [Test]
    public async Task Instance_MsDi_ReturnsPredefinedStaticInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var service1 = provider.GetRequiredService<IInstanceService>();
        var service2 = provider.GetRequiredService<IInstanceService>();

        // Assert
        await Assert.That(service1).IsNotNull();
        await Assert.That(service1.Name).IsEqualTo("InstanceService");
        await Assert.That(service1).IsSameReferenceAs(service2);
    }

    #endregion
}
