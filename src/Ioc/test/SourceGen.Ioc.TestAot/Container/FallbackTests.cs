namespace SourceGen.Ioc.TestAot.Container;

/// <summary>
/// The container with a fallback provider (Container.Output.spec, "Fallback provider", "Integration with
/// IServiceCollection").
/// </summary>
public class FallbackTests
{
    public sealed class External : IExternal, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    /// <summary>A fallback provider that is not MS.DI and does not supply <c>IServiceScopeFactory</c>.</summary>
    public sealed class PlainProvider(IExternal external) : IServiceProvider, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public object? GetService(Type serviceType) => serviceType == typeof(IExternal) ? external : null;

        public void Dispose() => IsDisposed = true;
    }

    /// <summary>An MS.DI service that implements only <see cref="IAsyncDisposable"/>.</summary>
    public sealed class AsyncExternal : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }

    public sealed class OtherClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.MaxValue;
    }

    public sealed class OnlyInFallback;

    private static ServiceProvider Build(Action<ServiceCollection> configure)
    {
        ServiceCollection services = new();
        configure(services);
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task Constructor_NullFallbackProvider_Throws()
    {
        await Assert.That(() => new FallbackContainer(null!)).ThrowsExactly<ArgumentNullException>();
    }

    [Test]
    public async Task MissingDependency_ResolvedOnTheRoot_ComesFromTheFallbackProvider()
    {
        using ServiceProvider provider = Build(static s => s.AddSingleton<IExternal, External>());
        using FallbackContainer root = new(provider);

        await Assert.That(root.External).IsSameReferenceAs(provider.GetRequiredService<IExternal>());
        await Assert.That(root.OptionalExternal).IsSameReferenceAs(root.External);
    }

    [Test]
    public async Task MissingDependency_ResolvedOnAScope_ComesFromItsFallbackScope()
    {
        using ServiceProvider provider = Build(static s => s.AddScoped<IExternal, External>());
        using FallbackContainer root = new(provider);
        FallbackContainer first = root.CreateScope();
        using FallbackContainer second = root.CreateScope();

        IExternal external = first.External;
        ExternalConsumer consumer = first.GetRequiredService<ExternalConsumer>();

        await Assert.That(consumer.External).IsSameReferenceAs(external);
        await Assert.That(second.External).IsNotSameReferenceAs(external);
        await Assert.That(root.External).IsNotSameReferenceAs(external);

        first.Dispose();

        await Assert.That(((External)external).IsDisposed).IsTrue();
        await Assert.That(((External)second.External).IsDisposed).IsFalse();
    }

    [Test]
    public async Task FallbackScope_ConcurrentFirstUses_LeaveOneFallbackScope()
    {
        using ServiceProvider provider = Build(static s => s.AddScoped<IExternal, External>());
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();
        const int count = 16;
        IExternal[] results = new IExternal[count];
        using Barrier barrier = new(count);

        Thread[] threads = [.. Enumerable.Range(0, count).Select(i => new Thread(() =>
        {
            barrier.SignalAndWait();
            results[i] = scope.External;
        }))];
        foreach(Thread thread in threads)
        {
            thread.Start();
        }

        foreach(Thread thread in threads)
        {
            thread.Join();
        }

        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
        await Assert.That(results[0].IsDisposed).IsFalse();
    }

    [Test]
    public async Task FallbackScope_ProviderWithoutScopeFactory_ScopeUsesTheProviderAndDoesNotDisposeIt()
    {
        External external = new();
        using PlainProvider provider = new(external);
        using FallbackContainer root = new(provider);
        FallbackContainer scope = root.CreateScope();

        IExternal fromScope = scope.External;
        scope.Dispose();

        await Assert.That(fromScope).IsSameReferenceAs(external);
        await Assert.That(root.External).IsSameReferenceAs(external);
        await Assert.That(provider.IsDisposed).IsFalse();
        await Assert.That(external.IsDisposed).IsFalse();
    }

    [Test]
    public async Task FallbackScope_ScopeDisposed_DisposesTheContainerServicesFirst()
    {
        using ServiceProvider provider = Build(static s => s.AddScoped<IExternal, External>());
        using FallbackContainer root = new(provider);
        FallbackContainer scope = root.CreateScope();
        ExternalConsumer consumer = scope.GetRequiredService<ExternalConsumer>();

        await scope.DisposeAsync();

        await Assert.That(consumer.ExternalDisposedBefore).IsFalse();
        await Assert.That(consumer.External!.IsDisposed).IsTrue();
    }

    [Test]
    public async Task MissingDependency_FallbackReturnsNull_RequiredThrowsAndOptionalGetsNull()
    {
        using ServiceProvider provider = Build(static _ => { });
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();

        await Assert.That(() => root.External).ThrowsExactly<InvalidOperationException>();
        await Assert.That(root.OptionalExternal).IsNull();
        await Assert.That(scope.GetRequiredService<ExternalConsumer>().External).IsNull();
    }

    [Test]
    public async Task Lookup_RegisteredInBoth_ContainerWinsAndCollectionsMergeFallbackFirst()
    {
        OtherClock other = new();
        using ServiceProvider provider = Build(s => s.AddSingleton<IClock>(other));
        using FallbackContainer root = new(provider);

        IClock[] clocks = [.. root.GetServices<IClock>()];

        await Assert.That(root.GetRequiredService<IClock>()).IsSameReferenceAs(root.Clock);
        await Assert.That(root.Clock).IsTypeOf<SystemClock>();
        await Assert.That(clocks.Length).IsEqualTo(2);
        await Assert.That(clocks[0]).IsSameReferenceAs(other);
        await Assert.That(clocks[1]).IsSameReferenceAs(root.Clock);
    }

    [Test]
    public async Task Lookup_UnknownToTheContainer_GoesToTheFallbackProvider()
    {
        OnlyInFallback only = new();
        using ServiceProvider provider = Build(s => s.AddSingleton(only).AddKeyedSingleton<IExternal, External>("key"));
        using FallbackContainer root = new(provider);
        using FallbackContainer scope = root.CreateScope();

        await Assert.That(root.GetService(typeof(OnlyInFallback))).IsSameReferenceAs(only);
        await Assert.That(scope.GetService<OnlyInFallback>()).IsSameReferenceAs(only);
        await Assert.That(root.GetKeyedService<IExternal>("key")).IsSameReferenceAs(provider.GetRequiredKeyedService<IExternal>("key"));
        await Assert.That(root.GetService<IExternal>()).IsNull();
        await Assert.That(root.GetServices<OnlyInFallback>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task IsService_AsksTheFallbackProviderForWhatTheContainerDoesNotHave()
    {
        using ServiceProvider provider = Build(static s => s.AddSingleton<OnlyInFallback>().AddKeyedSingleton<IExternal, External>("key"));
        using FallbackContainer root = new(provider);

        await Assert.That(root.IsService(typeof(IClock))).IsTrue();
        await Assert.That(root.IsService(typeof(OnlyInFallback))).IsTrue();
        await Assert.That(root.IsService(typeof(IExternal))).IsFalse();
        await Assert.That(root.IsKeyedService(typeof(IExternal), "key")).IsTrue();
        await Assert.That(root.IsKeyedService(typeof(IExternal), "other")).IsFalse();
    }

    [Test]
    public async Task Dispose_ProviderOfTheUser_NotDisposed()
    {
        using ServiceProvider provider = Build(static s => s.AddSingleton<IExternal, External>());
        FallbackContainer root = new(provider);
        External external = (External)root.External;

        root.Dispose();

        await Assert.That(external.IsDisposed).IsFalse();
        await Assert.That(provider.GetRequiredService<IExternal>()).IsSameReferenceAs(external);
    }

    [Test]
    public async Task Dispose_ScopeWithAsyncOnlyFallbackService_ThrowsAndDisposeAsyncWorks()
    {
        using ServiceProvider provider = Build(static s => s.AddScoped<AsyncExternal>());
        using FallbackContainer root = new(provider);
        FallbackContainer first = root.CreateScope();
        FallbackContainer second = root.CreateScope();
        AsyncExternal firstExternal = first.GetRequiredService<AsyncExternal>();
        AsyncExternal secondExternal = second.GetRequiredService<AsyncExternal>();

        // MS.DI throws for an object that implements only IAsyncDisposable, and the container passes the exception on
        await Assert.That(() => first.Dispose()).ThrowsExactly<InvalidOperationException>();
        await second.DisposeAsync();

        await Assert.That(secondExternal.Disposed).IsTrue();
        await Assert.That(firstExternal).IsNotSameReferenceAs(secondExternal);
    }

    [Test]
    public async Task ServiceProviderFactory_RootDisposed_DisposesTheProviderLast()
    {
        ServiceCollection services = new();
        services.AddSingleton<IExternal, External>();
        FallbackContainer.ServiceProviderFactory factory = new();

        IServiceProvider created = factory.CreateServiceProvider(factory.CreateBuilder(services));
        FallbackContainer root = (FallbackContainer)created;
        External external = (External)root.External;
        Journal journal = root.GetRequiredService<Journal>();

        ExternalHolder holder = root.GetRequiredService<ExternalHolder>();

        root.Dispose();

        await Assert.That(external.IsDisposed).IsTrue();
        await Assert.That(journal.Disposed).Contains(nameof(SystemClock));
        await Assert.That(holder.External).IsSameReferenceAs(external);
        await Assert.That(holder.ExternalDisposedBefore).IsFalse();
    }

    [Test]
    public async Task ServiceProviderFactory_ConstructorFails_DisposesTheProvider()
    {
        External external = new();
        ServiceCollection services = new();
        services.AddSingleton<IExternal>(_ => external);
        FallbackContainer.ServiceProviderFactory factory = new();
        FragileSingleton.Fail = true;
        try
        {
            await Assert.That(() => factory.CreateServiceProvider(services)).ThrowsExactly<InvalidOperationException>();
        }
        finally
        {
            FragileSingleton.Fail = false;
        }

        // ExternalHolder took the service from the provider during eager resolution, so the provider owned it
        await Assert.That(external.IsDisposed).IsTrue();
    }
}