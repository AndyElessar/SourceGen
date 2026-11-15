namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that the parameters of a generic factory are resolved with the factory's type arguments, closing the open
/// generic registrations they depend on.
/// </summary>
public sealed class GenericFactoryParameterTests
{
    [Test]
    public async Task GenericFactoryParameter_StandaloneContainer_ResolvesClosedParameter()
    {
        using var container = new FactoryParameterContainer();

        var consumer = container.GetRequiredService<FactoryParameterConsumer>();
        var repository = container.GetRequiredService<IFactoryParameterRepository<GenericFactoryEntity>>();

        await Assert.That(consumer.Handler.Repository).IsTypeOf<FactoryParameterRepository<GenericFactoryEntity>>();
        await Assert.That(consumer.Handler.Repository).IsSameReferenceAs(repository);
    }

    [Test]
    public async Task GenericFactoryParameter_MsDi_ResolvesClosedParameter()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<FactoryParameterConsumer>();
        var repository = provider.GetRequiredService<IFactoryParameterRepository<GenericFactoryEntity>>();

        await Assert.That(consumer.Handler.Repository).IsTypeOf<FactoryParameterRepository<GenericFactoryEntity>>();
        await Assert.That(consumer.Handler.Repository).IsSameReferenceAs(repository);
    }

    [Test]
    public async Task GenericFactoryParameter_ClosedRegistrationWithDefaultFactory_StandaloneContainer_CallsFactoryForServiceType()
    {
        using var container = new FactoryParameterContainer();

        var handler = container.GetRequiredService<IFactoryParameterHandler<Task<FactoryParameterClosedEntity>>>();
        var implementation = container.GetRequiredService<FactoryParameterClosedHandler>();

        await Assert.That(handler).IsTypeOf<FactoryParameterHandler<FactoryParameterClosedEntity>>();
        await Assert.That(handler.Repository).IsTypeOf<FactoryParameterRepository<FactoryParameterClosedEntity>>();
        await Assert.That(implementation).IsNotNull();
    }

    [Test]
    public async Task GenericFactoryParameter_ClosedRegistrationWithDefaultFactory_MsDi_CallsFactoryForServiceType()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IFactoryParameterHandler<Task<FactoryParameterClosedEntity>>>();
        var implementation = provider.GetRequiredService<FactoryParameterClosedHandler>();

        await Assert.That(handler).IsTypeOf<FactoryParameterHandler<FactoryParameterClosedEntity>>();
        await Assert.That(handler.Repository).IsTypeOf<FactoryParameterRepository<FactoryParameterClosedEntity>>();
        await Assert.That(implementation).IsNotNull();
    }
}