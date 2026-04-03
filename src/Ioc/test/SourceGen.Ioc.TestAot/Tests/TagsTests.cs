namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for IncludeTags filtering — verifies that containers with IncludeTags
/// only include services tagged with the specified tags.
/// Services must be defined in the same assembly as the container (using [IocRegister])
/// for the source generator's IncludeTags filter to apply at compile time.
/// </summary>
public sealed class TagsTests
{
    #region FeatureAContainer — IncludeTags = ["featureA"]

    [Test]
    public async Task FeatureAContainer_Resolves_FeatureAService()
    {
        // Arrange
        using var container = new FeatureAContainer();

        // Act
        var service = container.GetRequiredService<IFeatureService>();

        // Assert — only FeatureAService is tagged "featureA"
        await Assert.That(service).IsNotNull();
        await Assert.That(service.FeatureName).IsEqualTo("FeatureA");
    }

    [Test]
    public async Task FeatureAContainer_DoesNotContain_FeatureBService()
    {
        // Arrange — FeatureAContainer only includes services tagged "featureA"
        using var container = new FeatureAContainer();

        // Act — IFeatureService resolves to FeatureAService (only one registered)
        var service = container.GetRequiredService<IFeatureService>();

        // Assert — must NOT be FeatureBService
        await Assert.That(service.FeatureName).IsNotEqualTo("FeatureB");
    }

    #endregion

    #region FeatureBContainer — IncludeTags = ["featureB"]

    [Test]
    public async Task FeatureBContainer_Resolves_FeatureBService()
    {
        // Arrange
        using var container = new FeatureBContainer();

        // Act
        var service = container.GetRequiredService<IFeatureService>();

        // Assert — only FeatureBService is tagged "featureB"
        await Assert.That(service).IsNotNull();
        await Assert.That(service.FeatureName).IsEqualTo("FeatureB");
    }

    [Test]
    public async Task FeatureBContainer_DoesNotContain_FeatureAService()
    {
        // Arrange — FeatureBContainer only includes services tagged "featureB"
        using var container = new FeatureBContainer();

        // Act
        var service = container.GetRequiredService<IFeatureService>();

        // Assert — must NOT be FeatureAService
        await Assert.That(service.FeatureName).IsNotEqualTo("FeatureA");
    }

    #endregion

    #region TagsModule — unfiltered, both tagged services visible

    [Test]
    public async Task TagsModule_Resolves_ITaggedService()
    {
        // Arrange — TagsModule has no IncludeTags filter; both tagged services are registered
        using var container = new TagsModule();

        // Act
        var service = container.GetService<ITaggedService>();

        // Assert — at least one tagged service is resolvable
        await Assert.That(service).IsNotNull();
    }

    [Test]
    public async Task TagsModule_Resolves_BothTaggedServicesViaEnumerable()
    {
        // Arrange — TagsModule has no IncludeTags filter; both tagged services are registered
        using var container = new TagsModule();

        // Act — IEnumerable<T> returns all registered implementations
        var allServices = container.GetService<IEnumerable<ITaggedService>>()?.ToList();

        // Assert
        await Assert.That(allServices).IsNotNull();
        await Assert.That(allServices!.Count).IsEqualTo(2);
        await Assert.That(allServices.Any(s => s.ServiceName == "TaggedServiceA")).IsTrue();
        await Assert.That(allServices.Any(s => s.ServiceName == "TaggedServiceB")).IsTrue();
    }

    #endregion
}
