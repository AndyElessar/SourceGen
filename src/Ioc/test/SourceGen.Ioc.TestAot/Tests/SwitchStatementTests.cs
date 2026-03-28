namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for UseSwitchStatement = true in the generated container resolver.
/// Verifies that the switch-statement dispatch path correctly routes service
/// resolution for common lifecycle scenarios.
/// </summary>
public sealed class SwitchStatementTests
{
    [Test]
    public async Task SwitchStatement_Singleton_ResolvesCorrectly()
    {
        // Arrange
        using var container = new SwitchStatementContainer();

        // Act
        var service = container.GetRequiredService<ISingletonService>();

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.InstanceId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task SwitchStatement_Singleton_ReturnsSameInstance()
    {
        // Arrange
        using var container = new SwitchStatementContainer();

        // Act
        var s1 = container.GetRequiredService<ISingletonService>();
        var s2 = container.GetRequiredService<ISingletonService>();

        // Assert
        await Assert.That(s1.InstanceId).IsEqualTo(s2.InstanceId);
        await Assert.That(s1).IsSameReferenceAs(s2);
    }

    [Test]
    public async Task SwitchStatement_Transient_ReturnsDifferentInstances()
    {
        // Arrange
        using var container = new SwitchStatementContainer();

        // Act
        var t1 = container.GetRequiredService<ITransientService>();
        var t2 = container.GetRequiredService<ITransientService>();

        // Assert
        await Assert.That(t1.InstanceId).IsNotEqualTo(t2.InstanceId);
    }

    [Test]
    public async Task SwitchStatement_Scoped_SameScopeReturnsSameInstance()
    {
        // Arrange
        using var container = new SwitchStatementContainer();
        using var scope = container.CreateScope();

        // Act
        var s1 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        var s2 = scope.ServiceProvider.GetRequiredService<IScopedService>();

        // Assert
        await Assert.That(s1.InstanceId).IsEqualTo(s2.InstanceId);
    }

    [Test]
    public async Task SwitchStatement_UnregisteredService_ReturnsNull()
    {
        // Arrange
        using var container = new SwitchStatementContainer();

        // Act — ISingletonService is registered but IAsyncInitService is not in SwitchStatementContainer
        var service = container.GetService<IAsyncInitService>();

        // Assert
        await Assert.That(service).IsNull();
    }
}
