using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace SourceGen.Ioc;

/// <summary>
/// Holds the initialization task of an async-init service registered in an <c>IServiceCollection</c> as
/// <see cref="Task{TResult}"/>, so that the service provider disposes the service with the holder.
/// </summary>
/// <typeparam name="T">The type of the service the task creates.</typeparam>
/// <typeparam name="TImplementation">
/// The implementation type of the registration. It only identifies the registration, so that registrations of the same
/// <typeparamref name="T"/> and key have holders of their own.
/// </typeparam>
/// <remarks>
/// <para>
/// Generated <c>IServiceCollection</c> registrations register the holder with the service's lifetime and resolve
/// <see cref="Task{TResult}"/> from it, because the service provider tracks the object a factory returns (the task),
/// not the service the task produces.
/// </para>
/// <para>
/// Disposing the holder disposes the service only when the task has completed successfully; a pending, faulted or
/// canceled task is not awaited. An exception thrown while disposing the service is passed to
/// <see cref="IocContainerGlobalOptions.OnDisposeException"/> instead of being rethrown. <see cref="Dispose"/> cannot
/// dispose a service that implements only <see cref="IAsyncDisposable"/>: it passes an
/// <see cref="InvalidOperationException"/> to <see cref="IocContainerGlobalOptions.OnDisposeException"/>.
/// </para>
/// </remarks>
/// <param name="task">The initialization task of the service.</param>
[Experimental("SGIOCEXP001", Message = "This type is used by SourceGen.Ioc and may change or be removed in future versions.")]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AsyncInitHolder<T, TImplementation>(Task<T> task) : IDisposable, IAsyncDisposable
{
    private int disposed;

    /// <summary>
    /// Gets the initialization task of the service.
    /// </summary>
    public Task<T> Task { get; } = task;

    /// <inheritdoc/>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0 || !Task.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            switch(Task.Result)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case IAsyncDisposable service:
                    throw new InvalidOperationException($"'{service.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
            }
        }
        catch(Exception ex)
        {
            IocContainerGlobalOptions.OnDisposeException?.Invoke(ex);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0
            || !Task.IsCompletedSuccessfully
            || Task.Result is not (IAsyncDisposable or IDisposable))
        {
            return default;
        }

        return DisposeCoreAsync(Task.Result);

        static async ValueTask DisposeCoreAsync(object service)
        {
            try
            {
                if(service is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    ((IDisposable)service).Dispose();
                }
            }
            catch(Exception ex)
            {
                IocContainerGlobalOptions.OnDisposeException?.Invoke(ex);
            }
        }
    }
}
