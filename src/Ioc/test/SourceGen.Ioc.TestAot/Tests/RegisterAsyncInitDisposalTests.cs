namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that the Register output disposes async-init services (registered as <c>Task&lt;T&gt;</c>) and their
/// async-init decorators with the scope or provider that owns their lifetime.
/// </summary>
public sealed class RegisterAsyncInitDisposalTests
{
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Test]
    public async Task AsyncInitService_Scoped_IsDisposedWithScope()
    {
        await using var provider = CreateServiceProvider();

        IAsyncInitDisposable service;
        IAsyncInitDisposable keyed;
        using(var scope = provider.CreateScope())
        {
            service = await scope.ServiceProvider.GetRequiredService<Task<IAsyncInitDisposable>>();
            keyed = await scope.ServiceProvider.GetRequiredKeyedService<Task<IAsyncInitDisposable>>("scoped");

            await Assert.That(service.IsInitialized).IsTrue();
            await Assert.That(await scope.ServiceProvider.GetRequiredService<Task<IAsyncInitDisposable>>()).IsSameReferenceAs(service);
            await Assert.That(await scope.ServiceProvider.GetRequiredService<Task<AsyncInitDisposable>>()).IsSameReferenceAs(service);
            await Assert.That(service.IsDisposed).IsFalse();
        }

        await Assert.That(service.IsDisposed).IsTrue();
        await Assert.That(keyed).IsNotSameReferenceAs(service);
        await Assert.That(keyed.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitService_Singleton_IsDisposedWithProvider()
    {
        var provider = CreateServiceProvider();

        IAsyncInitDisposable service;
        await using(var scope = provider.CreateAsyncScope())
        {
            service = await scope.ServiceProvider.GetRequiredKeyedService<Task<IAsyncInitDisposable>>("singleton");
        }

        await Assert.That(service.IsDisposed).IsFalse();
        await Assert.That(await provider.GetRequiredKeyedService<Task<IAsyncInitDisposable>>("singleton")).IsSameReferenceAs(service);

        provider.Dispose();

        await Assert.That(service.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitService_Transient_IsDisposedWithScope()
    {
        await using var provider = CreateServiceProvider();

        IAsyncInitDisposable first;
        IAsyncInitDisposable second;
        await using(var scope = provider.CreateAsyncScope())
        {
            first = await scope.ServiceProvider.GetRequiredKeyedService<Task<IAsyncInitDisposable>>("transient");
            second = await scope.ServiceProvider.GetRequiredKeyedService<Task<IAsyncInitDisposable>>("transient");

            await Assert.That(second).IsNotSameReferenceAs(first);
            await Assert.That(first.IsDisposed).IsFalse();
        }

        await Assert.That(first.IsDisposed).IsTrue();
        await Assert.That(second.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitService_AsyncDisposableOnly_IsDisposedWithProviderDisposeAsync()
    {
        var provider = CreateServiceProvider();
        var service = await provider.GetRequiredService<Task<IAsyncInitAsyncDisposable>>();

        await provider.DisposeAsync();

        await Assert.That(service.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitService_WithDisposableDecorator_DisposesDecoratorThenImplementation()
    {
        await using var provider = CreateServiceProvider();

        IDecoratedAsyncInitDisposable decorated;
        await using(var scope = provider.CreateAsyncScope())
        {
            decorated = await scope.ServiceProvider.GetRequiredService<Task<IDecoratedAsyncInitDisposable>>();
            await Assert.That(decorated).IsTypeOf<DisposableDecoratorOfAsyncInit>();
        }

        await Assert.That(decorated.IsDisposed).IsTrue();
        await Assert.That(decorated.Inner!.IsDisposed).IsTrue();
        await Assert.That(decorated.InnerWasDisposedFirst).IsFalse();
    }

    [Test]
    public async Task AsyncInitDecorator_Scoped_IsDisposedWithScope()
    {
        await using var provider = CreateServiceProvider();

        IDisposableAsyncDecoratedService decorated;
        await using(var scope = provider.CreateAsyncScope())
        {
            decorated = await scope.ServiceProvider.GetRequiredKeyedService<Task<IDisposableAsyncDecoratedService>>("scoped");
            await Assert.That(decorated).IsTypeOf<DisposableAsyncInitDecorator>();
            await Assert.That(decorated.IsDisposed).IsFalse();
        }

        await Assert.That(decorated.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitDecorator_Singleton_IsDisposedWithProvider()
    {
        var provider = CreateServiceProvider();
        var decorated = await provider.GetRequiredKeyedService<Task<IDisposableAsyncDecoratedService>>("singleton");

        await Assert.That(decorated).IsTypeOf<DisposableAsyncInitDecorator>();
        await Assert.That(decorated.IsDisposed).IsFalse();

        await provider.DisposeAsync();

        await Assert.That(decorated.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitDecorator_Transient_IsDisposedWithScope()
    {
        await using var provider = CreateServiceProvider();

        IDisposableAsyncDecoratedService first;
        IDisposableAsyncDecoratedService second;
        await using(var scope = provider.CreateAsyncScope())
        {
            first = await scope.ServiceProvider.GetRequiredKeyedService<Task<IDisposableAsyncDecoratedService>>("transient");
            second = await scope.ServiceProvider.GetRequiredKeyedService<Task<IDisposableAsyncDecoratedService>>("transient");

            await Assert.That(second).IsNotSameReferenceAs(first);
        }

        await Assert.That(first.IsDisposed).IsTrue();
        await Assert.That(second.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncInitDecorator_TwoImplementationsUnderSameKey_EachResolvesItsOwnChain()
    {
        await using var provider = CreateServiceProvider();

        var tasks = provider.GetKeyedServices<Task<ISharedKeyAsyncInitService>>("shared").ToArray();
        var names = string.Join(",", (await Task.WhenAll(tasks)).Select(static s => s.Name).Order(StringComparer.Ordinal));

        await Assert.That(names).IsEqualTo("First,Second");
    }
}
