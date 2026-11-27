namespace SourceGen.Ioc.TestAot.Container;

/// <summary>Instance registrations in the container (Container.Output.spec, "Resolution of a closed definition").</summary>
public class InstanceTests
{
    [Test]
    public async Task GetService_Instance_ReturnsTheStaticObjectAndDoesNotDisposeIt()
    {
        AppContainer root = new();
        AppContainer scope = root.CreateScope();

        StaticResource resource = scope.GetRequiredService<StaticResource>();
        scope.Dispose();
        root.Dispose();

        await Assert.That(ReferenceEquals(resource, AotInstances.Resource)).IsTrue();
        await Assert.That(resource.IsDisposed).IsFalse();
    }

    [Test]
    public async Task GetKeyedService_InstanceIsNull_Throws()
    {
        using AppContainer root = new();
        Marker marker = root.GetRequiredKeyedService<Marker>(AotInstances.MarkerKey);

        AotInstances.ReturnNull = true;
        Exception? thrown;
        try
        {
            thrown = FactoryTests.Catch(() => root.GetKeyedService<Marker>(AotInstances.MarkerKey));
        }
        finally
        {
            AotInstances.ReturnNull = false;
        }

        // The container reads the member on each request, so the change is visible
        await Assert.That(marker).IsNotNull();
        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("Instance 'SourceGen.Ioc.TestAot.Container.AotInstances.Current (Key: \"marker\")' is null.");
    }
}
