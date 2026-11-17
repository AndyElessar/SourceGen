namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// A wrapper or collection type registered explicitly (<c>IReadOnlyDictionary&lt;K, V&gt;</c>, <c>Lazy&lt;T&gt;</c>,
/// <c>Func&lt;T&gt;</c>, <c>Lazy&lt;Func&lt;T&gt;&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>) is injected as that registration, like
/// <c>MS.DI</c> resolving a constructor parameter, in the Register output and in the generated container (GitHub issue #241).
/// </summary>
public sealed class RegisterExplicitWrapperRegistrationTests
{
    public static IEnumerable<Func<IServiceProvider>> Providers()
    {
        yield return static () => new ServiceCollection().AddSourceGen_Ioc_TestCase().BuildServiceProvider();
        yield return static () => new ExplicitWrapperRegistrationModule();
    }

    [Test]
    public async Task MsDi_ExplicitWrapperRegistrations_AreInjected()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IReadOnlyDictionary<string, IExplicitWrapperHandler>>(ExplicitWrapperHandlerDictionary.Default);
        services.AddSingleton(ExplicitWrapperInstances.Lazy);
        services.AddSingleton(ExplicitWrapperInstances.Func);
        services.AddSingleton(ExplicitWrapperInstances.LazyFunc);
        services.AddSingleton<IReadOnlyList<IExplicitWrapperHandler>>(ExplicitWrapperHandlerList.Default);
        services.AddTransient<ExplicitWrapperRegistrationConsumer>();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<ExplicitWrapperRegistrationConsumer>();

        await Assert.That(consumer.Dictionary).IsSameReferenceAs(ExplicitWrapperHandlerDictionary.Default);
        await Assert.That(consumer.Lazy).IsSameReferenceAs(ExplicitWrapperInstances.Lazy);
        // As object: Assert.That(Func<T>) would invoke the delegate
        await Assert.That((object)consumer.Func).IsSameReferenceAs(ExplicitWrapperInstances.Func);
        await Assert.That(consumer.LazyFunc).IsSameReferenceAs(ExplicitWrapperInstances.LazyFunc);
        await Assert.That(consumer.List).IsSameReferenceAs(ExplicitWrapperHandlerList.Default);
    }

    [Test]
    [MethodDataSource(nameof(Providers))]
    public async Task ExplicitWrapperRegistrations_AreInjectedAndReturnedByGetService(IServiceProvider provider)
    {
        await using var disposable = (IAsyncDisposable)provider;

        var consumer = provider.GetRequiredService<ExplicitWrapperRegistrationConsumer>();

        await Assert.That(provider.GetService<IReadOnlyDictionary<string, IExplicitWrapperHandler>>()).IsSameReferenceAs(ExplicitWrapperHandlerDictionary.Default);
        await Assert.That(provider.GetService<Lazy<IExplicitWrapperHandler>>()).IsSameReferenceAs(ExplicitWrapperInstances.Lazy);
        await Assert.That((object?)provider.GetService<Func<IExplicitWrapperHandler>>()).IsSameReferenceAs(ExplicitWrapperInstances.Func);

        await Assert.That(consumer.Dictionary).IsSameReferenceAs(ExplicitWrapperHandlerDictionary.Default);
        await Assert.That(consumer.Lazy).IsSameReferenceAs(ExplicitWrapperInstances.Lazy);
        await Assert.That((object)consumer.Func).IsSameReferenceAs(ExplicitWrapperInstances.Func);
        await Assert.That(consumer.LazyFunc).IsSameReferenceAs(ExplicitWrapperInstances.LazyFunc);
        await Assert.That(consumer.List).IsSameReferenceAs(ExplicitWrapperHandlerList.Default);
    }
}
