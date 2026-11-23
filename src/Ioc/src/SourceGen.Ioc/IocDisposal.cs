using System.Runtime.ExceptionServices;

namespace SourceGen.Ioc;

/// <summary>The disposal rules that the holders share.</summary>
internal static class IocDisposal
{
    /// <summary>Disposes an object synchronously.</summary>
    /// <param name="item">The object. An object that implements only <see cref="IAsyncDisposable"/> is not disposed and gives an <see cref="InvalidOperationException"/>.</param>
    /// <param name="errors">The list that receives the exception, created when it is needed.</param>
    public static void Dispose(object? item, ref List<Exception>? errors)
    {
        try
        {
            switch(item)
            {
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
                case IAsyncDisposable:
                    throw new InvalidOperationException(
                        $"'{item.GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
            }
        }
        catch(Exception exception)
        {
            (errors ??= []).Add(exception);
        }
    }

    /// <summary>Disposes an object, with <see cref="IAsyncDisposable"/> when it implements it.</summary>
    /// <param name="item">The object.</param>
    /// <param name="errors">The list that receives the exception, or <see langword="null"/>.</param>
    /// <returns><paramref name="errors"/>, created when an exception needs it.</returns>
    public static async ValueTask<List<Exception>?> DisposeAsync(object? item, List<Exception>? errors)
    {
        try
        {
            switch(item)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch(Exception exception)
        {
            (errors ??= []).Add(exception);
        }

        return errors;
    }

    /// <summary>Throws one exception unchanged, or more than one in an <see cref="AggregateException"/>.</summary>
    /// <param name="errors">The exceptions, or <see langword="null"/>.</param>
    public static void ThrowIfAny(List<Exception>? errors)
    {
        switch(errors)
        {
            case null or []:
                return;
            case [Exception single]:
                ExceptionDispatchInfo.Throw(single);
                return;
            default:
                throw new IocDisposalAggregateException(errors);
        }
    }

    /// <summary>Gives an exception that no caller can receive to <see cref="IocContainerGlobalOptions.OnDisposeException"/>.</summary>
    /// <param name="exception">The exception.</param>
    public static void Report(Exception exception) => IocContainerGlobalOptions.OnDisposeException?.Invoke(exception);
}

/// <summary>
/// The exceptions of several objects that one disposal collects. It marks an aggregate that a holder built, so that the
/// callback can receive each exception separately, and an <see cref="AggregateException"/> of user code stays intact.
/// </summary>
/// <param name="errors">The exceptions, in the order of disposal.</param>
internal sealed class IocDisposalAggregateException(IEnumerable<Exception> errors) : AggregateException(errors);

/// <summary>Disposes the result of the task of an asynchronous service. <see cref="IocHolder.OwnTask{T}"/> owns one.</summary>
/// <typeparam name="T">The type of the instance.</typeparam>
/// <param name="task">The task that creates the instance.</param>
internal sealed class IocTaskOwner<T>(Task<T> task) : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Disposes the result when the task completed successfully. For a task that is not complete, attaches a continuation
    /// and returns at once.
    /// </summary>
    public void Dispose()
    {
        // Check completion one time, first. The task can complete on another thread at any moment, but after IsCompleted
        // is true its state does not change. Two separate checks could both miss a task that completes between them.
        if(!task.IsCompleted)
        {
            _ = DisposeLaterAsync(task);
            return;
        }

        if(task.IsCompletedSuccessfully)
        {
            List<Exception>? errors = null;
            IocDisposal.Dispose(task.Result, ref errors);
            IocDisposal.ThrowIfAny(errors);
        }
    }

    /// <summary>Waits for the task, ignores its exception, and disposes the result when the task completed successfully.</summary>
    /// <returns>The disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if(!task.IsCompleted)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // The creation failed. It disposed the objects that it created, so there is nothing to dispose
            }
        }

        if(task.IsCompletedSuccessfully)
        {
            IocDisposal.ThrowIfAny(await IocDisposal.DisposeAsync(task.Result, null).ConfigureAwait(false));
        }
    }

    /// <summary>
    /// Disposes the result after the task completes successfully. <c>Dispose</c> returned long before, so the exception
    /// goes to the callback. It uses the rules of <c>DisposeAsync</c>, so a result that implements only
    /// <see cref="IAsyncDisposable"/> is disposed too.
    /// </summary>
    /// <param name="task">The task that creates the instance.</param>
    /// <returns>The disposal. Nobody waits for it.</returns>
    private static async Task DisposeLaterAsync(Task<T> task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        // A callback that throws gets no special handling (Container.Output.spec, "Dispose-exception callback"): this task
        // then faults, nobody observes it, and the exception reaches TaskScheduler.UnobservedTaskException.
        if(await IocDisposal.DisposeAsync(task.Result, null).ConfigureAwait(false) is { } errors)
        {
            foreach(Exception exception in errors)
            {
                // The holder of a decorator chain collects the exceptions of several layers in one aggregate. Each one goes
                // to the callback separately. An AggregateException that user code throws stays as it is.
                if(exception is IocDisposalAggregateException aggregate)
                {
                    foreach(Exception inner in aggregate.InnerExceptions)
                    {
                        IocDisposal.Report(inner);
                    }
                }
                else
                {
                    IocDisposal.Report(exception);
                }
            }
        }
    }
}
