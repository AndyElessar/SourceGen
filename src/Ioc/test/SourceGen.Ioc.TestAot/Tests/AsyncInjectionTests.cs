namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for async method injection pattern.
/// Verifies that services with [IocInject] async Task methods are properly
/// initialized before first use in both standalone and MS.Extensions.DI scenarios.
/// </summary>
[NotInParallel]
public sealed class AsyncInjectionTests
{
    #region Standalone Container Tests (via partial Task<T> accessor on AsyncInjectionModule)

    // AsyncInjectionModule.GetAsyncInitServiceAsync() is the generated partial accessor.
    // IInjectionDependency is provided by InjectionModule passed as a fallback provider.

    [Test]
    public async Task AsyncInitService_StandaloneContainer_IsInitializedAfterResolve()
    {
        // Arrange
        using var fallback = new InjectionModule();
        await using var module = new AsyncInjectionModule(fallback);

        // Act — resolve via the generated partial Task<T> accessor
        var service = await module.GetAsyncInitServiceAsync();

        // Assert
        await Assert.That(service.IsInitialized).IsTrue();
    }

    [Test]
    public async Task AsyncInitService_StandaloneContainer_HasCorrectDependencyName()
    {
        // Arrange
        using var fallback = new InjectionModule();
        await using var module = new AsyncInjectionModule(fallback);

        // Act
        var service = await module.GetAsyncInitServiceAsync();

        // Assert — initialized with the IInjectionDependency from InjectionModule
        await Assert.That(service.InitializedBy).IsNotNull();
        await Assert.That(service.InitializedBy).IsEqualTo("InjectionDependency");
    }

    [Test]
    public async Task AsyncInitService_StandaloneContainer_IsSingleton()
    {
        // Arrange
        using var fallback = new InjectionModule();
        await using var module = new AsyncInjectionModule(fallback);

        // Act — calling twice must return the same instance
        var instance1 = await module.GetAsyncInitServiceAsync();
        var instance2 = await module.GetAsyncInitServiceAsync();

        // Assert
        await Assert.That(instance1).IsSameReferenceAs(instance2);
    }

    #endregion

    #region MS.Extensions.DI Integration Tests (via Task<T>)

    [Test]
    public async Task AsyncInitService_MsDi_IsInitializedAfterAwaitingTask()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act — async-init services are registered as Task<T> in MS DI
        var task = provider.GetRequiredService<Task<IAsyncInitService>>();
        var service = await task;

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.IsInitialized).IsTrue();
    }

    [Test]
    public async Task AsyncInitService_MsDi_TaskResolvesTheSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var service1 = await provider.GetRequiredService<Task<IAsyncInitService>>();
        var service2 = await provider.GetRequiredService<Task<IAsyncInitService>>();

        // Assert — singleton-backed Task resolves the same underlying instance
        await Assert.That(service1).IsSameReferenceAs(service2);
    }

    #endregion

    #region Composite Container Tests

    [Test]
    public async Task AsyncInjectionContainer_InjectionDependency_IsResolvableFromCompositeContainer()
    {
        // Arrange — AsyncInjectionContainer imports both InjectionModule and AsyncInjectionModule
        using var container = new AsyncInjectionContainer();

        // Act — IInjectionDependency comes from the imported InjectionModule
        var dep = container.GetRequiredService<IInjectionDependency>();

        // Assert
        await Assert.That(dep).IsNotNull();
        await Assert.That(dep.Name).IsEqualTo("InjectionDependency");
    }

    #endregion
}
