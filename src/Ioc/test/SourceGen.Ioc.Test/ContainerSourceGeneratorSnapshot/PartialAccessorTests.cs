namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for partial accessor (fast-path service resolution) generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.PartialAccessor)]
public class PartialAccessorTests
{
    /// <summary>
    /// Suppressed diagnostics for initial compilation: 
    /// CS8795 (partial method must have implementation) and CS9248 (partial property must have implementation).
    /// These are expected because the source generator provides the implementation.
    /// </summary>
    private static readonly IReadOnlySet<string> SuppressedPartialDiagnostics = new HashSet<string>(["CS8795", "CS9248"]);

    [Test]
    public async Task PartialMethod_ResolvesRegisteredService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial global::TestNamespace.IMyService GetMyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_TaskOfSynchronousService_ReturnsCompletedTask()
    {
        // Only an async-init service is awaited; a synchronous service is wrapped without the 'async' modifier
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial global::System.Threading.Tasks.Task<global::TestNamespace.IMyService> GetMyServiceAsync();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialProperty_ResolvesRegisteredService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IMyService MyService { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_NullableReturnType_ResolvesOptionally()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IMyService? TryGetMyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_WithKeyedService_ResolvesWithKey()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "redis", ServiceTypes = [typeof(ICache)])]
            public class RedisCache : ICache { }

            [IocContainer]
            public partial class TestContainer
            {
                [IocInject("redis")]
                public partial ICache GetRedisCache();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_NamingConflict_RenamesInternalResolver()
    {
        // Without namespace, the generated resolver method name becomes GetMyService(),
        // which conflicts with the user-declared partial method GetMyService().
        // The generator should rename the internal resolver to GetMyService_Resolve().
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IMyService GetMyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_NoFallback_UnregisteredService_UsesRequiredServiceLookup()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnknownService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IUnknownService GetUnknownService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialProperty_WithKeyedService_ResolvesWithKey()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "memory", ServiceTypes = [typeof(ICache)])]
            public class MemoryCache : ICache { }

            [IocContainer]
            public partial class TestContainer
            {
                [IocInject("memory")]
                public partial ICache MemoryCache { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialAccessor_MixedMethodsAndProperties()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IServiceA GetServiceA();
                public partial IServiceB ServiceB { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialAccessor_NoFallback_WrapperOfRegisteredService_ResolvesWrapper()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Lazy<IMyService> GetLazyService();
                public partial Func<IMyService> GetServiceFactory();
                public partial IEnumerable<IMyService> GetServices();
                public partial IMyService[] ServiceArray { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialAccessor_NoFallback_ServiceOnlyInImportedModule_ResolvesFromModule()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IModuleService { }

            public class ModuleService : IModuleService { }

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor<ModuleService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IModuleService)])]
            [IocRegisterFor<ModuleService>(ServiceLifetime.Singleton, Key = "keyed", ServiceTypes = [typeof(IModuleService)])]
            public partial class ServiceModule { }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocImportModule<ServiceModule>]
            public partial class TestContainer
            {
                public partial IModuleService GetModuleService();
                public partial IModuleService? TryGetModuleService();
                public partial Task<IModuleService> GetModuleServiceAsync();
                public partial Task<IModuleService>? TryGetModuleServiceAsync();

                [IocInject("keyed")]
                public partial IModuleService GetKeyedModuleService();

                [IocInject("keyed")]
                public partial Task<IModuleService> GetKeyedModuleServiceAsync();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "TestContainer.Container.g.cs");

        await Verify(generatedSource);
    }
}

