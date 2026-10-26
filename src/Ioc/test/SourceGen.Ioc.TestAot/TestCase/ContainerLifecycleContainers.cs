namespace SourceGen.Ioc.TestAot.TestCase;

public interface ILifecycleWrapped;

internal sealed class LifecycleWrapped : ILifecycleWrapped;

/// <summary>Eager singleton that injects Lazy/Func wrapper fields during container construction.</summary>
public sealed class LifecycleWrapperConsumer(Lazy<ILifecycleWrapped> lazy, Func<ILifecycleWrapped> factory)
{
    public Lazy<ILifecycleWrapped> Lazy { get; } = lazy;
    public Func<ILifecycleWrapped> Factory { get; } = factory;
}

public interface ILifecycleEagerDependency;

internal sealed class LifecycleEagerDependency : ILifecycleEagerDependency;

/// <summary>Eager singleton whose factory resolves another eager singleton through <see cref="IServiceProvider"/>.</summary>
public sealed class LifecycleProviderFactoryService(ILifecycleEagerDependency dependency)
{
    public ILifecycleEagerDependency Dependency { get; } = dependency;

    public static LifecycleProviderFactoryService Create(IServiceProvider serviceProvider) =>
        new((ILifecycleEagerDependency)serviceProvider.GetRequiredService(typeof(ILifecycleEagerDependency)));
}

/// <summary>
/// Eager singletons that depend on wrapper fields and on other eager singletons resolved dynamically during construction.
/// </summary>
[IocRegisterFor<LifecycleProviderFactoryService>(ServiceLifetime.Singleton, Factory = nameof(LifecycleProviderFactoryService.Create))]
[IocRegisterFor<LifecycleWrapperConsumer>(ServiceLifetime.Singleton)]
[IocRegisterFor<LifecycleWrapped>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ILifecycleWrapped)])]
[IocRegisterFor<LifecycleEagerDependency>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ILifecycleEagerDependency)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class EagerInitOrderContainer;

public interface ILifecycleLazySingleton
{
    int DisposeCount { get; }
}

internal sealed class LifecycleLazySingleton : ILifecycleLazySingleton, IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

/// <summary>Lazily created singleton that may be first requested from a scope.</summary>
[IocRegisterFor<LifecycleLazySingleton>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ILifecycleLazySingleton)])]
[IocContainer(ExplicitOnly = true, EagerResolveOptions = EagerResolveOptions.None)]
public sealed partial class LazySingletonScopeContainer;

public interface ILifecycleScopedDisposable
{
    bool IsDisposed { get; }
}

internal sealed class LifecycleScopedDisposable : ILifecycleScopedDisposable, IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

public interface ILifecycleAsyncOnlyDisposable
{
    bool IsDisposed { get; }
}

internal sealed class LifecycleAsyncOnlyDisposable : ILifecycleAsyncOnlyDisposable, IAsyncDisposable
{
    public bool IsDisposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Scoped service that may be resolved directly from the root container.</summary>
[IocRegisterFor<LifecycleScopedDisposable>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ILifecycleScopedDisposable)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class RootScopedContainer;

/// <summary>
/// Scoped service that only implements <see cref="IAsyncDisposable"/>. Scoped (not singleton) because
/// [IocRegisterFor] registrations are also visible to other containers in this assembly.
/// </summary>
[IocRegisterFor<LifecycleAsyncOnlyDisposable>(ServiceLifetime.Scoped, ServiceTypes = [typeof(ILifecycleAsyncOnlyDisposable)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class AsyncOnlyDisposableContainer;

/// <summary>Service registered only in the fallback <see cref="IServiceCollection"/>.</summary>
public sealed class LifecycleExternalService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

/// <summary>Container scoped service that depends on a fallback-provider service.</summary>
public sealed class LifecycleFallbackConsumer(LifecycleExternalService external)
{
    public LifecycleExternalService External { get; } = external;
}

/// <summary>Container integrated with an external <see cref="IServiceCollection"/>.</summary>
[IocRegisterFor<LifecycleFallbackConsumer>(ServiceLifetime.Scoped)]
[IocContainer(ExplicitOnly = true)]
public sealed partial class FallbackScopeContainer;
