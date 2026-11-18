namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// The Register output adds tagged registrations when any of their tags is passed, so one call can add several tag groups.
/// Explicit wrapper registrations and async-init registrations of another tag group are only used when they are added.
/// </summary>
public sealed class RegisterTaggedWrapperRegistrationTests
{
    [Test]
    public async Task ExplicitLazy_UnderPassedTag_WinsOverGeneratedLazyOfOverlappingTagGroup()
    {
        await using var provider = new ServiceCollection().AddSourceGen_Ioc_TestCase("ExplicitWrapperA").BuildServiceProvider();

        await Assert.That(provider.GetService<Lazy<ITaggedExplicitWrapperService>>()).IsSameReferenceAs(TaggedExplicitWrapperInstances.Lazy);
        await Assert.That(provider.GetRequiredService<TaggedExplicitWrapperConsumer>().Lazy).IsSameReferenceAs(TaggedExplicitWrapperInstances.Lazy);
        await Assert.That(provider.GetServices<Lazy<ITaggedExplicitWrapperService>>().Single()).IsSameReferenceAs(TaggedExplicitWrapperInstances.Lazy);
    }

    [Test]
    public async Task GeneratedLazy_WithoutExplicitLazyTag_ResolvesService()
    {
        await using var provider = new ServiceCollection().AddSourceGen_Ioc_TestCase("ExplicitWrapperC").BuildServiceProvider();

        var lazy = provider.GetRequiredService<TaggedExplicitWrapperConsumer>().Lazy;

        await Assert.That(lazy).IsNotSameReferenceAs(TaggedExplicitWrapperInstances.Lazy);
        await Assert.That(lazy.Value).IsSameReferenceAs(provider.GetRequiredService<ITaggedExplicitWrapperService>());
    }

    [Test]
    public async Task TaskDependency_WithoutAsyncInitTag_WrapsSynchronousService()
    {
        await using var provider = new ServiceCollection().AddSourceGen_Ioc_TestCase("SplitAsyncB").BuildServiceProvider();

        var service = await provider.GetRequiredService<SplitAsyncConsumer>().Service;

        await Assert.That(service).IsTypeOf<SyncSplitAsyncService>();
    }

    [Test]
    public async Task TaskDependency_WithAsyncInitTag_ResolvesAsyncInitRegistration()
    {
        await using var provider = new ServiceCollection().AddSourceGen_Ioc_TestCase("SplitAsyncA", "SplitAsyncB").BuildServiceProvider();

        var service = await provider.GetRequiredService<SplitAsyncConsumer>().Service;

        await Assert.That(service).IsTypeOf<AsyncSplitAsyncService>();
    }
}
