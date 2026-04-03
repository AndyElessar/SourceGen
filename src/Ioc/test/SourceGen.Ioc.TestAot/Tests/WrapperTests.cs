namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for Lazy&lt;T&gt; and Func&lt;T&gt; wrapper injection — resolving a wrapper type whose
/// inner type has multiple registrations.
/// </summary>
public sealed class WrapperTests
{
    #region Standalone Container — Lazy<T> and Func<T> wrapper

    [Test]
    public async Task LazyConsumer_StandaloneContainer_LazyWrapperResolvesService()
    {
        // Arrange
        using var container = new WrapperModule();

        // Act
        var consumer = container.GetRequiredService<LazyPluginConsumer>();

        // Assert — consumer is resolved; lazy is not yet materialised
        await Assert.That(consumer).IsNotNull();
        var plugin = consumer.LazyPlugin.Value;
        await Assert.That(plugin).IsNotNull();
    }

    [Test]
    public async Task FuncConsumer_StandaloneContainer_FuncWrapperResolvesService()
    {
        // Arrange
        using var container = new WrapperModule();

        // Act
        var consumer = container.GetRequiredService<LazyPluginConsumer>();
        var plugin1 = consumer.PluginFactory();
        var plugin2 = consumer.PluginFactory();

        // Assert — singleton-backed Func returns the same instance each call
        await Assert.That(plugin1).IsNotNull();
        await Assert.That(plugin1).IsSameReferenceAs(plugin2);
    }

    #endregion

    #region MS.Extensions.DI Integration Tests

    [Test]
    public async Task LazyConsumer_MsDi_LazyWrapperResolvesService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var consumer = provider.GetRequiredService<LazyPluginConsumer>();

        // Assert — consumer is resolved; lazy materialises on first access
        await Assert.That(consumer).IsNotNull();
        var plugin = consumer.LazyPlugin.Value;
        await Assert.That(plugin).IsNotNull();
    }

    [Test]
    public async Task FuncConsumer_MsDi_FuncWrapperResolvesService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestCase();
        await using var provider = services.BuildServiceProvider();

        // Act
        var consumer = provider.GetRequiredService<LazyPluginConsumer>();
        var plugin = consumer.PluginFactory();

        // Assert
        await Assert.That(plugin).IsNotNull();
    }

    #endregion
}
