namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for collection injection — IEnumerable&lt;T&gt; and IDictionary&lt;TKey, TValue&gt; resolving
/// multiple registrations for the same service interface.
/// </summary>
public sealed class CollectionTests
{
    #region Standalone Container — IEnumerable<T>

    [Test]
    public async Task Enumerable_StandaloneContainer_ReturnsAllRegisteredImplementations()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var plugins = container.GetRequiredService<IEnumerable<IPlugin>>().ToList();

        // Assert
        await Assert.That(plugins.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Enumerable_StandaloneContainer_ReturnsAllExpectedNames()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var names = container.GetRequiredService<IEnumerable<IPlugin>>()
            .Select(p => p.Name)
            .ToHashSet();

        // Assert
        await Assert.That(names.Contains("PluginA")).IsTrue();
        await Assert.That(names.Contains("PluginB")).IsTrue();
        await Assert.That(names.Contains("PluginC")).IsTrue();
    }

    [Test]
    public async Task PluginHost_StandaloneContainer_ReceivesAllPluginsViaConstructor()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var host = container.GetRequiredService<PluginHost>();

        // Assert
        await Assert.That(host.Plugins.Count).IsEqualTo(3);
    }

    #endregion

    #region Standalone Container — IDictionary<TKey, TValue>

    [Test]
    public async Task KeyedDictionary_StandaloneContainer_ContainsBothKeyedServices()
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
    public async Task KeyedDictionary_StandaloneContainer_ReturnsCorrectImplementationPerKey()
    {
        // Arrange
        using var container = new ContainerModule();

        // Act
        var registry = container.GetRequiredService<ProcessorRegistry>();

        // Assert
        await Assert.That(registry.Processors["alpha"].ProcessorName).IsEqualTo("Alpha");
        await Assert.That(registry.Processors["beta"].ProcessorName).IsEqualTo("Beta");
    }

    #endregion

    #region MS.Extensions.DI Integration Tests

    [Test]
    public async Task Enumerable_MsDi_ReturnsAllRegisteredImplementations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var plugins = provider.GetServices<IPlugin>().ToList();

        // Assert
        await Assert.That(plugins.Count).IsEqualTo(3);
    }

    [Test]
    public async Task PluginHost_MsDi_ReceivesAllPluginsViaConstructor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var host = provider.GetRequiredService<PluginHost>();

        // Assert
        await Assert.That(host.Plugins.Count).IsEqualTo(3);
    }

    #endregion
}
