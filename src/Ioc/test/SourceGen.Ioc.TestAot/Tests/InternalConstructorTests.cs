namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that implementation types with only an internal constructor are constructed by generated code, since MS.DI
/// activates public constructors only.
/// </summary>
public sealed class InternalConstructorTests
{
    [Test]
    public async Task InternalConstructor_StandaloneContainer_ResolvesClosedAndOpenGenericTypes()
    {
        using var container = new InternalCtorContainer();

        var service = container.GetRequiredService<InternalCtorService>();
        var consumer = container.GetRequiredService<InternalCtorRepoConsumer>();

        await Assert.That(service.Dependency).IsTypeOf<InternalCtorDependency>();
        await Assert.That(consumer.Repo).IsTypeOf<InternalCtorRepo<string>>();
    }

    [Test]
    public async Task InternalConstructor_MsDi_ResolvesClosedAndOpenGenericTypes()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var service = provider.GetRequiredService<InternalCtorService>();
        var consumer = provider.GetRequiredService<InternalCtorRepoConsumer>();

        await Assert.That(service.Dependency).IsTypeOf<InternalCtorDependency>();
        await Assert.That(consumer.Repo).IsTypeOf<InternalCtorRepo<string>>();
    }
}
