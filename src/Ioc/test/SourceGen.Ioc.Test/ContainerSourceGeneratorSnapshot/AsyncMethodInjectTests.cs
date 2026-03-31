namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for async method injection container generation (Phase 3B).
/// Verifies that async-init services generate <c>Task&lt;ImplType&gt;</c> cached fields,
/// async routing resolver methods, and async creation methods.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.AsyncMethodInject)]
public class AsyncMethodInjectTests
{
    private const string AsyncMethodInjectFeatures = "Register,Container,PropertyInject,FieldInject,MethodInject,AsyncMethodInject";

    /// <summary>
    /// Suppressed diagnostics for partial accessor tests:
    /// CS8795 (partial method must have implementation) and CS9248 (partial property must have implementation).
    /// These are expected because the source generator provides the implementation.
    /// </summary>
    private static readonly IReadOnlySet<string> SuppressedPartialDiagnostics = new HashSet<string>(["CS8795", "CS9248"]);

    // ─────────────────────────────────────────────────────────────────────────
    // Basic async resolver generation
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_SingletonWithAsyncInit_GeneratesAsyncResolver_None()
    {
        // A singleton with a single async-init method should get a Task<T>? field
        // and async routing + creation methods. With ThreadSafeStrategy.None, no semaphore.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync(IDependency dep) { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_SingletonWithAsyncInit_GeneratesAsyncResolver_SemaphoreSlim()
    {
        // A singleton with a single async-init method and SemaphoreSlim strategy should get
        // a Task<T>? field, a SemaphoreSlim field, and the async routing body uses WaitAsync().
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync(IDependency dep) { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_TransientWithAsyncInit_GeneratesAsyncCreateMethod()
    {
        // A transient async-init service produces only a creation method (no caching field).
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync(IDependency dep) { }
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Mixed injection: property + sync method + async method
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_MixedInjection_GeneratesAllCallsInOrder()
    {
        // Property injection → sync method call → await async method call, all in CreateAsync.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDep1 { }
            public interface IDep2 { }
            public interface IDep3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep3 : IDep3 { }

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IDep1 Dep1 { get; set; } = default!;

                [IocInject]
                public void SyncInit(IDep2 dep2) { }

                [IocInject]
                public async Task AsyncInit(IDep3 dep3) { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared field deduplication — multiple service aliases share ONE Task<ImplType> field
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_MultipleServiceTypes_ShareSingleTaskField()
    {
        // MyService registered as both IFoo and IBar.
        // Both aliases share the same Task<MyService>? field and resolver method.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo { }
            public interface IBar { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo), typeof(IBar)])]
            public class MyService : IFoo, IBar
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Eager resolve exclusion — async-init services must NOT be in constructor init
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_AsyncService_ExcludedFromEagerInit()
    {
        // EagerResolveOptions.Singleton is set, but the async-init service must NOT be eager.
        // The sync-only SyncService IS eager. Verify that only SyncService appears in the ctor.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IAsyncService { }
            public interface ISyncService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncService)])]
            public class AsyncService : IAsyncService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISyncService)])]
            public class SyncService : ISyncService { }

            [IocContainer(EagerResolveOptions = EagerResolveOptions.Singleton, ThreadSafeStrategy = ThreadSafeStrategy.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Collection exclusion — async-init services must NOT appear in collection resolvers
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_AsyncService_ExcludedFromCollectionResolver()
    {
        // Multiple IMyService registrations — only the sync one appears in the array resolver.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class SyncImpl : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class AsyncImpl : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Task<T> wrapper resolution
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_TaskDependency_AsyncInitService_UsesAsyncResolver()
    {
        // A consumer takes Task<IMyService> — should resolve via async/await projection (not ContinueWith)
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Task<IMyService> service) { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_TaskDependency_SyncService_UsesTaskFromResult()
    {
        // A consumer takes Task<ISyncService> — should resolve via Task.FromResult(GetSync())
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ISyncService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISyncService)])]
            public class SyncService : ISyncService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Task<ISyncService> service) { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Partial accessor with Task<T> return type
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_PartialTaskAccessor_GeneratesAsyncMethod()
    {
        // A partial method returning Task<IMyService> → generated as async + await.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer
            {
                public partial Task<IMyService> GetMyServiceAsync();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Decorator support for async-init services
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_WithDecorator_GeneratesAsyncResolverWithDecoratorApplication()
    {
        // An async-init service with a decorator:
        // - the creation method must await the async member before applying the decorator.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            public class MyServiceDecorator(IMyService inner) : IMyService { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_NonGenericTaskDependency_TreatedAsPlainServiceInContainer()
    {
        // Non-generic Task (arity 0) must NOT be classified as WrapperKind.Task in the
        // container output path. The container should resolve it as a plain service, not
        // attempt to unwrap a Task<T>.InnerType (which would cause a NullReferenceException).
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Task nonGenericTask)
            {
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        // Generator must not throw NRE — arity-0 Task is a plain service dependency.
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
