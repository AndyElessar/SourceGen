namespace SourceGen.Ioc.TestAot.Container;

/// <summary>
/// Factory registrations in the container (Container.Output.spec, "Resolution of a closed definition" and "Order";
/// Analysis.Model.spec, "Disposal kind").
/// </summary>
public class FactoryTests
{
    [Test]
    public async Task GetService_SingletonFactory_CalledOnceWithItsDependencies()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        IFactoryHandle handle = root.GetRequiredService<IFactoryHandle>();

        await Assert.That(handle.Clock).IsSameReferenceAs(root.Clock);
        await Assert.That(scope.GetRequiredService<IFactoryHandle>()).IsSameReferenceAs(handle);
    }

    [Test]
    public async Task Dispose_FactoryOfAnInterface_DisposesTheObjectByItsTypeAtRunTime()
    {
        AppContainer root = new();
        IFactoryHandle handle = root.GetRequiredService<IFactoryHandle>();
        AppContainer other = new();
        IFactoryHandle otherHandle = other.GetRequiredService<IFactoryHandle>();

        root.Dispose();
        await other.DisposeAsync();

        await Assert.That(handle.IsDisposed).IsTrue();
        await Assert.That(otherHandle.IsDisposed).IsTrue();
    }

    [Test]
    public async Task GetKeyedService_ScopedFactory_ReceivesTheKeyAndTheScopedDependency()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        FactoryNote note = scope.GetRequiredKeyedService<FactoryNote>(AotFactories.NoteKey);

        await Assert.That(note.Key).IsEqualTo(AotFactories.NoteKey);
        await Assert.That(note.Session).IsSameReferenceAs(scope.GetRequiredService<Session>());
        await Assert.That(ReferenceEquals(scope.GetRequiredKeyedService<FactoryNote>(AotFactories.NoteKey), note)).IsTrue();
    }

    [Test]
    public async Task GetService_FactoryReturnsNull_Throws()
    {
        using AppContainer root = new();

        Exception? thrown = Catch(() => root.GetService<NullProduct>());

        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("Factory 'SourceGen.Ioc.TestAot.Container.AotFactories.CreateNull()' returned null.");
    }

    internal static Exception? Catch(Func<object?> resolve)
    {
        try
        {
            GC.KeepAlive(resolve());
            return null;
        }
        catch(Exception e)
        {
            return e;
        }
    }
}
