namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that an <c>Instance</c> registration without an explicit Lifetime is a singleton in both outputs, whatever its
/// default settings (issue #207).
/// </summary>
public sealed class ImplicitSingletonInstanceTests
{
    [Test]
    public async Task ImplicitSingletonInstance_StandaloneContainer_ResolvesInstanceAsSingleton()
    {
        using var container = new ImplicitSingletonInstanceContainer();
        using var scope = container.CreateScope();

        await Assert.That(container.GetRequiredService<IImplicitSingletonInstance>()).IsSameReferenceAs(ImplicitSingletonInstance.Default);
        await Assert.That(scope.ServiceProvider.GetRequiredService<IImplicitSingletonInstance>()).IsSameReferenceAs(ImplicitSingletonInstance.Default);
        await Assert.That(container.GetRequiredService<ImplicitSingletonInstanceConsumer>().Instance.Value).IsSameReferenceAs(ImplicitSingletonInstance.Default);

        // The Lazy wrapper of a singleton is shared
        await Assert.That(container.GetRequiredService<Lazy<IImplicitSingletonInstance>>())
            .IsSameReferenceAs(container.GetRequiredService<Lazy<IImplicitSingletonInstance>>());
    }

    [Test]
    public async Task ImplicitSingletonInstance_MsDi_RegistersInstanceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();

        var descriptor = services.Single(static d => d.ServiceType == typeof(IImplicitSingletonInstance));
        await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Singleton);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await Assert.That(provider.GetRequiredService<IImplicitSingletonInstance>()).IsSameReferenceAs(ImplicitSingletonInstance.Default);
        await Assert.That(provider.GetRequiredService<ImplicitSingletonInstanceConsumer>().Instance.Value).IsSameReferenceAs(ImplicitSingletonInstance.Default);
    }
}
