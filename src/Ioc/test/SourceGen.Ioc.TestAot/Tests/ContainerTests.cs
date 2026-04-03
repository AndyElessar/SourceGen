namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for standalone IIocContainer implementation behavior.
/// </summary>
public sealed class ContainerTests
{
    #region IServiceProvider Tests

    [Test]
    public async Task GetService_RegisteredType_ReturnsInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetService<ISingletonService>();

        // Assert
        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task GetService_UnregisteredType_ReturnsNull()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetService<UnregisteredService>();

        // Assert
        await Assert.That(service).IsNull();
    }

    [Test]
    public async Task GetRequiredService_RegisteredType_ReturnsInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<ISingletonService>();

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.InstanceId).IsNotEqualTo(Guid.Empty);
    }

    [Test]
    public async Task GetRequiredService_UnregisteredType_ThrowsException()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act & Assert
        await Assert.That(() => container.GetRequiredService<UnregisteredService>())
            .Throws<InvalidOperationException>();
    }

    #endregion

    #region Lifetime Tests

    [Test]
    public async Task Singleton_ReturnsSameInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var instance1 = container.GetRequiredService<ISingletonService>();
        var instance2 = container.GetRequiredService<ISingletonService>();

        // Assert
        await Assert.That(instance1.InstanceId).IsEqualTo(instance2.InstanceId);
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }

    [Test]
    public async Task Transient_ReturnsNewInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var instance1 = container.GetRequiredService<ITransientService>();
        var instance2 = container.GetRequiredService<ITransientService>();

        // Assert
        await Assert.That(instance1.InstanceId).IsNotEqualTo(instance2.InstanceId);
        await Assert.That(instance1).IsNotSameReferenceAs(instance2);
    }

    #endregion

    #region IKeyedServiceProvider Tests

    [Test]
    public async Task GetKeyedService_WithKey_ReturnsCorrectInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var serviceA = container.GetKeyedService<IKeyedService>("A");
        var serviceB = container.GetKeyedService<IKeyedService>("B");

        // Assert
        await Assert.That(serviceA).IsNotNull();
        await Assert.That(serviceA!.Key).IsEqualTo("A");

        await Assert.That(serviceB).IsNotNull();
        await Assert.That(serviceB!.Key).IsEqualTo("B");
    }

    [Test]
    public async Task GetKeyedService_UnregisteredKey_ReturnsNull()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetKeyedService<IKeyedService>("NonExistentKey");

        // Assert
        await Assert.That(service).IsNull();
    }

    [Test]
    public async Task GetRequiredKeyedService_WithKey_ReturnsInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredKeyedService<IKeyedService>("A");

        // Assert
        await Assert.That(service.Key).IsEqualTo("A");
    }

    #endregion

    #region IServiceScopeFactory Tests

    [Test]
    public async Task CreateScope_ReturnsValidScope()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        using var scope = container.CreateScope();

        // Assert
        await Assert.That(scope).IsNotNull();
        await Assert.That(scope.ServiceProvider).IsNotNull();
    }

    [Test]
    public async Task CreateScope_ScopedService_SameScopeReturnsSameInstance()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        using var scope = container.CreateScope();
        var instance1 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        var instance2 = scope.ServiceProvider.GetRequiredService<IScopedService>();

        // Assert
        await Assert.That(instance1.InstanceId).IsEqualTo(instance2.InstanceId);
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }

    [Test]
    public async Task CreateScope_ScopedService_DifferentScopesReturnDifferentInstances()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        Guid scopeId1, scopeId2;

        using (var scope1 = container.CreateScope())
        {
            scopeId1 = scope1.ServiceProvider.GetRequiredService<IScopedService>().InstanceId;
        }

        using (var scope2 = container.CreateScope())
        {
            scopeId2 = scope2.ServiceProvider.GetRequiredService<IScopedService>().InstanceId;
        }

        // Assert
        await Assert.That(scopeId1).IsNotEqualTo(scopeId2);
    }

    #endregion

    #region IServiceProviderIsService Tests

    [Test]
    public async Task IsService_RegisteredType_ReturnsTrue()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var isService = container.IsService(typeof(ISingletonService));

        // Assert
        await Assert.That(isService).IsTrue();
    }

    [Test]
    public async Task IsService_UnregisteredType_ReturnsFalse()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var isService = container.IsService(typeof(UnregisteredService));

        // Assert
        await Assert.That(isService).IsFalse();
    }

    #endregion

    #region IServiceProviderIsKeyedService Tests

    [Test]
    public async Task IsKeyedService_RegisteredKeyedType_ReturnsTrue()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var isKeyedService = container.IsKeyedService(typeof(IKeyedService), "A");

        // Assert
        await Assert.That(isKeyedService).IsTrue();
    }

    [Test]
    public async Task IsKeyedService_UnregisteredKey_ReturnsFalse()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var isKeyedService = container.IsKeyedService(typeof(IKeyedService), "NonExistent");

        // Assert
        await Assert.That(isKeyedService).IsFalse();
    }

    #endregion

    #region Injection Tests

    [Test]
    public async Task Container_PropertyInjection_IsInjected()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<IPropertyInjectedService>();

        // Assert
        await Assert.That(service.PropertyDependency).IsNotNull();
    }

    [Test]
    public async Task Container_FieldInjection_IsInjected()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<IFieldInjectedService>();

        // Assert
        await Assert.That(service.GetFieldDependency()).IsNotNull();
    }

    [Test]
    public async Task Container_MethodInjection_IsInjected()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<IMethodInjectedService>();

        // Assert
        await Assert.That(service.IsInitialized).IsTrue();
        await Assert.That(service.MethodDependency).IsNotNull();
    }

    #endregion

    #region Decorator Tests

    [Test]
    public async Task Container_Decorator_ChainIsApplied()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var service = container.GetRequiredService<IDecoratedService>();

        // Assert
        await Assert.That(service.DecoratorCount).IsEqualTo(2);
        // Decorators are applied in reverse order: first in array wraps outermost
        // Array: [Decorator1, Decorator2] => Decorator1(Decorator2(Core))
        await Assert.That(service.GetMessage()).IsEqualTo("Decorator1(Decorator2(Core))");
    }

    #endregion

    #region Open Generic Tests

    [Test]
    public async Task Container_OpenGeneric_ConstructorDiscovery_Resolves()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var consumer = container.GetRequiredService<HandlerConsumerA>();

        // Assert
        await Assert.That(consumer.Handler).IsNotNull();

        var result = consumer.Execute(new RequestA("Container"));
        await Assert.That(result.Result).IsEqualTo("Handled: Container");
    }

    [Test]
    public async Task Container_OpenGeneric_ServiceProviderDiscovery_Resolves()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var locator = container.GetRequiredService<ServiceLocatorB>();

        // Assert
        await Assert.That(locator.GetHandler()).IsNotNull();

        var result = locator.Execute(new RequestB(10));
        await Assert.That(result.Result).IsEqualTo(20);
    }

    [Test]
    public async Task Container_OpenGeneric_IocDiscoverAttribute_Resolves()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act — IHandler<RequestC, ResponseC> is discovered via [IocDiscover] on Marker in OpenGenericDiscovery.cs
        var handler = container.GetRequiredService<IHandler<RequestC, ResponseC>>();

        // Assert
        await Assert.That(handler).IsNotNull();
        var result = handler.Handle(new RequestC(true));
        await Assert.That(result.Result).IsFalse();
    }

    #endregion

    #region Dispose Tests

    [Test]
    public async Task Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var container = new ContainerModule();

        // Act - Should not throw
        container.Dispose();
        container.Dispose();

        // Assert - If we get here, no exception was thrown
        await Task.CompletedTask;
    }

    [Test]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var container = new ContainerModule();

        // Act - Should not throw
        await container.DisposeAsync();
        await container.DisposeAsync();

        // Assert - If we get here, no exception was thrown
        await Task.CompletedTask;
    }

    #endregion

    #region Helper Types

    /// <summary>Unregistered service for testing null/exception scenarios.</summary>
    private sealed class UnregisteredService;

    #endregion
}
