namespace SourceGen.Ioc.TestCase;

/// <summary>Async-init service that records its disposal, registered with each lifetime under its own key.</summary>
public interface IAsyncInitDisposable
{
    bool IsInitialized { get; }
    bool IsDisposed { get; }
}

public sealed class AsyncInitDisposable : IAsyncInitDisposable, IDisposable
{
    public bool IsInitialized { get; private set; }
    public bool IsDisposed { get; private set; }

    [IocInject]
    public async Task InitializeAsync()
    {
        await Task.Yield();
        IsInitialized = true;
    }

    public void Dispose() => IsDisposed = true;
}

/// <summary>Async-init service that implements only <see cref="IAsyncDisposable"/>.</summary>
public interface IAsyncInitAsyncDisposable
{
    bool IsDisposed { get; }
}

public sealed class AsyncInitAsyncDisposable : IAsyncInitAsyncDisposable, IAsyncDisposable
{
    public bool IsDisposed { get; private set; }

    [IocInject]
    public async Task InitializeAsync() => await Task.Yield();

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return default;
    }
}

/// <summary>Disposable async-init service wrapped by a disposable synchronous decorator.</summary>
public interface IDecoratedAsyncInitDisposable
{
    IDecoratedAsyncInitDisposable? Inner { get; }
    bool IsDisposed { get; }

    /// <summary>Whether the wrapped service was already disposed when this instance was disposed.</summary>
    bool InnerWasDisposedFirst { get; }
}

public sealed class DecoratedAsyncInitDisposable : IDecoratedAsyncInitDisposable, IDisposable
{
    public IDecoratedAsyncInitDisposable? Inner => null;
    public bool IsDisposed { get; private set; }
    public bool InnerWasDisposedFirst => false;

    [IocInject]
    public async Task InitializeAsync() => await Task.Yield();

    public void Dispose() => IsDisposed = true;
}

public sealed class DisposableDecoratorOfAsyncInit(IDecoratedAsyncInitDisposable inner) : IDecoratedAsyncInitDisposable, IDisposable
{
    public IDecoratedAsyncInitDisposable? Inner => inner;
    public bool IsDisposed { get; private set; }
    public bool InnerWasDisposedFirst { get; private set; }

    public void Dispose()
    {
        InnerWasDisposedFirst = inner.IsDisposed;
        IsDisposed = true;
    }
}

public sealed class TransientDisposableAsyncDecoratedService : IDisposableAsyncDecoratedService
{
    public bool IsDisposed => false;
}

/// <summary>Service type implemented by two async-init decorated registrations under the same key.</summary>
public interface ISharedKeyAsyncInitService
{
    string Name { get; }
}

public sealed class FirstSharedKeyAsyncInitService : ISharedKeyAsyncInitService
{
    public string Name => "First";
}

public sealed class SecondSharedKeyAsyncInitService : ISharedKeyAsyncInitService
{
    public string Name => "Second";
}

public sealed class SharedKeyAsyncInitDecorator(ISharedKeyAsyncInitService inner) : ISharedKeyAsyncInitService
{
    public string Name => inner.Name;

    [IocInject]
    public async Task InitializeAsync() => await Task.Yield();
}

[IocRegisterFor<FirstSharedKeyAsyncInitService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISharedKeyAsyncInitService)], Key = "shared", Decorators = [typeof(SharedKeyAsyncInitDecorator)])]
[IocRegisterFor<SecondSharedKeyAsyncInitService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(ISharedKeyAsyncInitService)], Key = "shared", Decorators = [typeof(SharedKeyAsyncInitDecorator)])]
[IocRegisterFor<AsyncInitDisposable>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IAsyncInitDisposable)])]
[IocRegisterFor<AsyncInitDisposable>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IAsyncInitDisposable)], Key = "scoped")]
[IocRegisterFor<AsyncInitDisposable>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncInitDisposable)], Key = "singleton")]
[IocRegisterFor<AsyncInitDisposable>(ServiceLifetime.Transient, ServiceTypes = [typeof(IAsyncInitDisposable)], Key = "transient")]
[IocRegisterFor<AsyncInitAsyncDisposable>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncInitAsyncDisposable)])]
[IocRegisterFor<DecoratedAsyncInitDisposable>(
    ServiceLifetime.Scoped,
    ServiceTypes = [typeof(IDecoratedAsyncInitDisposable)],
    Decorators = [typeof(DisposableDecoratorOfAsyncInit)])]
// Transient counterpart of the singleton and scoped async-init decorator registrations in Decorator.cs
[IocRegisterFor<TransientDisposableAsyncDecoratedService>(
    ServiceLifetime.Transient,
    ServiceTypes = [typeof(IDisposableAsyncDecoratedService)],
    Key = "transient",
    Decorators = [typeof(DisposableAsyncInitDecorator)])]
internal static class AsyncInitDisposalRegistrations;
