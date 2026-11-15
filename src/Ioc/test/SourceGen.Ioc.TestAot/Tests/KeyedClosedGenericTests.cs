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
}
