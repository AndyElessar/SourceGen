#pragma warning disable SGIOCEXP001 // The test uses the types that are only for generated code

namespace SourceGen.Ioc.Test.Library;

/// <summary>
/// The disposal rules of the holders (Register.Output.spec, "Holder" and "Disposal of OwnTask").
/// </summary>
[NotInParallel(nameof(IocContainerGlobalOptions))]
public class IocHolderTests
{
    private sealed class Log
    {
        private readonly List<string> entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock(entries)
                {
                    return [.. entries];
                }
            }
        }

        public void Add(string entry)
        {
            lock(entries)
            {
                entries.Add(entry);
            }
        }
    }

    private sealed class SyncDisposable(string name, Log log, Exception? error = null) : IDisposable
    {
        public void Dispose()
        {
            log.Add($"{name}.Dispose");
            if(error is not null)
            {
                throw error;
            }
        }
    }

    private sealed class AsyncOnlyDisposable(string name, Log log, Exception? error = null) : IAsyncDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            log.Add($"{name}.DisposeAsync");
            Disposed.TrySetResult();
            if(error is not null)
            {
                throw error;
            }
        }
    }

    private sealed class BothDisposable(string name, Log log) : IDisposable, IAsyncDisposable
    {
        public void Dispose() => log.Add($"{name}.Dispose");

        public ValueTask DisposeAsync()
        {
            log.Add($"{name}.DisposeAsync");
            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task Own_Dispose_DisposesValue()
    {
        Log log = new();
        IocHolder<SyncDisposable> holder = IocHolder.Own(new SyncDisposable("a", log));

        holder.Dispose();

        await Assert.That(log.Entries).IsEquivalentTo(["a.Dispose"]);
    }

    [Test]
    public async Task Chain_Dispose_DisposesOnlyInnerInSequence()
    {
        Log log = new();
        IocHolder<SyncDisposable> holder = IocHolder.Chain(new SyncDisposable("outer", log), new SyncDisposable("middle", log), new SyncDisposable("impl", log));

        holder.Dispose();

        await Assert.That(log.Entries).IsEquivalentTo(["middle.Dispose", "impl.Dispose"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Dispose_SecondCall_HasNoEffect()
    {
        Log log = new();
        IocHolder<SyncDisposable> holder = IocHolder.Own(new SyncDisposable("a", log));

        holder.Dispose();
        holder.Dispose();
        await holder.DisposeAsync();

        await Assert.That(log.Entries).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_OneObjectThrows_DisposesOthersAndThrowsSameException()
    {
        Log log = new();
        InvalidCastException error = new("first");
        IocHolder<object> holder = IocHolder.Chain<object>(new object(), new SyncDisposable("a", log, error), new SyncDisposable("b", log));

        InvalidCastException? thrown = await Assert.That(holder.Dispose).Throws<InvalidCastException>();

        await Assert.That(thrown).IsSameReferenceAs(error);
        await Assert.That(log.Entries).IsEquivalentTo(["a.Dispose", "b.Dispose"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task DisposeAsync_TwoObjectsThrow_ThrowsAggregateException()
    {
        Log log = new();
        InvalidCastException first = new("first");
        FormatException second = new("second");
        IocHolder<object> holder = IocHolder.Chain<object>(new object(), new SyncDisposable("a", log, first), new AsyncOnlyDisposable("b", log, second));

        AggregateException? thrown = await Assert.That(async () => await holder.DisposeAsync()).Throws<AggregateException>();

        Exception[] expected = [first, second];
        await Assert.That(thrown!.InnerExceptions).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Dispose_AsyncOnlyObject_ThrowsInvalidOperationAfterDisposingOthers()
    {
        Log log = new();
        IocHolder<object> holder = IocHolder.Chain<object>(new object(), new AsyncOnlyDisposable("a", log), new SyncDisposable("b", log));

        InvalidOperationException? thrown = await Assert.That(holder.Dispose).Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("only implements IAsyncDisposable");
        await Assert.That(log.Entries).IsEquivalentTo(["b.Dispose"]);
    }

    [Test]
    public async Task DisposeAsync_BothInterfaces_UsesDisposeAsyncOnly()
    {
        Log log = new();
        IocHolder<BothDisposable> holder = IocHolder.Own(new BothDisposable("a", log));

        await holder.DisposeAsync();

        await Assert.That(log.Entries).IsEquivalentTo(["a.DisposeAsync"]);
    }

    [Test]
    public async Task OwnTask_CompletedTask_DisposesResult()
    {
        Log log = new();
        IocHolder<Task<SyncDisposable>> holder = IocHolder.OwnTask(Task.FromResult(new SyncDisposable("a", log)));

        holder.Dispose();

        await Assert.That(log.Entries).IsEquivalentTo(["a.Dispose"]);
    }

    [Test]
    public async Task OwnTask_FaultedOrCanceledTask_SkipsWithoutThrowing()
    {
        IocHolder<Task<SyncDisposable>> faulted = IocHolder.OwnTask(Task.FromException<SyncDisposable>(new FormatException()));
        IocHolder<Task<SyncDisposable>> canceled = IocHolder.OwnTask(Task.FromCanceled<SyncDisposable>(new CancellationToken(canceled: true)));

        faulted.Dispose();
        await canceled.DisposeAsync();

        await Assert.That(faulted.Value.IsFaulted).IsTrue();
    }

    [Test]
    public async Task OwnTask_DisposeAsyncOnIncompleteTask_WaitsAndDisposesResult()
    {
        Log log = new();
        TaskCompletionSource<SyncDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IocHolder<Task<SyncDisposable>> holder = IocHolder.OwnTask(creation.Task);

        Task disposal = holder.DisposeAsync().AsTask();
        await Assert.That(disposal.IsCompleted).IsFalse();
        creation.SetResult(new SyncDisposable("a", log));
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(log.Entries).IsEquivalentTo(["a.Dispose"]);
    }

    [Test]
    public async Task OwnTask_DisposeAsyncOnTaskThatFailsLater_DoesNotThrow()
    {
        TaskCompletionSource<SyncDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IocHolder<Task<SyncDisposable>> holder = IocHolder.OwnTask(creation.Task);

        Task disposal = holder.DisposeAsync().AsTask();
        creation.SetException(new FormatException());
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(disposal.IsCompletedSuccessfully).IsTrue();
    }

    [Test]
    public async Task OwnTask_DisposeOnIncompleteTask_ReturnsAndDisposesResultWithDisposeAsyncLater()
    {
        Log log = new();
        TaskCompletionSource<AsyncOnlyDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IocHolder<Task<AsyncOnlyDisposable>> holder = IocHolder.OwnTask(creation.Task);

        holder.Dispose();
        await Assert.That(log.Entries).IsEmpty();
        AsyncOnlyDisposable result = new("a", log);
        creation.SetResult(result);
        await result.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(log.Entries).IsEquivalentTo(["a.DisposeAsync"]);
    }

    [Test]
    public async Task OwnTask_DisposeOnIncompleteTaskAndLaterDisposalThrows_ReportsToCallback()
    {
        // Accept only this test's exception: a continuation of an earlier test can still report to the global callback
        FormatException error = new("dispose failed");
        TaskCompletionSource<Exception> reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = e =>
        {
            if(ReferenceEquals(e, error))
            {
                reported.TrySetResult(e);
            }
        };
        try
        {
            TaskCompletionSource<AsyncOnlyDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IocHolder<Task<AsyncOnlyDisposable>> holder = IocHolder.OwnTask(creation.Task);

            holder.Dispose();
            creation.SetResult(new AsyncOnlyDisposable("a", new Log(), error));

            Exception actual = await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(actual).IsSameReferenceAs(error);
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }
    }

    [Test]
    public async Task OwnTask_DisposeOnIncompleteChainAndTwoLayersThrow_ReportsEachExceptionSeparately()
    {
        // Accept only this test's exception: a continuation of an earlier test can still report to the global callback
        FormatException first = new("outer");
        InvalidCastException second = new("middle");
        List<Exception> reported = [];
        TaskCompletionSource allReported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = e =>
        {
            if(!ReferenceEquals(e, first) && !ReferenceEquals(e, second) && !(e is AggregateException a && a.InnerExceptions.Contains(first)))
            {
                return;
            }

            lock(reported)
            {
                reported.Add(e);
                if(reported.Count == 2)
                {
                    allReported.TrySetResult();
                }
            }
        };
        try
        {
            Log log = new();
            SyncDisposable outer = new("outer", log, first);
            TaskCompletionSource<IocHolder<SyncDisposable>> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IocHolder<Task<IocHolder<SyncDisposable>>> owner = IocHolder.OwnTask(creation.Task);

            owner.Dispose();
            creation.SetResult(IocHolder.Chain(outer, outer, new SyncDisposable("middle", log, second)));
            await allReported.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Exception[] expected = [first, second];
            await Assert.That(reported).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }
    }

    [Test]
    public async Task OwnTask_ContinuationAndResultThrowsOwnAggregate_ReportsItUnchanged()
    {
        // Accept only this test's exception: a continuation of an earlier test can still report to the global callback
        AggregateException error = new(new FormatException("a"), new FormatException("b"));
        TaskCompletionSource<Exception> reported = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = e =>
        {
            if(ReferenceEquals(e, error) || ReferenceEquals(e, error.InnerExceptions[0]) || ReferenceEquals(e, error.InnerExceptions[1]))
            {
                reported.TrySetResult(e);
            }
        };
        try
        {
            TaskCompletionSource<AsyncOnlyDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IocHolder<Task<AsyncOnlyDisposable>> holder = IocHolder.OwnTask(creation.Task);

            holder.Dispose();
            creation.SetResult(new AsyncOnlyDisposable("a", new Log(), error));
            Exception actual = await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.That(actual).IsSameReferenceAs(error);
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }
    }

    [Test]
    public async Task OwnTask_DisposeAsyncAndResultThrows_Throws()
    {
        FormatException error = new("dispose failed");
        IocHolder<Task<AsyncOnlyDisposable>> holder = IocHolder.OwnTask(Task.FromResult(new AsyncOnlyDisposable("a", new Log(), error)));

        FormatException? thrown = await Assert.That(async () => await holder.DisposeAsync()).Throws<FormatException>();

        await Assert.That(thrown).IsSameReferenceAs(error);
    }

    [Test]
    public async Task OwnTask_DisposeOnCompletedTaskAndResultThrows_ThrowsSameExceptionAndDoesNotCallCallback()
    {
        // Count only this test's exception: a continuation of an earlier test can still report to the global callback
        int calls = 0;
        FormatException error = new("dispose failed");
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = e =>
        {
            if(ReferenceEquals(e, error))
            {
                Interlocked.Increment(ref calls);
            }
        };
        try
        {
            IocHolder<Task<SyncDisposable>> holder = IocHolder.OwnTask(Task.FromResult(new SyncDisposable("a", new Log(), error)));

            FormatException? thrown = await Assert.That(holder.Dispose).Throws<FormatException>();

            await Assert.That(thrown).IsSameReferenceAs(error);
            await Assert.That(calls).IsEqualTo(0);
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }
    }

    [Test]
    public async Task OwnTask_DisposeOnCompletedAsyncOnlyResult_ThrowsInvalidOperation()
    {
        Log log = new();
        IocHolder<Task<AsyncOnlyDisposable>> holder = IocHolder.OwnTask(Task.FromResult(new AsyncOnlyDisposable("a", log)));

        InvalidOperationException? thrown = await Assert.That(holder.Dispose).Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("only implements IAsyncDisposable");
        await Assert.That(log.Entries).IsEmpty();
    }

    [Test]
    public async Task OwnTask_DisposeOnTaskThatFaultsLater_ReportsNothing()
    {
        // Count only this test's exception: a continuation of an earlier test can still report to the global callback
        int calls = 0;
        FormatException fault = new("creation failed");
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = e =>
        {
            if(ReferenceEquals(e, fault) || ReferenceEquals(e.InnerException, fault))
            {
                Interlocked.Increment(ref calls);
            }
        };
        try
        {
            TaskCompletionSource<SyncDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IocHolder<Task<SyncDisposable>> holder = IocHolder.OwnTask(creation.Task);

            holder.Dispose();
            creation.SetException(fault);
            await Task.WhenAny(creation.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            await Task.Delay(100);

            await Assert.That(calls).IsEqualTo(0);
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }
    }

    [Test]
    public async Task OwnTask_ContinuationThrowsWithoutCallback_DiscardsException()
    {
        Action<Exception>? previous = IocContainerGlobalOptions.OnDisposeException;
        IocContainerGlobalOptions.OnDisposeException = null;
        try
        {
            TaskCompletionSource<AsyncOnlyDisposable> creation = new(TaskCreationOptions.RunContinuationsAsynchronously);
            IocHolder<Task<AsyncOnlyDisposable>> holder = IocHolder.OwnTask(creation.Task);
            AsyncOnlyDisposable result = new("a", new Log(), new FormatException());

            holder.Dispose();
            creation.SetResult(result);
            await result.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await Assert.That(result.Disposed.Task.IsCompletedSuccessfully).IsTrue();
        }
        finally
        {
            IocContainerGlobalOptions.OnDisposeException = previous;
        }
    }

    [Test]
    public async Task Dispose_TwoObjectsThrow_ThrowsAggregateExceptionInSequence()
    {
        Log log = new();
        InvalidCastException first = new("first");
        FormatException second = new("second");
        IocHolder<object> holder = IocHolder.Chain<object>(new object(), new SyncDisposable("a", log, first), new SyncDisposable("b", log, second));

        AggregateException? thrown = await Assert.That(holder.Dispose).Throws<AggregateException>();

        Exception[] expected = [first, second];
        await Assert.That(thrown!.InnerExceptions).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task DisposeAsync_SyncOnlyObject_UsesDispose()
    {
        Log log = new();
        IocHolder<SyncDisposable> holder = IocHolder.Own(new SyncDisposable("a", log));

        await holder.DisposeAsync();

        await Assert.That(log.Entries).IsEquivalentTo(["a.Dispose"]);
    }

    [Test]
    public async Task OwnTask_AsynchronousChain_DisposesChainOutermostLayerFirst()
    {
        Log log = new();
        SyncDisposable outer = new("outer", log);
        IocHolder<SyncDisposable> chain = IocHolder.Chain(outer, outer, new SyncDisposable("middle", log));
        IocHolder<Task<IocHolder<SyncDisposable>>> owner = IocHolder.OwnTask(Task.FromResult(chain));

        await owner.DisposeAsync();

        await Assert.That(log.Entries).IsEquivalentTo(["outer.Dispose", "middle.Dispose"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task OwnTask_DisposeConcurrentWithCompletion_DisposesEachResultExactlyOnce()
    {
        const int Iterations = 2000;
        Log log = new();
        for(int i = 0; i < Iterations; i++)
        {
            TaskCompletionSource<SyncDisposable> creation = new();
            IocHolder<Task<SyncDisposable>> holder = IocHolder.OwnTask(creation.Task);
            SyncDisposable result = new($"r{i}", log);
            using Barrier barrier = new(2);
            Task complete = Task.Run(() =>
            {
                barrier.SignalAndWait();
                creation.SetResult(result);
            });
            Task dispose = Task.Run(() =>
            {
                barrier.SignalAndWait();
                holder.Dispose();
            });
            await Task.WhenAll(complete, dispose);
        }

        for(int attempt = 0; attempt < 100 && log.Entries.Count < Iterations; attempt++)
        {
            await Task.Delay(50);
        }

        IReadOnlyList<string> entries = log.Entries;
        await Assert.That(entries).Count().IsEqualTo(Iterations);
        await Assert.That(entries.Distinct().Count()).IsEqualTo(Iterations);
    }

    [Test]
    public async Task IocTask_Task_ReturnsTheTask()
    {
        Task<string> task = Task.FromResult("x");

        object actual = new IocTask<string>(task).Task;

        await Assert.That(actual).IsSameReferenceAs(task);
    }
}
