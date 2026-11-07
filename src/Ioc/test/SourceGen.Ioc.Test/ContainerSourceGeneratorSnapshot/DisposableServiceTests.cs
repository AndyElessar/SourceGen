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
        await result.VerifyCompilableAsync();
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
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithNonDisposableServices_OmitsTheirDisposeCalls()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlain { }
            public interface IInheritedDisposable { }
            public interface IAsyncOnly { }
            public interface IRepository<T> { }

            public abstract class DisposableBase : IDisposable
            {
                public void Dispose() { }
            }

            // Not disposable: no call
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlain)])]
            public class Plain : IPlain { }

            // Disposable through its base class
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IInheritedDisposable)])]
            public class InheritedDisposable : DisposableBase, IInheritedDisposable { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IAsyncOnly)])]
            public class AsyncOnly : IAsyncOnly, IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }

            // Closed from an open generic registration that is not disposable
            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class Consumer(IRepository<string> repository) { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithFactoryOrPartialServices_KeepsRuntimeDisposeChecks()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConnection { }
            public interface IPartialService { }
            public partial interface IExtensible { }

            public sealed class Connection : IConnection, IDisposable
            {
                public void Dispose() { }
            }

            public static class ConnectionFactory
            {
                // The declared return type is not disposable, the returned instance is
                public static IConnection Create() => new Connection();
            }

            // Another generator may add IDisposable to a partial type
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPartialService)])]
            public partial class PartialService : IPartialService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IExtensible)])]
            public class ExtensibleService : IExtensible { }

            [IocRegisterFor<IConnection>(ServiceLifetime.Singleton, Factory = nameof(ConnectionFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithDecorators_ChecksOutermostDecoratorDisposability()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }
            public interface IReader { }

            // Not disposable, wrapped by a disposable decorator
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Decorators = [typeof(DisposableDecorator)])]
            public class Handler : IHandler { }

            public class DisposableDecorator(IHandler inner) : IHandler, IDisposable
            {
                public void Dispose() { }
            }

            // Disposable, wrapped by a decorator that is not
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IReader)], Decorators = [typeof(PlainDecorator)])]
            public class Reader : IReader, IDisposable
            {
                public void Dispose() { }
            }

            public class PlainDecorator(IReader inner) : IReader { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithoutAnythingToDispose_GeneratesNonAsyncDisposeAsync()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlain { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlain)])]
            public class Plain : IPlain { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
