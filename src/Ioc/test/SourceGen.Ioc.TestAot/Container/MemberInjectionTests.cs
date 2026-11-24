namespace SourceGen.Ioc.TestAot.Container;

/// <summary>
/// Member injection in the container (Annotations.spec, "Injected members" and "Order"; Container.Output.spec, "Failure
/// during creation").
/// </summary>
public class MemberInjectionTests
{
    [Test]
    public async Task GetService_InjectedMembers_AssignedInTheOrderOfTheSteps()
    {
        using AppContainer root = new();
        using AppContainer scope = root.CreateScope();

        InjectedScoped service = scope.GetRequiredService<InjectedScoped>();

        await Assert.That(service.Session).IsSameReferenceAs(scope.GetRequiredService<Session>());
        await Assert.That(service.Repository).IsSameReferenceAs(root.GetRequiredService<Repository>());
        await Assert.That(service.BaseClock).IsSameReferenceAs(root.Clock);
        await Assert.That(service.Missing).IsNull();
        await Assert.That(service.Label).IsEqualTo("initial");
        await Assert.That(string.Join(",", service.Steps)).IsEqualTo("InitializeBase:True,Initialize:True:True:True");
        await Assert.That(scope.GetRequiredService<InjectedScoped>()).IsSameReferenceAs(service);
    }

    [Test]
    public async Task GetService_InjectionMethodThrows_DisposesTheInstanceAndCreatesItAgainLater()
    {
        using AppContainer root = new();
        Journal journal = root.GetRequiredService<Journal>();
        using AppContainer scope = root.CreateScope();

        Exception? thrown = CatchInitialize(() => scope.GetService<InjectedScoped>());

        await Assert.That(thrown).IsTypeOf<InvalidOperationException>();
        await Assert.That(thrown!.Message).IsEqualTo("Initialize failed.");
        await Assert.That(string.Join(",", journal.Disposed)).IsEqualTo("InjectedScoped");

        InjectedScoped service = scope.GetRequiredService<InjectedScoped>();
        scope.Dispose();

        await Assert.That(service.Steps.Count).IsEqualTo(2);
        await Assert.That(string.Join(",", journal.Disposed)).StartsWith("InjectedScoped,InjectedScoped,");
    }

    /// <summary>Runs <paramref name="resolve"/> on this thread with a failing injection method, and returns its exception.</summary>
    internal static Exception? CatchInitialize(Func<object?> resolve)
    {
        InjectedScoped.FailInitialize = true;
        try
        {
            GC.KeepAlive(resolve());
            return null;
        }
        catch(Exception e)
        {
            return e;
        }
        finally
        {
            InjectedScoped.FailInitialize = false;
        }
    }
}
