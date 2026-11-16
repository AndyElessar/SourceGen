#pragma warning disable SGIOCEXP001

namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for <see cref="AsyncInitHolder{T, TImplementation}"/>, which disposes async-init services registered by the Register output.
/// Its failure handling matches the Container output's disposal of async-init services.
/// </summary>
[NotInParallel(nameof(IocContainerGlobalOptions.OnDisposeException))]
public sealed class AsyncInitHolderTests
{
    [Test]
    public async Task AsyncInitHolder_PendingTask_DoesNotDisposeLaterResult()
    {
        var completion = new TaskCompletionSource<AsyncInitDisposable>();
        var holder = new AsyncInitHolder<AsyncInitDisposable, AsyncInitDisposable>(completion.Task);
        var service = new AsyncInitDisposable();

        holder.Dispose();
        await holder.DisposeAsync();
        completion.SetResult(service);

        await Assert.That(service.IsDisposed).IsFalse();
    }

    [Test]
    public async Task AsyncInitHolder_FaultedOrCanceledTask_DisposesWithoutThrowing()
    {
        var exceptions = new List<Exception>();
        var previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = exceptions.Add;
        try
        {
            new AsyncInitHolder<IDisposable, IDisposable>(Task.FromException<IDisposable>(new InvalidOperationException())).Dispose();
            await new AsyncInitHolder<IDisposable, IDisposable>(Task.FromException<IDisposable>(new InvalidOperationException())).DisposeAsync();
            new AsyncInitHolder<IDisposable, IDisposable>(Task.FromCanceled<IDisposable>(new CancellationToken(true))).Dispose();
            await new AsyncInitHolder<IDisposable, IDisposable>(Task.FromCanceled<IDisposable>(new CancellationToken(true))).DisposeAsync();
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }

        await Assert.That(exceptions).IsEmpty();
    }

    [Test]
    public async Task AsyncInitHolder_DisposedTwice_DisposesServiceOnce()
    {
        var service = new CountingDisposable();
        var holder = new AsyncInitHolder<CountingDisposable, CountingDisposable>(Task.FromResult(service));

        holder.Dispose();
        await holder.DisposeAsync();
        holder.Dispose();

        await Assert.That(service.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncInitHolder_DisposeAsync_PrefersDisposeAsync()
    {
        var service = new CountingDisposable();

        await new AsyncInitHolder<CountingDisposable, CountingDisposable>(Task.FromResult(service)).DisposeAsync();

        await Assert.That(service.DisposeAsyncCount).IsEqualTo(1);
        await Assert.That(service.DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncInitHolder_SyncDisposeOfAsyncDisposableOnly_ReportsInvalidOperationException()
    {
        var exceptions = new List<Exception>();
        var previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = exceptions.Add;
        var service = new AsyncInitAsyncDisposable();
        try
        {
            new AsyncInitHolder<AsyncInitAsyncDisposable, AsyncInitAsyncDisposable>(Task.FromResult(service)).Dispose();
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }

        await Assert.That(service.IsDisposed).IsFalse();
        await Assert.That(exceptions).Count().IsEqualTo(1);
        await Assert.That(exceptions[0]).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task AsyncInitHolder_ServiceDisposeThrows_ReportsExceptionWithoutThrowing()
    {
        var exceptions = new List<Exception>();
        var previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = exceptions.Add;
        try
        {
            new AsyncInitHolder<ThrowingDisposable, ThrowingDisposable>(Task.FromResult(new ThrowingDisposable())).Dispose();
            await new AsyncInitHolder<ThrowingDisposable, ThrowingDisposable>(Task.FromResult(new ThrowingDisposable())).DisposeAsync();
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }

        await Assert.That(exceptions).Count().IsEqualTo(2);
        await Assert.That(exceptions.All(static e => e is NotSupportedException)).IsTrue();
    }

    private sealed class CountingDisposable : IDisposable, IAsyncDisposable
    {
        public int DisposeCount { get; private set; }
        public int DisposeAsyncCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public ValueTask DisposeAsync()
        {
            DisposeAsyncCount++;
            return default;
        }
    }

    private sealed class ThrowingDisposable : IDisposable, IAsyncDisposable
    {
        public void Dispose() => throw new NotSupportedException();

        public ValueTask DisposeAsync() => throw new NotSupportedException();
    }
}
