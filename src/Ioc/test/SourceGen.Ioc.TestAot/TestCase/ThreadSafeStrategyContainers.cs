namespace SourceGen.Ioc.TestAot.TestCase;

#pragma warning disable SGIOC011 // Duplicated Registration Detected

/// <summary>
/// Container with ThreadSafeStrategy.None for testing.
/// Uses ISingletonService from TestCaseModule to test singleton behavior.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, ExplicitOnly = true)]
public sealed partial class ThreadSafeNoneContainer;

/// <summary>
/// Container with ThreadSafeStrategy.Lock for testing.
/// Uses ISingletonService from TestCaseModule to test singleton behavior.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock, ExplicitOnly = true)]
public sealed partial class ThreadSafeLockContainer;

/// <summary>
/// Container with ThreadSafeStrategy.SemaphoreSlim (default) for testing.
/// Uses ISingletonService from TestCaseModule to test singleton behavior.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim, ExplicitOnly = true)]
public sealed partial class ThreadSafeSemaphoreSlimContainer;

/// <summary>
/// Container with ThreadSafeStrategy.SpinLock for testing.
/// Uses ISingletonService from TestCaseModule to test singleton behavior.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SpinLock, ExplicitOnly = true)]
public sealed partial class ThreadSafeSpinLockContainer;

/// <summary>
/// Container with ThreadSafeStrategy.CompareExchange for testing.
/// Uses ISingletonService from TestCaseModule to test singleton behavior.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocRegisterFor<TransientService>(ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange, ExplicitOnly = true)]
public sealed partial class ThreadSafeCompareExchangeContainer;

/// <summary>
/// Container with ThreadSafeStrategy.Lock and lazy singletons, so first resolutions go through the
/// synchronization fields, which are allocated on first use.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock, EagerResolveOptions = EagerResolveOptions.None, ExplicitOnly = true)]
public sealed partial class ThreadSafeLazyLockContainer;

/// <summary>
/// Container with ThreadSafeStrategy.SemaphoreSlim and lazy singletons, so first resolutions go through the
/// synchronization fields, which are allocated on first use.
/// </summary>
[IocRegisterFor<SingletonService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
[IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
[IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim, EagerResolveOptions = EagerResolveOptions.None, ExplicitOnly = true)]
public sealed partial class ThreadSafeLazySemaphoreSlimContainer;
