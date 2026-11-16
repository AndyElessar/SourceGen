namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that a registration of a closed service type with another key does not keep an open generic registration from
/// being closed for a dependency (issue #205).
/// </summary>
public sealed class KeyedClosedGenericTests
{
    [Test]
    public async Task KeyedClosedGeneric_StandaloneContainer_ClosesForDependencyKey()
    {
        using var container = new KeyedClosedGenericContainer();

        await Assert.That(container.GetRequiredService<KeyedClosingConsumer>().Handler).IsTypeOf<KeyedClosingHandler<int>>();
        await Assert.That(container.GetRequiredService<KeyedOpenConsumer>().Handler).IsTypeOf<KeyedOpenHandler<int>>();
    }

    [Test]
    public async Task KeyedClosedGeneric_MsDi_ClosesForDependencyKey()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        // MS.DI cannot close the open generic registration for a value type under Native AOT
        await Assert.That(provider.GetRequiredService<KeyedClosingConsumer>().Handler).IsTypeOf<KeyedClosingHandler<int>>();
        await Assert.That(provider.GetRequiredService<KeyedOpenConsumer>().Handler).IsTypeOf<KeyedOpenHandler<int>>();
    }

    [Test]
    public async Task KeyedClosedGeneric_StandaloneContainer_SelectsKeyedRegistrations()
    {
        using var container = new KeyedClosedGenericContainer();

        // A value key without a same-key registration falls back to the C# code key, not to another value key (issue #217)
        await Assert.That(container.GetRequiredService<KeyedFallbackConsumer>().Handler).IsTypeOf<CodeKeyFallbackHandler<int>>();

        // A dictionary closes every keyed registration of its value type
        var handlers = container.GetRequiredService<KeyedDictionaryConsumer>().Handlers;
        await Assert.That(handlers.Count).IsEqualTo(2);
        await Assert.That(handlers["a"]).IsTypeOf<ADictionaryHandler<int>>();
        await Assert.That(handlers["b"]).IsTypeOf<BDictionaryHandler<int>>();

        // A keyed invocation closes the keyed registration, not the non-keyed one declared first
        await Assert.That(container.GetRequiredKeyedService<IKeyedInvocationHandler<int>>("k")).IsTypeOf<KeyedInvocationHandler<int>>();
    }

    [Test]
    public async Task KeyedClosedGeneric_MsDi_SelectsKeyedRegistrations()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<KeyedFallbackConsumer>().Handler).IsTypeOf<CodeKeyFallbackHandler<int>>();
        await Assert.That(provider.GetRequiredKeyedService<IKeyedInvocationHandler<int>>("k")).IsTypeOf<KeyedInvocationHandler<int>>();
    }
}
