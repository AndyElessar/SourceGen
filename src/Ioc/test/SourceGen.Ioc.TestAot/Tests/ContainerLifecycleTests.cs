namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for container construction order, singleton ownership across scopes,
/// and fallback provider scope/disposal integration.
/// </summary>
public sealed class ContainerLifecycleTests
{
    [Test]
    public async Task EagerSingleton_InjectingWrappers_ReceivesInitializedWrappers()
    {
        using var container = new EagerInitOrderContainer();

        var consumer = container.GetRequiredService<LifecycleWrapperConsumer>();

        await Assert.That(consumer.Lazy).IsNotNull();
        await Assert.That(consumer.Factory).IsNotNull();
        await Assert.That(consumer.Lazy.Value).IsNotNull();
        await Assert.That(consumer.Factory()).IsNotNull();
    }

    [Test]
    public async Task EagerSingleton_FactoryResolvingEagerSingletonDuringConstruction_ReceivesSameInstance()
    {
        using var container = new EagerInitOrderContainer();

        var service = container.GetRequiredService<LifecycleProviderFactoryService>();
        var dependency = container.GetRequiredService<ILifecycleEagerDependency>();

        await Assert.That(service.Dependency).IsSameReferenceAs(dependency);
    }

    [Test]
    public async Task LazySingleton_FirstResolvedFromScope_IsSharedWithRootAndOtherScopes()
    {
        await using var container = new LazySingletonScopeContainer();
        await using var scope1 = container.CreateAsyncScope();
        await using var scope2 = container.CreateAsyncScope();

        var fromScope1 = scope1.ServiceProvider.GetRequiredService<ILifecycleLazySingleton>();
        var fromScope2 = scope2.ServiceProvider.GetRequiredService<ILifecycleLazySingleton>();
        var fromRoot = container.GetRequiredService<ILifecycleLazySingleton>();

        await Assert.That(fromScope2).IsSameReferenceAs(fromScope1);
        await Assert.That(fromRoot).IsSameReferenceAs(fromScope1);
    }

    [Test]
    public async Task LazySingleton_FirstResolvedFromScope_IsDisposedOnlyByRoot()
    {
        var container = new LazySingletonScopeContainer();
        ILifecycleLazySingleton singleton;

        using(var scope = container.CreateScope())
        {
            singleton = scope.ServiceProvider.GetRequiredService<ILifecycleLazySingleton>();
        }

        await Assert.That(singleton.DisposeCount).IsEqualTo(0);

        container.Dispose();

        await Assert.That(singleton.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task FallbackProvider_ScopedService_IsScopedPerContainerScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<LifecycleExternalService>();
        var container = (FallbackScopeContainer)new FallbackScopeContainer().CreateServiceProvider(services);

        LifecycleExternalService fromScope1;
        using(var scope1 = container.CreateScope())
        {
            fromScope1 = scope1.ServiceProvider.GetRequiredService<LifecycleFallbackConsumer>().External;
            var direct = scope1.ServiceProvider.GetRequiredService(typeof(LifecycleExternalService));

            await Assert.That(direct).IsSameReferenceAs(fromScope1);
        }

        await Assert.That(fromScope1.IsDisposed).IsTrue();

        using(var scope2 = container.CreateScope())
        {
            var fromScope2 = scope2.ServiceProvider.GetRequiredService<LifecycleFallbackConsumer>().External;

            await Assert.That(fromScope2).IsNotSameReferenceAs(fromScope1);
        }

        container.Dispose();
    }

    [Test]
    public async Task FallbackProvider_Scope_IsCreatedOnlyOnFirstFallbackResolution()
    {
        var services = new ServiceCollection();
        services.AddScoped<LifecycleExternalService>();
        using var inner = services.BuildServiceProvider();
        var fallback = new CountingScopeFactoryProvider(inner);
        using var container = new FallbackScopeContainer(fallback);

        using(container.CreateScope())
        {
        }

        await Assert.That(fallback.CreatedScopes).IsEqualTo(0);

        using(var scope = container.CreateScope())
        {
            var first = scope.ServiceProvider.GetRequiredService(typeof(LifecycleExternalService));
            var second = scope.ServiceProvider.GetRequiredService<LifecycleFallbackConsumer>().External;

            await Assert.That(second).IsSameReferenceAs(first);
        }

        await Assert.That(fallback.CreatedScopes).IsEqualTo(1);
    }

    [Test]
    public async Task FallbackProvider_CreatedByServiceProviderFactory_IsDisposedWithContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LifecycleExternalService>();
        var container = (FallbackScopeContainer)new FallbackScopeContainer().CreateServiceProvider(services);

        var external = (LifecycleExternalService)container.GetRequiredService(typeof(LifecycleExternalService));

        await Assert.That(external.IsDisposed).IsFalse();

        await container.DisposeAsync();

        await Assert.That(external.IsDisposed).IsTrue();
    }

    [Test]
    public async Task FallbackProvider_PassedToConstructor_IsNotDisposedWithContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LifecycleExternalService>();
        using var provider = services.BuildServiceProvider();
        var external = provider.GetRequiredService<LifecycleExternalService>();

        new FallbackScopeContainer(provider).Dispose();

        await Assert.That(external.IsDisposed).IsFalse();
    }

    [Test]
    public async Task GenericResolution_ServiceOnlyInFallback_MatchesNonGenericResolution()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LifecycleExternalService>();
        services.AddKeyedSingleton<LifecycleExternalService>("keyed");
        using var container = (FallbackScopeContainer)new FallbackScopeContainer().CreateServiceProvider(services);

        var service = container.GetRequiredService(typeof(LifecycleExternalService));
        var keyedService = container.GetRequiredKeyedService(typeof(LifecycleExternalService), "keyed");

        await Assert.That(container.GetService<LifecycleExternalService>()).IsSameReferenceAs(service);
        await Assert.That(container.GetRequiredService<LifecycleExternalService>()).IsSameReferenceAs(service);
        await Assert.That(container.GetServices<LifecycleExternalService>().Single()).IsSameReferenceAs(service);
        await Assert.That(container.GetKeyedService<LifecycleExternalService>("keyed")).IsSameReferenceAs(keyedService);
        await Assert.That(container.GetRequiredKeyedService<LifecycleExternalService>("keyed")).IsSameReferenceAs(keyedService);
        await Assert.That(container.GetKeyedServices<LifecycleExternalService>("keyed").Single()).IsSameReferenceAs(keyedService);
    }

    [Test]
    public async Task GenericResolution_ServiceMissingEverywhere_RequiredThrowsAndOptionalReturnsEmpty()
    {
        using var container = (FallbackScopeContainer)new FallbackScopeContainer().CreateServiceProvider(new ServiceCollection());

        await Assert.That(container.GetService<LifecycleExternalService>()).IsNull();
        await Assert.That(container.GetKeyedService<LifecycleExternalService>("missing")).IsNull();
        await Assert.That(container.GetServices<LifecycleExternalService>()).IsEmpty();
        await Assert.That(() => container.GetRequiredService<LifecycleExternalService>()).Throws<InvalidOperationException>();
        await Assert.That(() => container.GetRequiredKeyedService<LifecycleExternalService>("missing")).Throws<InvalidOperationException>();
    }

    /// <summary>Fallback provider that counts scopes created through its <see cref="IServiceScopeFactory"/>.</summary>
    private sealed class CountingScopeFactoryProvider(IServiceProvider inner) : IServiceProvider, IServiceScopeFactory
    {
        public int CreatedScopes { get; private set; }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? this : inner.GetService(serviceType);

        public IServiceScope CreateScope()
        {
            CreatedScopes++;
            return inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
        }
    }
}
