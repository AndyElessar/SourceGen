namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for disposable service container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.DisposableService)]
public class DisposableServiceTests
{
    [Test]
    public async Task Container_WithTransientDisposable_GeneratesDisposableTracking()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDisposableService : IDisposable { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IDisposableService)])]
            public class DisposableService : IDisposableService
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithAsyncDisposable_GeneratesAsyncDisposableTracking()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IAsyncDisposableService : IAsyncDisposable { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IAsyncDisposableService)])]
            public class AsyncDisposableService : IAsyncDisposableService
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
