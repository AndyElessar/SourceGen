namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for keyed service collection injection — IDictionary&lt;string, T&gt; resolving
/// all keyed registrations for the same service interface.
/// </summary>
public sealed class KeyedCollectionTests
{
    #region Standalone Container Tests

    [Test]
    public async Task KeyedDictionary_StandaloneContainer_ContainsBothKeys()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var registry = container.GetRequiredService<ProcessorRegistry>();

        // Assert
        await Assert.That(registry.Processors.ContainsKey("alpha")).IsTrue();
        await Assert.That(registry.Processors.ContainsKey("beta")).IsTrue();
    }

    [Test]
    public async Task KeyedDictionary_StandaloneContainer_ReturnsCorrectImplementation()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var registry = container.GetRequiredService<ProcessorRegistry>();

        // Assert
        await Assert.That(registry.Processors["alpha"].ProcessorName).IsEqualTo("Alpha");
        await Assert.That(registry.Processors["beta"].ProcessorName).IsEqualTo("Beta");
    }

    [Test]
    public async Task KeyedDictionary_StandaloneContainer_ExactlyTwoEntries()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var registry = container.GetRequiredService<ProcessorRegistry>();

        // Assert
        await Assert.That(registry.Processors.Count).IsEqualTo(2);
    }

    #endregion

    #region MS.Extensions.DI Integration Tests

    [Test]
    public async Task GetKeyedService_MsDi_AlphaKeyReturnsAlphaProcessor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var processor = provider.GetKeyedService<IProcessor>("alpha");

        // Assert
        await Assert.That(processor).IsNotNull();
        await Assert.That(processor!.ProcessorName).IsEqualTo("Alpha");
    }

    [Test]
    public async Task GetKeyedService_MsDi_BetaKeyReturnsBetaProcessor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var processor = provider.GetKeyedService<IProcessor>("beta");

        // Assert
        await Assert.That(processor).IsNotNull();
        await Assert.That(processor!.ProcessorName).IsEqualTo("Beta");
    }

    [Test]
    [Skip("IDictionary<string,T> MS DI injection via IEnumerable<KeyValuePair<string,T>> is AOT-incompatible: KeyValuePair<K,V> is a ValueType and cannot be enumerated in native AOT.")]
    public async Task ProcessorRegistry_MsDi_ReceivesAllProcessorsViaDictionary()
    {
        // NOTE: MS DI resolves IDictionary<string, T> by enumerating KeyValuePair<string, T>.
        // KeyValuePair<K,V> is a ValueType, which native AOT cannot enumerate through IEnumerable.
        // Use the standalone container path (KeyedCollectionModule) instead.
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ProcessorRegistry>();

        await Assert.That(registry.Processors.Count).IsEqualTo(2);
        await Assert.That(registry.Processors["alpha"].ProcessorName).IsEqualTo("Alpha");
        await Assert.That(registry.Processors["beta"].ProcessorName).IsEqualTo("Beta");
    }

    #endregion
}
