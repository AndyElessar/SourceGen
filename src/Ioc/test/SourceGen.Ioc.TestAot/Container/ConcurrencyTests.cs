namespace SourceGen.Ioc.TestAot.Container;

/// <summary>Concurrent resolution and disposal (Container.Output.spec, "Thread safety", "After disposal, and concurrency").</summary>
public class ConcurrencyTests
{
    private const int Threads = 8;

    /// <summary>
    /// Resolves on <see cref="Threads"/> threads that start at the same time. An exception on a thread would end the
    /// process, so each one is caught and thrown again on the caller.
    /// </summary>
    private static T[] ResolveConcurrently<T>(Func<T> resolve)
    {
        T[] results = new T[Threads];
        List<Exception> errors = [];
        using Barrier barrier = new(Threads);
        Thread[] threads = [.. Enumerable.Range(0, Threads).Select(i => new Thread(() =>
        {
            barrier.SignalAndWait();
            try
            {
                results[i] = resolve();
            }
            catch(Exception e)
            {
                lock(errors)
                {
                    errors.Add(e);
                }
            }
        }))];
        foreach(Thread thread in threads)
        {
            thread.Start();
        }

        foreach(Thread thread in threads)
        {
            thread.Join();
        }

        return errors.Count == 0 ? results : throw new AggregateException(errors);
    }

    [Test]
    public async Task Lock_ConcurrentFirstResolution_CreatesOneInstance()
    {
        using LazyLockContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();

        SlowSingleton[] results = ResolveConcurrently(root.GetRequiredService<SlowSingleton>);

        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
        await Assert.That(journal.SlowCreated).IsEqualTo(1);
    }

    [Test]
    public async Task CompareExchange_ConcurrentFirstResolution_KeepsOneAndDisposesTheOthers()
    {
        using CompareExchangeContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();

        SlowSingleton[] results = ResolveConcurrently(root.GetRequiredService<SlowSingleton>);

        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
        await Assert.That(journal.Disposed.Count).IsEqualTo(journal.SlowCreated - 1);
        await Assert.That(journal.Disposed).DoesNotContain($"SlowSingleton#{results[0].Id}");
    }

    [Test]
    public async Task None_ResolvedSequentially_KeepsTheFirstInstance()
    {
        using NoLockContainer root = new();
        using NoLockContainer scope = root.CreateScope();

        await Assert.That(root.GetRequiredService<SlowSingleton>()).IsSameReferenceAs(scope.GetRequiredService<SlowSingleton>());
        await Assert.That(scope.GetRequiredService<Session>()).IsSameReferenceAs(scope.GetRequiredService<Session>());
    }

    [Test]
    [Arguments(ThreadSafeStrategy.Lock)]
    [Arguments(ThreadSafeStrategy.CompareExchange)]
    public async Task Dispose_DuringConcurrentResolution_DisposesEachInstanceOneTime(ThreadSafeStrategy strategy)
    {
        for(int round = 0; round < 50; round++)
        {
            IServiceProvider root = strategy == ThreadSafeStrategy.Lock ? new LazyLockContainer() : new CompareExchangeContainer();
            Journal journal = root.GetRequiredService<Journal>();
            using Barrier barrier = new(Threads + 1);
            List<Exception> unexpected = [];
            Thread[] threads = [.. Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                try
                {
                    _ = root.GetRequiredService<SlowSingleton>().Id;
                }
                catch(ObjectDisposedException)
                {
                }
                catch(Exception e)
                {
                    lock(unexpected)
                    {
                        unexpected.Add(e);
                    }
                }
            }))];
            foreach(Thread thread in threads)
            {
                thread.Start();
            }

            barrier.SignalAndWait();
            ((IDisposable)root).Dispose();
            foreach(Thread thread in threads)
            {
                thread.Join();
            }

            // Each created instance is disposed exactly one time: by the disposal, by the resolution that lost, or as
            // a losing instance of CompareExchange
            string[] slow = [.. journal.Disposed.Where(static d => d.StartsWith("SlowSingleton#", StringComparison.Ordinal))];
            await Assert.That(unexpected).IsEmpty();
            await Assert.That(slow.Length).IsEqualTo(journal.SlowCreated);
            await Assert.That(slow.Distinct().Count()).IsEqualTo(slow.Length);
        }
    }
}
