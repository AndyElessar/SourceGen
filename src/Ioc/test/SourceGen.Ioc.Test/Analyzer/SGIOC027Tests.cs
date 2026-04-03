namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC027: Partial accessor must return Task&lt;T&gt; for an async-init service.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC027)]
public class SGIOC027Tests
{
    [Test]
    public async Task SGIOC027_PartialAccessorReturnsSyncType_ForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService
            {
                [IocInject]
                public Task InitializeAsync(IService service) => Task.CompletedTask;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc027 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027").ToList();

        await Assert.That(sgioc027).Count().IsEqualTo(1);
        await Assert.That(sgioc027[0].GetMessage()).Contains("GetService").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC027_PartialAccessorReturnsTaskType_ForAsyncInitService_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Task<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC027_PartialAccessorReturnsSyncType_ForKeyedAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)], Key = "special")]
            public class AsyncService : IService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class SyncService : IService
            {
                [IocInject]
                public void Initialize() { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                [IocInject("special")]
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc027 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027").ToList();

        await Assert.That(sgioc027).Count().IsEqualTo(1);
        await Assert.That(sgioc027[0].GetMessage()).Contains("GetService").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC027_NullablePartialAccessor_ForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IService? GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc027 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027").ToList();

        await Assert.That(sgioc027).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC027_PartialAccessorReturnsSyncType_ForSyncOnlyService_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService
            {
                [IocInject]
                public void Initialize() { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC027_GenericTaskInjectMethod_DoesNotTriggerAsyncInit_NoDiagnostic()
    {
        // Task<T> is a generic method — only non-generic Task qualifies as async init.
        // A partial accessor returning the sync service type should NOT report SGIOC027.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService
            {
                [IocInject]
                public Task<int> SomeGenericMethod() => Task.FromResult(0);
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        // Task<T> is generic so the service is NOT async-init; no SGIOC027 should fire.
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC027_AssemblyLevelRegisterForGeneric_SyncPartialAccessor_ReportsDiagnostic()
    {
        // AsyncService is registered via assembly-level [IocRegisterFor<T>] (generic) with async inject method.
        // Container partial accessor returns synchronous IService. SGIOC027 SHOULD fire.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.AsyncService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IService)])]

            namespace TestNamespace;

            public interface IService { }

            public class AsyncService : IService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc027 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027").ToList();

        await Assert.That(sgioc027).Count().IsEqualTo(1);
        await Assert.That(sgioc027[0].GetMessage()).Contains("GetService").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC027_IntegrateServiceProviderTrue_SyncPartialAccessor_ForAsyncInitService_ReportsDiagnostic()
    {
        // SGIOC027 must fire for ALL containers regardless of IntegrateServiceProvider.
        // Returning the sync service type for an async-init service is always a semantic error —
        // the generator cannot produce a sync accessor for an async-init service.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer(IntegrateServiceProvider = true)]
            public partial class TestContainer
            {
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc027 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027").ToList();

        await Assert.That(sgioc027).Count().IsEqualTo(1);
        await Assert.That(sgioc027[0].GetMessage()).Contains("GetService").And.Contains("IService");
    }
}

