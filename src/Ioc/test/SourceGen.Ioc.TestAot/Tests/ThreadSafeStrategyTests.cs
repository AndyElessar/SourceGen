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
    public async Task ThreadSafeStrategy_CompareExchange_ResolvesSingleton()
    {
        // Arrange
        using var container = new ThreadSafeCompareExchangeContainer();

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
    public async Task ThreadSafeStrategy_CompareExchange_ConcurrentAccess_ReturnsOnlyOneInstance()
    {
        // Arrange
        using var container = new ThreadSafeCompareExchangeContainer();
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

    [Test]
    public async Task Container_AfterDispose_GetService_ThrowsObjectDisposedException()
    {
        // Arrange
        var container = new ThreadSafeLockContainer();
        container.Dispose();

        // Act & Assert
        await Assert.That(() => container.GetService<ISingletonService>())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Container_AfterDispose_GetRequiredService_ThrowsObjectDisposedException()
    {
        // Arrange
        var container = new ThreadSafeSemaphoreSlimContainer();
        container.Dispose();

        // Act & Assert
        await Assert.That(() => container.GetRequiredService<ISingletonService>())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Container_AfterDispose_CreateScope_ThrowsObjectDisposedException()
    {
        // Arrange
        var container = new ThreadSafeSpinLockContainer();
        container.Dispose();

        // Act & Assert
        await Assert.That(() => container.CreateScope())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Container_CompareExchange_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var container = new ThreadSafeCompareExchangeContainer();
        container.Dispose();

        // Act & Assert
        await Assert.That(() => container.GetService<ISingletonService>())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Container_MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var container = new ThreadSafeLockContainer();

        // Act - dispose multiple times (should not throw)
        container.Dispose();
        container.Dispose();
        container.Dispose();

        // Assert - container is disposed, subsequent GetService should throw
        await Assert.That(() => container.GetService<ISingletonService>())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Container_ConcurrentDispose_DoesNotThrow()
    {
        // Arrange
        var container = new ThreadSafeSemaphoreSlimContainer();
        const int concurrentDisposes = 10;

        // Act - simulate concurrent disposal
        var tasks = Enumerable.Range(0, concurrentDisposes)
            .Select(_ => Task.Run(() => container.Dispose()))
            .ToArray();

        // Assert - no exception thrown
        await Assert.That(async () => await Task.WhenAll(tasks)).ThrowsNothing();
    }

    [Test]
    public async Task Container_AfterDisposeAsync_GetService_ThrowsObjectDisposedException()
    {
        // Arrange
        var container = new ThreadSafeLockContainer();
        await container.DisposeAsync();

        // Act & Assert
        await Assert.That(() => container.GetService<ISingletonService>())
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task Container_ThreadSafeNone_AfterDispose_GetService_ThrowsObjectDisposedException()
    {
        // Arrange - even ThreadSafeStrategy.None should throw after dispose
        var container = new ThreadSafeNoneContainer();
        container.Dispose();

        // Act & Assert
        await Assert.That(() => container.GetService<ISingletonService>())
            .Throws<ObjectDisposedException>();
    }
}
