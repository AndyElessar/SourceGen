using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc.TestAot.TestCase;
using SourceGen.Ioc.TestCase;

namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Integration tests for Register-generated IServiceCollection extension methods with MS.Extensions.DI.
/// </summary>
public sealed class RegisterIntegrationTests
{
    private const string InjectionDependencyName = "InjectionDependency";

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services
        .AddSourceGen_Ioc_TestCase()
        .AddSourceGen_Ioc_TestAot();
        return services.BuildServiceProvider();
    }

    #region Lifetime Tests

    [Test]
    public async Task Lifetime_Singleton_ReturnsSameInstance()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var instance1 = provider.GetRequiredService<ISingletonService>();
        var instance2 = provider.GetRequiredService<ISingletonService>();

        // Assert
        await Assert.That(instance1.InstanceId).IsEqualTo(instance2.InstanceId);
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }

    [Test]
    public async Task Lifetime_Scoped_SameScopeReturnsSameInstance()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act & Assert - Same scope returns same instance
        using var scope = provider.CreateScope();
        var instance1 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        var instance2 = scope.ServiceProvider.GetRequiredService<IScopedService>();

        await Assert.That(instance1.InstanceId).IsEqualTo(instance2.InstanceId);
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }

    [Test]
    public async Task Lifetime_Scoped_DifferentScopesReturnDifferentInstances()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        Guid scopeId1, scopeId2;

        using (var scope1 = provider.CreateScope())
        {
            scopeId1 = scope1.ServiceProvider.GetRequiredService<IScopedService>().InstanceId;
        }

        using (var scope2 = provider.CreateScope())
        {
            scopeId2 = scope2.ServiceProvider.GetRequiredService<IScopedService>().InstanceId;
        }

        // Assert
        await Assert.That(scopeId1).IsNotEqualTo(scopeId2);
    }

    [Test]
    public async Task Lifetime_Transient_ReturnsNewInstance()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var instance1 = provider.GetRequiredService<ITransientService>();
        var instance2 = provider.GetRequiredService<ITransientService>();

        // Assert
        await Assert.That(instance1.InstanceId).IsNotEqualTo(instance2.InstanceId);
        await Assert.That(instance1).IsNotSameReferenceAs(instance2);
    }

    #endregion

    #region Keyed Service Tests

    [Test]
    public async Task KeyedService_ResolvesCorrectImplementation_KeyA()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredKeyedService<IKeyedService>("A");

        // Assert
        await Assert.That(service.Key).IsEqualTo("A");
    }

    [Test]
    public async Task KeyedService_ResolvesCorrectImplementation_KeyB()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredKeyedService<IKeyedService>("B");

        // Assert
        await Assert.That(service.Key).IsEqualTo("B");
    }

    [Test]
    public async Task KeyedService_ScopedKeyed_WorksCorrectly()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredKeyedService<IKeyedService>("Scoped");

        // Assert
        await Assert.That(service.Key).IsEqualTo("Scoped");
    }

    #endregion

    #region Injection Tests

    [Test]
    public async Task Injection_PropertyInjection_IsInjected()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredService<IPropertyInjectedService>();

        // Assert
        await Assert.That(service.PropertyDependency).IsNotNull();
        await Assert.That(service.PropertyDependency!.Name).IsEqualTo(InjectionDependencyName);
    }

    [Test]
    public async Task Injection_FieldInjection_IsInjected()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredService<IFieldInjectedService>();

        // Assert
        await Assert.That(service.GetFieldDependency()).IsNotNull();
        await Assert.That(service.GetFieldDependency()!.Name).IsEqualTo(InjectionDependencyName);
    }

    [Test]
    public async Task Injection_MethodInjection_IsInjected()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredService<IMethodInjectedService>();

        // Assert
        await Assert.That(service.IsInitialized).IsTrue();
        await Assert.That(service.MethodDependency).IsNotNull();
        await Assert.That(service.MethodDependency!.Name).IsEqualTo(InjectionDependencyName);
    }

    [Test]
    public async Task Injection_ConstructorInjection_IsInjected()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredService<IConstructorInjectedService>();

        // Assert
        await Assert.That(service.ConstructorDependency).IsNotNull();
        await Assert.That(service.ConstructorDependency.Name).IsEqualTo(InjectionDependencyName);
    }

    #endregion

    #region Decorator Tests

    [Test]
    public async Task Decorator_ChainIsApplied_DecoratorCountIsCorrect()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredService<IDecoratedService>();

        // Assert - Should have 2 decorators wrapping the core service
        await Assert.That(service.DecoratorCount).IsEqualTo(2);
    }

    [Test]
    public async Task Decorator_ChainIsApplied_MessageShowsCorrectOrder()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var service = provider.GetRequiredService<IDecoratedService>();
        var message = service.GetMessage();

        // Assert - Decorators are applied in reverse order: first in array wraps outermost
        // Array: [Decorator1, Decorator2] => Decorator1(Decorator2(Core))
        await Assert.That(message).IsEqualTo("Decorator1(Decorator2(Core))");
    }

    #endregion

    #region Open Generic Tests

    [Test]
    public async Task OpenGeneric_ConstructorParameterDiscovery_Resolves()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var consumer = provider.GetRequiredService<HandlerConsumerA>();
        var result = consumer.Execute(new RequestA("Test"));

        // Assert
        await Assert.That(consumer.Handler).IsNotNull();
        await Assert.That(result.Result).IsEqualTo("Handled: Test");
    }

    [Test]
    public async Task OpenGeneric_ServiceProviderMethodDiscovery_Resolves()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var locator = provider.GetRequiredService<ServiceLocatorB>();
        var result = locator.Execute(new RequestB(21));

        // Assert
        await Assert.That(locator.GetHandler()).IsNotNull();
        await Assert.That(result.Result).IsEqualTo(42);
    }

    [Test]
    public async Task OpenGeneric_IocDiscoverAttribute_Resolves()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var handler = provider.GetService<IHandler<RequestC, ResponseC>>();

        // Assert
        await Assert.That(handler).IsNotNull();

        var result = handler!.Handle(new RequestC(true));
        await Assert.That(result.Result).IsFalse();
    }

    #endregion

    #region Collection Resolution Tests

    [Test]
    public async Task Collection_ReturnsAllImplementations()
    {
        // Arrange
        await using var provider = CreateServiceProvider();

        // Act
        var keyedServices = provider.GetKeyedServices<IKeyedService>("A")
            .Concat(provider.GetKeyedServices<IKeyedService>("B"))
            .ToList();

        // Assert - Should have at least KeyedServiceA and KeyedServiceB
        await Assert.That(keyedServices.Count).IsGreaterThanOrEqualTo(2);
    }

    #endregion
}
