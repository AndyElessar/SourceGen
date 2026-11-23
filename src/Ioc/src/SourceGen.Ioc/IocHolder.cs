using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace SourceGen.Ioc;

/// <summary>Creates the holders of <c>Add{Name}</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001", Message = "This type is only for the code that SourceGen.Ioc generates. Do not use it directly.")]
public static class IocHolder
{
    /// <summary>Creates a holder that disposes its value.</summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value. The holder disposes it.</param>
    /// <returns>The holder.</returns>
    public static IocHolder<T> Own<T>(T value) => new(value, [value]);

    /// <summary>
    /// Creates the holder of a decorator chain. It does not dispose its value. It disposes only <paramref name="inner"/>.
    /// </summary>
    /// <typeparam name="T">The service type of the chain.</typeparam>
    /// <param name="value">The outermost layer.</param>
    /// <param name="inner">
    /// The objects to dispose, in sequence: the middle layers, outer layers first, and for a transient chain the
    /// implementation instance last.
    /// </param>
    /// <returns>The holder.</returns>
    public static IocHolder<T> Chain<T>(T value, params ReadOnlySpan<object> inner) => new(value, inner.ToArray());

    /// <summary>
    /// Creates the holder of the task of an asynchronous service. On disposal it disposes the result of the task when the
    /// task completed successfully, and skips a faulted or canceled task. For a task that is not complete,
    /// <c>DisposeAsync</c> waits for it and ignores its exception, and <c>Dispose</c> attaches a continuation that disposes
    /// the result later and gives its exception to <see cref="IocContainerGlobalOptions.OnDisposeException"/>.
    /// </summary>
    /// <typeparam name="T">The type of the instance.</typeparam>
    /// <param name="task">The task that creates the instance.</param>
    /// <returns>The holder.</returns>
    public static IocHolder<Task<T>> OwnTask<T>(Task<T> task) => new(task, [new IocTaskOwner<T>(task)]);
}

/// <summary>
/// Holds an instance for <c>Add{Name}</c> and disposes the objects that it owns. It is the only type that is responsible
/// for disposal in <c>Add{Name}</c>.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
[Experimental("SGIOCEXP001", Message = "This type is only for the code that SourceGen.Ioc generates. Do not use it directly.")]
public sealed class IocHolder<T> : IDisposable, IAsyncDisposable
{
    private readonly object?[] owned;
    private int disposed;

    /// <summary>Initializes a new instance of the <see cref="IocHolder{T}"/> class.</summary>
    /// <param name="value">The value.</param>
    /// <param name="owned">The objects to dispose, in sequence.</param>
    internal IocHolder(T value, object?[] owned)
    {
        Value = value;
        this.owned = owned;
    }

    /// <summary>Gets the value.</summary>
    public T Value { get; }

    /// <summary>
    /// Disposes the owned objects in sequence. When one throws, it continues and throws after all are done: one exception
    /// unchanged, more than one in an <see cref="AggregateException"/>. An object that implements only
    /// <see cref="IAsyncDisposable"/> is not disposed and counts as an <see cref="InvalidOperationException"/>.
    /// A second call has no effect.
    /// </summary>
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? errors = null;
        foreach(object? item in owned)
        {
            IocDisposal.Dispose(item, ref errors);
        }

        IocDisposal.ThrowIfAny(errors);
    }

    /// <summary>
    /// Disposes the owned objects in sequence, with <see cref="IAsyncDisposable"/> when an object implements it. The
    /// exceptions follow the rules of <see cref="Dispose"/>. A second call has no effect.
    /// </summary>
    /// <returns>The disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        List<Exception>? errors = null;
        foreach(object? item in owned)
        {
            errors = await IocDisposal.DisposeAsync(item, errors).ConfigureAwait(false);
        }

        IocDisposal.ThrowIfAny(errors);
    }
}
