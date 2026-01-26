using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc.TestAot.TestCase;
using SourceGen.Ioc.TestCase;

namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// AOT runtime tests for ThreadSafeStrategy feature.
/// Tests verify that each thread-safety strategy correctly resolves singleton services.
/// </summary>
public sealed class ThreadSafeStrategyTests
{
    [Test]
    public async Task ThreadSafeStrategy_None_ResolvesSingleton()
    {
        // Arrange
        using var container = new ThreadSafeNoneContainer();

        // Act
        var service1 = container.GetService<ISingletonService>();
        var service2 = container.GetService<ISingletonService>();

        // Assert
        await Assert.That(service1).IsNotNull();
        await Assert.That(service2).IsNotNull();
        await Assert.That(service1!.InstanceId).IsEqualTo(service2!.InstanceId);
    }

    [Test]
    public async Task ThreadSafeStrategy_Lock_ResolvesSingleton()
    {
        // Arrange
        using var container = new ThreadSafeLockContainer();

        // Act
        var service1 = container.GetService<ISingletonService>();
        var service2 = container.GetService<ISingletonService>();

        // Assert
        await Assert.That(service1).IsNotNull();
        await Assert.That(service2).IsNotNull();
        await Assert.That(service1!.InstanceId).IsEqualTo(service2!.InstanceId);
    }

    [Test]
    public async Task ThreadSafeStrategy_SemaphoreSlim_ResolvesSingleton()
    {
        // Arrange
        using var container = new ThreadSafeSemaphoreSlimContainer();

        // Act
        var service1 = container.GetService<ISingletonService>();
        var service2 = container.GetService<ISingletonService>();

        // Assert
        await Assert.That(service1).IsNotNull();
        await Assert.That(service2).IsNotNull();
        await Assert.That(service1!.InstanceId).IsEqualTo(service2!.InstanceId);
    }

    [Test]
    public async Task ThreadSafeStrategy_SpinLock_ResolvesSingleton()
    {
        // Arrange
        using var container = new ThreadSafeSpinLockContainer();

        // Act
        var service1 = container.GetService<ISingletonService>();
        var service2 = container.GetService<ISingletonService>();

        // Assert
        await Assert.That(service1).IsNotNull();
        await Assert.That(service2).IsNotNull();
        await Assert.That(service1!.InstanceId).IsEqualTo(service2!.InstanceId);
    }

    [Test]
    public async Task ThreadSafeStrategy_Lock_ConcurrentAccess_ReturnsOnlyOneInstance()
    {
        // Arrange
        using var container = new ThreadSafeLockContainer();
        const int concurrentRequests = 100;

        // Act - simulate concurrent access
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => Task.Run(() => container.GetService<ISingletonService>()))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - all should return the same singleton instance
        var distinctInstanceIds = results.Select(s => s!.InstanceId).Distinct().ToList();
        await Assert.That(distinctInstanceIds).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ThreadSafeStrategy_SemaphoreSlim_ConcurrentAccess_ReturnsOnlyOneInstance()
    {
        // Arrange
        using var container = new ThreadSafeSemaphoreSlimContainer();
        const int concurrentRequests = 100;

        // Act - simulate concurrent access
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => Task.Run(() => container.GetService<ISingletonService>()))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - all should return the same singleton instance
        var distinctInstanceIds = results.Select(s => s!.InstanceId).Distinct().ToList();
        await Assert.That(distinctInstanceIds).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ThreadSafeStrategy_SpinLock_ConcurrentAccess_ReturnsOnlyOneInstance()
    {
        // Arrange
        using var container = new ThreadSafeSpinLockContainer();
        const int concurrentRequests = 100;

        // Act - simulate concurrent access
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => Task.Run(() => container.GetService<ISingletonService>()))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - all should return the same singleton instance
        var distinctInstanceIds = results.Select(s => s!.InstanceId).Distinct().ToList();
        await Assert.That(distinctInstanceIds).Count().IsEqualTo(1);
    }

    [Test]
    [Skip("ThreadSafeStrategy.None may create multiple instances under concurrent access")]
    public async Task ThreadSafeStrategy_None_ConcurrentAccess_MayCreateMultipleInstances()
    {
        // Arrange
        using var container = new ThreadSafeNoneContainer();
        const int concurrentRequests = 100;

        // Act - simulate concurrent access
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => Task.Run(() => container.GetService<ISingletonService>()))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - ThreadSafeStrategy.None does NOT guarantee singleton behavior under concurrent access
        // This test documents that behavior - multiple instances may be created
        var distinctInstanceIds = results.Select(s => s!.InstanceId).Distinct().ToList();
        await Assert.That(distinctInstanceIds.Count).IsGreaterThanOrEqualTo(1);
    }
}
