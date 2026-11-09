namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for AsyncMethodInject feature — async method injection code generation.
/// Verifies that the source generator emits correct Task&lt;T&gt; registrations with
/// async local Init() functions when services have async-inject methods.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.AsyncMethodInject)]
public class AsyncMethodInjectTests
{
    private const string AsyncMethodInjectFeatures = "Register,Container,PropertyInject,FieldInject,MethodInject,AsyncMethodInject";

    [Test]
    public async Task AsyncMethodInject_BasicAsyncMethod_GeneratesTaskRegistration()
    {
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
                public async Task InitAsync(IDependency dep)
                {
                    // async init
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_MultipleAsyncMethods_GeneratesTaskRegistration()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency1 { }
            public interface IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitStep1(IDependency1 dep1) { }

                [IocInject]
                public async Task InitStep2(IDependency2 dep2) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_MixedSyncAndAsyncMethods_GeneratesTaskRegistration()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency1 { }
            public interface IDependency2 { }
            public interface IDependency3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency3 : IDependency3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IDependency1 Dep1 { get; init; }

                [IocInject]
                public void SyncInit(IDependency2 dep2) { }

                [IocInject]
                public async Task AsyncInit(IDependency3 dep3) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_TaskWrapperDependency_AsyncInitService_ResolvesDirectly()
    {
        // Consumer takes Task<IMyService> — resolved as sp.GetRequiredService<Task<IMyService>>()
        // because MyService is async-init.
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

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Task<IMyService> lazyService)
            {
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_NonGenericTaskDependency_TreatedAsPlainService()
    {
        // Non-generic Task (arity 0) must NOT be classified as WrapperKind.Task.
        // Before the fix, GetNonCollectionWrapperKind("Task") returned WrapperKind.Task
        // for arity-0 Task, causing a NullReferenceException when TaskTypeData.InnerType
        // tried to access TypeParameters![0].
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Task nonGenericTask)
            {
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        // Generator must not throw NRE — arity-0 Task is a plain service dependency.
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_TaskWrapperDependency_SyncService_UsesTaskFromResult()
    {
        // Consumer takes Task<ISyncService> — resolved as Task.FromResult(sp.GetRequiredService<ISyncService>())
        // because SyncService is NOT async-init.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ISyncService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISyncService)])]
            public class SyncService : ISyncService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Task<ISyncService> taskService)
            {
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_WithDecorator_RegistersTaskOfDecoratedServiceType()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)], Key = "keyed", Decorators = [typeof(MyServiceDecorator)])]
            public class KeyedMyService : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            public class MyServiceDecorator(IMyService inner, ILogger logger) : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_AsyncDecoratorOnSyncImplementation_RegistersTaskOfDecoratedServiceType()
    {
        // The implementation type stays synchronous; only the decorated service type is registered as Task<T>.
        // Decorators are applied innermost first, each one's async methods awaited before the next one wraps it.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IOther { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDependency)])]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService), typeof(IOther)], Decorators = [typeof(OuterDecorator), typeof(InnerDecorator)])]
            public class MyService : IMyService, IOther { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)], Key = "keyed", Decorators = [typeof(InnerDecorator)])]
            public class KeyedMyService : IMyService { }

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
            public class Consumer(MyService implementation, IOther other, Task<IMyService> decorated, Func<IOther> otherFactory);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_AsyncDecoratorWithFeatureDisabled_RegistersSynchronousDecoratorChain()
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
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,MethodInject"
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_FuncWithInputParametersOfAsyncInitService_IsNotRegistered()
    {
        // A Func with input parameters constructs the instance synchronously, so it cannot initialize an async-init service.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IPlainService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService(string name) : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public async Task InitAsync() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IPlainService)])]
            public class PlainService(string name) : IPlainService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Func<string, IMyService> factory, Func<string, IPlainService> plainFactory);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task AsyncMethodInject_TaskOfServiceWithAsyncAndSyncKeys_ResolvesEachKeyByItsOwnShape()
    {
        // IMyService is async-init without a key and synchronous with the "sync" key, and the reverse for IOtherService.
        // A Task<T> dependency is resolved as Task<T> only for the async-init (service type, key) pair.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IOtherService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(AsyncDecorator)])]
            public class AsyncService : IMyService { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Key = "sync")]
            public class SyncService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IOtherService)], Key = "async")]
            public class AsyncOtherService : IOtherService
            {
                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IOtherService)])]
            public class SyncOtherService : IOtherService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(
                Task<IMyService> asyncService,
                [FromKeyedServices("sync")] Task<IMyService> syncService,
                [FromKeyedServices("async")] Task<IOtherService> asyncOther,
                Task<IOtherService> syncOther);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = AsyncMethodInjectFeatures
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource)
            .Contains("sp.GetRequiredService<global::System.Threading.Tasks.Task<global::TestNamespace.IMyService>>()")
            .And.Contains("global::System.Threading.Tasks.Task.FromResult(sp.GetRequiredKeyedService<global::TestNamespace.IMyService>(\"sync\"))")
            .And.Contains("sp.GetRequiredKeyedService<global::System.Threading.Tasks.Task<global::TestNamespace.IOtherService>>(\"async\")")
            .And.Contains("global::System.Threading.Tasks.Task.FromResult(sp.GetRequiredService<global::TestNamespace.IOtherService>())");
        await Verify(generatedSource);
    }
}
