namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for EagerResolveOptions — verifies that containers configured with
/// EagerResolveOptions.SingletonAndScoped resolve singletons during construction
/// rather than on first use.
/// </summary>
[NotInParallel]
public sealed class EagerResolveTests
{
    [Test]
    public async Task AsyncEagerResolveContainer_SingletonAsyncInit_StartsDuringContainerConstruction()
    {
        AsyncEagerSingletonProbe.Reset();

        await using var container = new AsyncEagerResolveContainer();

        await Assert.That(AsyncEagerSingletonProbe.ConstructedCount).IsEqualTo(1);
        await Assert.That(AsyncEagerSingletonProbe.InitializeStartedCount).IsEqualTo(1);
        await Assert.That(AsyncEagerSingletonProbe.ConstructedCount).IsEqualTo(1);
        await Assert.That(AsyncEagerSingletonProbe.InitializeStartedCount).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncInjectionContainer_EagerNone_DoesNotStartAsyncInitDuringContainerConstruction()
    {
        AsyncInitServiceProbe.Reset();

        using var container = new AsyncInjectionContainer();

        await Assert.That(AsyncInitServiceProbe.ConstructedCount).IsEqualTo(0);
        await Assert.That(AsyncInitServiceProbe.InitializeStartedCount).IsEqualTo(0);

        var dependency = container.GetRequiredService<IInjectionDependency>();

        await Assert.That(dependency).IsNotNull();
        await Assert.That(AsyncInitServiceProbe.ConstructedCount).IsEqualTo(0);
        await Assert.That(AsyncInitServiceProbe.InitializeStartedCount).IsEqualTo(0);
    }

    [Test]
    public async Task EagerResolveContainer_Singleton_IsNotNullAfterConstruction()
    {
        // Arrange & Act
        using var container = new EagerResolveContainer();

        // Assert — singleton was eagerly resolved during construction
        var service = container.GetRequiredService<ISingletonService>();
        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task EagerResolveContainer_Singleton_ReturnsSameInstance()
    {
        // Arrange
        using var container = new EagerResolveContainer();

        // Act
        var s1 = container.GetRequiredService<ISingletonService>();
        var s2 = container.GetRequiredService<ISingletonService>();

        // Assert
        await Assert.That(s1.InstanceId).IsEqualTo(s2.InstanceId);
        await Assert.That(s1).IsSameReferenceAs(s2);
    }

    [Test]
    public async Task StandardContainer_Singleton_AlsoBehavesAsSingleton()
    {
        // Ensure default (non-eager) container also resolves correctly
        using var container = new ContainerModule();

        var s1 = container.GetRequiredService<ISingletonService>();
        var s2 = container.GetRequiredService<ISingletonService>();

        await Assert.That(s1.InstanceId).IsEqualTo(s2.InstanceId);
    }
}
