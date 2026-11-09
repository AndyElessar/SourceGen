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
    public async Task AsyncMethodInject_DisposableAndNonDisposableServices_DisposesOnlyDisposableTasks()
    {
        // Only the disposable async-init service gets a dispose call, through the Task<T> overloads
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlainAsync { }
            public interface IDisposableAsync { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IPlainAsync)])]
            public class PlainAsync : IPlainAsync
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IDisposableAsync)])]
            public class DisposableAsync : IDisposableAsync, IAsyncDisposable
            {
                [IocInject]
                public async Task InitAsync() { }

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
        // a Task<T>? field, a SemaphoreSlim field, a non-async routing fast path,
        // and an async first-initialization method that uses WaitAsync().
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
    public async Task AsyncMethodInject_SingletonWithAsyncInit_GeneratesAsyncResolver_Lock()
    {
        // With Lock strategy, the async-init singleton keeps Lock: the non-async first-initialization
        // method starts the task inside lock(...) without awaiting it.
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

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock, EagerResolveOptions = EagerResolveOptions.None)]
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
    [Arguments("SpinLock")]
    [Arguments("CompareExchange")]
    public async Task AsyncMethodInject_SpinLockOrCompareExchange_AsyncInitUsesLock(string strategy)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class MyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.{{strategy}}, EagerResolveOptions = EagerResolveOptions.None)]
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

        // The lock is allocated on the first slow path
        await Assert.That(generatedSource).Contains("private global::System.Threading.Lock? _myServiceLock;");
        await Assert.That(generatedSource).Contains("lock(_myServiceLock ?? __CreateLock(ref _myServiceLock))");
        await Assert.That(generatedSource).DoesNotContain("SpinLock");
        await Assert.That(generatedSource).DoesNotContain("Interlocked.CompareExchange(ref _myService");
        await Verify(generatedSource).UseParameters(strategy);
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
    // Async-init eager init — fire-and-forget startup follows EagerResolveOptions
    // ─────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncMethodInject_SingletonAsyncInit_EagerSingleton_FiresInConstructor()
    {
        // EagerResolveOptions.Singleton eagerly starts async-init singletons in the root ctor.
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

    [Test]
    public async Task AsyncMethodInject_SingletonAsyncInit_EagerNone_DoesNotFireInConstructor()
    {
        // EagerResolveOptions.None keeps async-init singletons lazy too.
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

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None, ThreadSafeStrategy = ThreadSafeStrategy.None)]
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
    public async Task AsyncMethodInject_ScopedAsyncInit_EagerScoped_FiresInScopeConstructor()
    {
        // EagerResolveOptions.Scoped eagerly starts async-init scoped services in child scopes.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IAsyncScopedService { }
            public interface ISyncScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IAsyncScopedService)])]
            public class AsyncScopedService : IAsyncScopedService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ISyncScopedService)])]
            public class SyncScopedService : ISyncScopedService { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.Scoped)]
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
    public async Task AsyncMethodInject_ScopedAsyncInit_EagerNone_DoesNotFireInScopeConstructor()
    {
        // EagerResolveOptions.None keeps async-init scoped services lazy.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IAsyncScopedService { }
            public interface ISyncScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IAsyncScopedService)])]
            public class AsyncScopedService : IAsyncScopedService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ISyncScopedService)])]
            public class SyncScopedService : ISyncScopedService { }

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
    public async Task AsyncMethodInject_TaskDependency_TransientAsyncInitService_UsesCreateMethod()
    {
        // Transient async-init services have no cached Get…Async resolver, only the Create…Async method
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Task<IMyService> service) { }

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
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
        // A partial method returning Task<IMyService> → returns the cached Task<IMyService> projection (no async).
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

    [Test]
    [Arguments("Singleton")]
    [Arguments("Transient")]
    public async Task AsyncMethodInject_SynchronousPartialAccessor_BlocksOnInitialization(string lifetime)
    {
        // A synchronous accessor of an async-init service (reported by SGIOC027) still compiles: it blocks on the task
        var source = $$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.{{lifetime}}, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.None, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer
            {
                public partial IMyService GetMyService();
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

        await Verify(generatedSource).UseParameters(lifetime);
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
    public async Task AsyncMethodInject_WithDecorator_ResolvesImplementationTypeUndecorated()
    {
        // Task<MyService> resolves the undecorated instance; Task<IMyService> awaits it and applies the decorator.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface ITransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            public class MyServiceDecorator(IMyService inner) : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)], Decorators = [typeof(TransientServiceDecorator)])]
            public class TransientService : ITransientService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            public class TransientServiceDecorator(ITransientService inner) : ITransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Task<MyService> service, Task<IMyService> decorated, Task<TransientService> transient, Task<ITransientService> transientDecorated);

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
    [Arguments("Singleton", "Lock")]
    [Arguments("Scoped", "SemaphoreSlim")]
    [Arguments("Transient", "None")]
    public async Task AsyncMethodInject_AsyncDecoratorOnSyncImplementation_AwaitsDecoratorAfterSyncInnerInstance(
        string lifetime,
        string strategy)
    {
        // The implementation type stays synchronous; only the decorated service type becomes async-init.
        // Decorators are applied innermost first, each one's async methods awaited before the next one wraps it.
        var source = $$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDependency)])]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.{{lifetime}}, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(OuterDecorator), typeof(InnerDecorator)])]
            public class MyService : IMyService { }

            public class OuterDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public void Initialize() { }
            }

            public class InnerDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public IDependency? Dependency { get; set; }

                [IocInject]
                public void Initialize(IDependency dependency) { }

                [IocInject]
                public async Task InitializeAsync(IDependency dependency) { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(MyService implementation, Task<IMyService> decorated);

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.{{strategy}}, EagerResolveOptions = EagerResolveOptions.None)]
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

        await Verify(generatedSource).UseParameters(lifetime, strategy);
    }

    [Test]
    public async Task AsyncMethodInject_AsyncDecoratorOnAsyncImplementation_AwaitsImplementationThenDecorator()
    {
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

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock, EagerResolveOptions = EagerResolveOptions.Singleton)]
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
    public async Task AsyncMethodInject_OpenGenericDisposableAsyncDecoratorFromDefaults_AppliesOnlyWhereConstraintIsSatisfied()
    {
        // The default decorator only closes for handlers whose request is IValidated; it is disposable, so the cached
        // Task<IHandler<ValidRequest>> field is disposed after it completes.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IHandler<>), ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.ValidationDecorator<>)])]

            namespace TestNamespace;

            public interface IValidated { }
            public interface IHandler<TRequest> { }

            public class ValidRequest : IValidated { }
            public class OtherRequest { }

            [IocRegister]
            public class ValidHandler : IHandler<ValidRequest> { }

            [IocRegister]
            public class OtherHandler : IHandler<OtherRequest> { }

            public sealed class ValidationDecorator<TRequest>(IHandler<TRequest> inner) : IHandler<TRequest>, IDisposable
                where TRequest : IValidated
            {
                [IocInject]
                public async Task InitializeAsync() { }

                public void Dispose() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Task<IHandler<ValidRequest>> valid, IHandler<OtherRequest> other);

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
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
    public async Task AsyncMethodInject_AsyncDecoratorOnOneServiceType_KeepsOtherServiceTypesSynchronous()
    {
        // A decorator only applies to the service types it implements, so only IDecorated becomes async-init.
        const string source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDecorated { }
            public interface IPlain { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDecorated), typeof(IPlain)], Decorators = [typeof(AsyncDecorator)])]
            public class MyService : IDecorated, IPlain { }

            public class AsyncDecorator(IDecorated inner) : IDecorated
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(IPlain plain, IEnumerable<IPlain> plains, Task<IDecorated> decorated);

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
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
    public async Task AsyncMethodInject_AsyncDecoratorWithFeatureDisabled_KeepsServiceSynchronous()
    {
        // Without AsyncMethodInject the decorator's Task method is ignored (SGIOC022), so the service stays synchronous.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public void Initialize() { }

                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,MethodInject"
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_FuncWithInputParametersOfAsyncInitService_FallsBackToProvider()
    {
        // A Func with input parameters constructs the instance synchronously, so it cannot initialize an async-init service.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService(string name) : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Func<string, IMyService> factory);

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
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

    [Test]
    public async Task AsyncMethodInject_TransientPartialTaskAccessor_ReturnsProjectedTaskWithoutAsync()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocContainer(EagerResolveOptions = EagerResolveOptions.None)]
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

        // Transient tasks are never cached, so they are projected per resolution, without an async accessor
        await Assert.That(generatedSource).Contains("public partial global::System.Threading.Tasks.Task<global::TestNamespace.IMyService> GetMyServiceAsync() => __ProjectTask<global::TestNamespace.IMyService, global::TestNamespace.MyService>(CreateMyService_ResolveAsync());");
        await Assert.That(generatedSource).DoesNotContain("Task_As_");
        await Verify(generatedSource);
    }

    [Test]
    [Arguments("InitMyServiceAsync", "InitMyService_ResolveAsync()")]
    [Arguments("GetMyServiceAsync_As_IMyService", "GetMyServiceAsync_As_IMyService_Resolve()")]
    public async Task AsyncMethodInject_UserMemberNamedLikeDerivedMethod_GetsResolveSuffix(string userMember, string expectedCall)
    {
        var source = $$"""
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

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class TestContainer
            {
                private void {{userMember}}() { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains(expectedCall);
        await Verify(generatedSource).UseParameters(userMember);
    }

    [Test]
    [Arguments("MyServiceTask_As_IMyService", "_myServiceTask_As_IMyService_Resolve")]
    [Arguments("MyServiceAsync_As_IMyService", "GetMyServiceAsync_As_IMyService_Resolve()")]
    public async Task AsyncMethodInject_ServiceNamedLikeProjection_ProjectionGetsResolveSuffix(string serviceName, string expectedMember)
    {
        var source = $$"""
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
            public class {{serviceName}} { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.Lock, EagerResolveOptions = EagerResolveOptions.None)]
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

        await Assert.That(generatedSource).Contains(expectedMember);
        await Verify(generatedSource).UseParameters(serviceName);
    }
}
