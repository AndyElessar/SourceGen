#pragma warning disable SGIOCEXP001 // The test uses the types that are only for generated code

namespace SourceGen.Ioc.Test.Library;

/// <summary>The guard against duplicate registration (ServiceCollection.Output.spec, "Guard against duplicate registration").</summary>
public class IocRegistrationMarkerTests
{
    [Test]
    public async Task Enter_NewAssemblies_AddsOneInstanceMarkerForEach()
    {
        ServiceCollection services = new();

        IocRegistrationMarker.Enter(services, "App.AppServiceCollectionExtensions.AddApp", "App", "Mod");

        IocRegistrationMarker[] markers = [.. services.Select(static d => d.ImplementationInstance).OfType<IocRegistrationMarker>()];
        await Assert.That(services.All(static d => d.ServiceType == typeof(IocRegistrationMarker) && d.Lifetime == ServiceLifetime.Singleton)).IsTrue();
        await Assert.That(markers.Select(static m => m.Assembly)).IsEquivalentTo(["App", "Mod"]);
        await Assert.That(markers.All(static m => m.Method == "App.AppServiceCollectionExtensions.AddApp")).IsTrue();
    }

    [Test]
    public async Task Enter_AssemblyAlreadyRegistered_ThrowsAndLeavesServicesUnchanged()
    {
        ServiceCollection services = new();
        IocRegistrationMarker.Enter(services, "Mod.ModServiceCollectionExtensions.AddMod", "Mod");

        InvalidOperationException? thrown = await Assert.That(() => IocRegistrationMarker.Enter(services, "App.AppServiceCollectionExtensions.AddApp", "App", "Mod"))
            .Throws<InvalidOperationException>();

        await Assert.That(thrown!.Message).Contains("'Mod'");
        await Assert.That(thrown.Message).Contains("Mod.ModServiceCollectionExtensions.AddMod");
        await Assert.That(thrown.Message).Contains("final assembly");
        await Assert.That(services).HasSingleItem();
    }

    [Test]
    public async Task Enter_SameMethodTwice_Throws()
    {
        ServiceCollection services = new();
        IocRegistrationMarker.Enter(services, "App.AppServiceCollectionExtensions.AddApp", "App");

        await Assert.That(() => IocRegistrationMarker.Enter(services, "App.AppServiceCollectionExtensions.AddApp", "App"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Enter_OtherAssembliesAndKeyedServices_DoesNotThrow()
    {
        ServiceCollection services = new();
        services.AddKeyedSingleton<object>("key", new object());
        IocRegistrationMarker.Enter(services, "A.AServiceCollectionExtensions.AddA", "A");

        IocRegistrationMarker.Enter(services, "B.BServiceCollectionExtensions.AddB", "B");

        await Assert.That(services.Count(static d => d.ServiceType == typeof(IocRegistrationMarker))).IsEqualTo(2);
    }

    [Test]
    public async Task Marker_Descriptor_IsAnInstanceRegistration()
    {
        ServiceCollection services = new();
        IocRegistrationMarker.Enter(services, "App.AppServiceCollectionExtensions.AddApp", "App");

        ServiceDescriptor descriptor = services.Single();
        await using ServiceProvider provider = services.BuildServiceProvider();

        await Assert.That(descriptor.ImplementationInstance).IsNotNull();
        await Assert.That(descriptor.ImplementationFactory).IsNull();
        await Assert.That(provider.GetServices<IocRegistrationMarker>()).HasSingleItem();
    }
}
