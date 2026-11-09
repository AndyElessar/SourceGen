namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for partial accessors of a container without a fallback provider.
/// </summary>
public sealed class PartialAccessorTests
{
    [Test]
    public async Task PartialAccessor_ServiceOfImportedModule_ResolvesModuleSingleton()
    {
        using var container = new PartialAccessorContainer();

        var service = container.GetModuleService();

        await Assert.That(service).IsNotNull();
        await Assert.That(container.TryGetModuleService()).IsSameReferenceAs(service);
        await Assert.That(container.GetRequiredService<IAccessorModuleService>()).IsSameReferenceAs(service);
    }

    [Test]
    public async Task PartialAccessor_KeyedServiceOfImportedModule_ResolvesKeyedService()
    {
        using var container = new PartialAccessorContainer();

        var service = container.KeyedModuleService;

        await Assert.That(service).IsNotNull();
        await Assert.That(container.GetRequiredKeyedService<IAccessorModuleService>("keyed")).IsSameReferenceAs(service);
    }

    [Test]
    public async Task PartialAccessor_TaskOfSynchronousModuleService_ReturnsCompletedTask()
    {
        using var container = new PartialAccessorContainer();

        var task = container.GetModuleServiceAsync();

        await Assert.That(task.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await task).IsSameReferenceAs(container.GetModuleService());
    }

    [Test]
    public async Task PartialAccessor_ServiceOfCrossAssemblyModule_ResolvesModuleSingleton()
    {
        using var container = new PartialAccessorContainer();

        var service = container.GetCrossAssemblyModuleService();

        await Assert.That(service).IsNotNull();
        await Assert.That(container.GetRequiredService<ISingletonService>()).IsSameReferenceAs(service);
    }

    [Test]
    public async Task PartialAccessor_WrappersOfOwnService_ResolveOwnSingleton()
    {
        using var container = new PartialAccessorContainer();

        var service = container.GetLocalService();

        await Assert.That(container.LazyLocalService.Value).IsSameReferenceAs(service);
        await Assert.That(container.GetLocalServiceFactory()()).IsSameReferenceAs(service);
    }

    [Test]
    public async Task PartialAccessor_CollectionsOfOwnService_ContainOwnSingleton()
    {
        using var container = new PartialAccessorContainer();

        var service = container.GetLocalService();

        var services = container.GetLocalServices().ToArray();
        var array = container.LocalServiceArray;

        await Assert.That(services.Length).IsEqualTo(1);
        await Assert.That(services[0]).IsSameReferenceAs(service);
        await Assert.That(array.Length).IsEqualTo(1);
        await Assert.That(array[0]).IsSameReferenceAs(service);
    }
}
