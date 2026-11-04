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

    [Test]
    public async Task AsyncInitService_ResolvedInScopeBeforeRoot_DelegatesToRootSingleton()
    {
        AsyncInitServiceProbe.Reset();
        using var fallback = new InjectionModule();
        await using var module = new AsyncInjectionModule(fallback);
        using var scope = module.CreateScope();

        var fromScope = await scope.ServiceProvider.GetRequiredService<Task<IAsyncInitService>>();
        var fromRoot = await module.GetAsyncInitServiceAsync();

        await Assert.That(fromScope).IsSameReferenceAs(fromRoot);
        await Assert.That(AsyncInitServiceProbe.ConstructedCount).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncInitService_ResolvedAsServiceTypeTask_ReturnsSameCachedTask()
    {
        using var fallback = new InjectionModule();
        await using var module = new AsyncInjectionModule(fallback);
        using var scope = module.CreateScope();

        var fromAccessor = module.GetAsyncInitServiceAsync();
        var fromProvider = module.GetRequiredService<Task<IAsyncInitService>>();
        var fromScope = scope.ServiceProvider.GetRequiredService<Task<IAsyncInitService>>();

        await Assert.That((object)fromProvider).IsSameReferenceAs(fromAccessor);
        await Assert.That((object)fromScope).IsSameReferenceAs(fromAccessor);
    }

    [Test]
    public async Task AsyncInitService_CompareExchangeContainer_ConcurrentResolution_InitializesOnce()
    {
        ContendedAsyncInitServiceProbe.Reset();
        using var module = new CompareExchangeAsyncInjectionModule();

        var services = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => module.GetContendedAsyncInitServiceAsync())));

        await Assert.That(services.Distinct().Count()).IsEqualTo(1);
        await Assert.That(ContendedAsyncInitServiceProbe.InitializeCount).IsEqualTo(1);
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
    public async Task AsyncInitService_StandaloneContainer_IsResolvedAsTaskOfServiceType()
    {
        using var fallback = new InjectionModule();
        await using var module = new AsyncInjectionModule(fallback);

        var service1 = await module.GetRequiredService<Task<IAsyncInitService>>();
        var service2 = await (Task<IAsyncInitService>)module.GetRequiredService(typeof(Task<IAsyncInitService>));

        await Assert.That(service1.IsInitialized).IsTrue();
        await Assert.That(service2).IsSameReferenceAs(service1);
    }

    [Test]
    public async Task AsyncInjectionContainer_AsyncInitService_IsNotResolvedAsServiceType()
    {
        using var container = new AsyncInjectionContainer();

        await Assert.That(container.GetService<IAsyncInitService>()).IsNull();
        await Assert.That(container.IsService(typeof(IAsyncInitService))).IsFalse();
        await Assert.That(container.IsService(typeof(Task<IAsyncInitService>))).IsTrue();
    }

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
