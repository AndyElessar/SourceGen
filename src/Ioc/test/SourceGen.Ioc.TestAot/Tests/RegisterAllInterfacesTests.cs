namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for <c>RegisterAllInterfaces = true</c> registration.
/// In standalone containers the generator registers only the concrete type;
/// interface forwarding (<c>IServiceA</c>, <c>IServiceB</c>) is generated only
/// in the MS.Extensions.DI Register path where forwarding lambdas are supported.
/// </summary>
public sealed class RegisterAllInterfacesTests
{
    #region Standalone Container Tests

    [Test]
    public async Task RegisterAllInterfaces_StandaloneContainer_ConcreteTypeIsResolvable()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act — standalone container registers the concrete type; access it directly
        var service = container.GetRequiredService<MultiInterfaceService>();

        // Assert — concrete type implements both interfaces
        await Assert.That(service).IsNotNull();
        await Assert.That(service.NameA).IsEqualTo("IServiceA");
        await Assert.That(service.NameB).IsEqualTo("IServiceB");
    }

    #endregion

    #region MS.Extensions.DI Integration Tests

    [Test]
    public async Task RegisterAllInterfaces_MsDi_ResolvesViaFirstInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var serviceA = provider.GetRequiredService<IServiceA>();

        // Assert
        await Assert.That(serviceA).IsNotNull();
        await Assert.That(serviceA.NameA).IsEqualTo("IServiceA");
    }

    [Test]
    public async Task RegisterAllInterfaces_MsDi_ResolvesViaSecondInterface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var serviceB = provider.GetRequiredService<IServiceB>();

        // Assert
        await Assert.That(serviceB).IsNotNull();
        await Assert.That(serviceB.NameB).IsEqualTo("IServiceB");
    }

    [Test]
    public async Task RegisterAllInterfaces_MsDi_BothInterfacesPointToSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act — singleton registered for all interfaces shares the same instance
        var serviceA = provider.GetRequiredService<IServiceA>();
        var serviceB = provider.GetRequiredService<IServiceB>();

        // Assert — same underlying object
        await Assert.That(serviceA as object).IsSameReferenceAs(serviceB as object);
    }

    #endregion
}
