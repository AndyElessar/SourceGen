using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc;

namespace SourceGen.Ioc.Benchmark;

/// <summary>
/// Test service interface for thread-safe strategy benchmarking.
/// </summary>
public interface ISingletonBenchmarkService
{
    Guid InstanceId { get; }
}

/// <summary>
/// Test service implementation for thread-safe strategy benchmarking.
/// </summary>
[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonBenchmarkService)])]
public sealed class SingletonBenchmarkService : ISingletonBenchmarkService
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

/// <summary>
/// Container with ThreadSafeStrategy.None for benchmarking.
/// </summary>
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None)]
public sealed partial class BenchmarkContainerNone;

/// <summary>
/// Container with ThreadSafeStrategy.Lock for benchmarking.
/// </summary>
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock)]
public sealed partial class BenchmarkContainerLock;

/// <summary>
/// Container with ThreadSafeStrategy.SemaphoreSlim (default) for benchmarking.
/// </summary>
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim)]
public sealed partial class BenchmarkContainerSemaphoreSlim;

/// <summary>
/// Container with ThreadSafeStrategy.SpinLock for benchmarking.
/// </summary>
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SpinLock)]
public sealed partial class BenchmarkContainerSpinLock;
