namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that <c>Lazy&lt;T&gt;</c> / <c>Func&lt;T&gt;</c> wrappers resolve the same instances as the wrapped service,
/// in the generated container and in the Register output with Microsoft.Extensions.DependencyInjection.
/// </summary>
public class WrapperSemanticsTests
{
    public static IEnumerable<Func<IServiceProvider>> Providers()
    {
        yield return static () => new WrapperSemanticsModule();
        yield return static () => new ServiceCollection().AddSourceGen_Ioc_TestCase().BuildServiceProvider();
    }

    [Test]
    [MethodDataSource(nameof(Providers))]
    public async Task Wrapper_OfDecoratedService_ResolvesSharedDecoratedInstance(IServiceProvider provider)
    {
        var consumer = provider.GetRequiredService<WrapperSemanticsConsumer>();
        var decorated = provider.GetRequiredService<IWrappedService>();

        await Assert.That(decorated).IsTypeOf<WrappedServiceDecorator>();
        await Assert.That(consumer.DecoratedLazy.Value).IsSameReferenceAs(decorated);
        await Assert.That(consumer.DecoratedFactory()).IsSameReferenceAs(decorated);
        await ((IAsyncDisposable)provider).DisposeAsync();
    }

    [Test]
    [MethodDataSource(nameof(Providers))]
    public async Task Lazy_OfTransientService_GivesEachConsumerItsOwnInstance(IServiceProvider provider)
    {
        var first = provider.GetRequiredService<WrapperSemanticsConsumer>();
        var second = provider.GetRequiredService<WrapperSemanticsConsumer>();

        await Assert.That(first.TransientLazy.Value).IsNotSameReferenceAs(second.TransientLazy.Value);
        await ((IAsyncDisposable)provider).DisposeAsync();
    }

    [Test]
    [MethodDataSource(nameof(Providers))]
    public async Task Lazy_OfKeyedService_ResolvesKeyedInstanceAndStaysOutOfNonKeyedCollection(IServiceProvider provider)
    {
        var consumer = provider.GetRequiredService<WrapperSemanticsConsumer>();
        var keyed = provider.GetRequiredKeyedService<IKeyedWrappedService>("keyed");
        var nonKeyed = provider.GetRequiredService<IKeyedWrappedService>();

        await Assert.That(consumer.KeyedLazy.Value).IsSameReferenceAs(keyed);
        await Assert.That(keyed).IsNotSameReferenceAs(nonKeyed);

        var all = consumer.AllKeyedWrapped.ToArray();
        await Assert.That(all.Length).IsEqualTo(1);
        await Assert.That(all[0].Value).IsSameReferenceAs(nonKeyed);
        await ((IAsyncDisposable)provider).DisposeAsync();
    }

    [Test]
    [MethodDataSource(nameof(Providers))]
    public async Task FuncWithArguments_OfDecoratedSingleton_CreatesNewDecoratedInstancePerCall(IServiceProvider provider)
    {
        var consumer = provider.GetRequiredService<FuncWithArgumentsConsumer>();

        var first = consumer.Factory("first");
        var second = consumer.Factory("first");

        await Assert.That(first).IsTypeOf<NamedServiceDecorator>();
        await Assert.That(first.Inner).IsTypeOf<NamedService>();
        await Assert.That(first.Name).IsEqualTo("first");
        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(second.Inner).IsNotSameReferenceAs(first.Inner);
        await ((IAsyncDisposable)provider).DisposeAsync();
    }

    [Test]
    [MethodDataSource(nameof(Providers))]
    public async Task FuncWithArguments_OfKeyedService_CreatesKeyedImplementationWithServiceKey(IServiceProvider provider)
    {
        var consumer = provider.GetRequiredService<FuncWithArgumentsConsumer>();

        var keyed = consumer.KeyedFactory("keyed-name");

        await Assert.That(keyed).IsTypeOf<KeyedNamedService>();
        await Assert.That(keyed.Name).IsEqualTo("keyed-name");
        await Assert.That(keyed.Key).IsEqualTo("keyed");
        await Assert.That(consumer.Factory("name")).IsTypeOf<NamedServiceDecorator>();
        await ((IAsyncDisposable)provider).DisposeAsync();
    }
}