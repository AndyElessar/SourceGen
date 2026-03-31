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
}
