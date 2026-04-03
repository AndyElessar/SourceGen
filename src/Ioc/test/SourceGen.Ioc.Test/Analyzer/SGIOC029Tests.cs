namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC029: Unsupported async partial accessor type (e.g., ValueTask&lt;T&gt;).
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC029)]
public class SGIOC029Tests
{
    [Test]
    public async Task SGIOC029_PartialAccessorReturnsValueTask_ForAsyncInitService_ReportsDiagnostic()
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
                public partial ValueTask<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("ValueTask<IService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsTaskType_ForAsyncInitService_NoDiagnostic()
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

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsBareTask_ForAsyncInitService_ReportsDiagnostic()
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
                public partial Task GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(0);
        await Assert.That(sgioc021).Count().IsEqualTo(1);
        await Assert.That(sgioc021[0].GetMessage()).Contains("Task").And.Contains("GetService");
    }

    [Test]
    public async Task SGIOC029_NullableValueTaskAccessor_ForAsyncInitService_ReportsDiagnostic()
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
                public partial ValueTask<IService>? GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("ValueTask<IService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_AssemblyLevelRegisterFor_NonGeneric_ValueTaskPartialAccessor_ReportsDiagnostic()
    {
        // AsyncService is registered via assembly-level [IocRegisterFor] (non-generic) with async inject method.
        // Container partial accessor returns ValueTask<IService> (unsupported). SGIOC029 SHOULD fire.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.AsyncService), ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IService)])]

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
                public partial ValueTask<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("ValueTask<IService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsLazy_ForAsyncInitService_ReportsDiagnostic()
    {
        // Lazy<IService> on an async-init service should trigger SGIOC029.
        const string source = """
            using System;
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
                public partial Lazy<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Lazy<IService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsFunc_ForAsyncInitService_ReportsDiagnostic()
    {
        // Func<IService> on an async-init service should trigger SGIOC029.
        const string source = """
            using System;
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
                public partial Func<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Func<IService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_MixedSyncAndAsyncInitRegistrations_ReportsDiagnostic()
    {
        // When sync and async-init implementations share the same service type+key,
        // SGIOC029 should still report for non-Task async accessor shapes.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "mixed")]
            public class AsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "mixed")]
            public class SyncService : IMyService
            {
                [IocInject]
                public void Initialize() { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                [IocInject("mixed")]
                public partial Lazy<IMyService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Lazy<IMyService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_IntegrateServiceProviderTrue_ValueTaskAccessor_ForAsyncInitService_ReportsDiagnostic()
    {
        // SGIOC029 must fire for ALL containers regardless of IntegrateServiceProvider.
        // A ValueTask<T> return type for an async-init service is always an error — the generator
        // only supports Task<T> for async-init accessor methods.
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
                public partial ValueTask<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("ValueTask<IService>").And.Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsEnumerable_ForAsyncInitService_ReportsDiagnostic()
    {
        // IEnumerable<IService> on an async-init service should trigger SGIOC029.
        const string source = """
            using System.Collections.Generic;
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
                public partial IEnumerable<IService> GetServices();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsArray_ForAsyncInitService_ReportsDiagnostic()
    {
        // IService[] on an async-init service should trigger SGIOC029.
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
                public partial IService[] GetServices();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsTaskLazy_ForAsyncInitService_ReportsDiagnostic()
    {
        // Task<Lazy<IService>> (downgraded shape) on an async-init service should trigger SGIOC029.
        // The generator downgrades this shape to fallback, but SGIOC029 still fires on the async-init wrapper.
        const string source = """
            using System;
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
                public partial Task<Lazy<IService>> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsLazyTask_ForAsyncInitService_ReportsDiagnostic()
    {
        // Lazy<Task<IService>> (downgraded shape) on an async-init service should trigger SGIOC029.
        const string source = """
            using System;
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
                public partial Lazy<Task<IService>> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsValueTaskLazy_ForAsyncInitService_ReportsDiagnostic()
    {
        // ValueTask<Lazy<IService>> (nested ValueTask) on an async-init service should trigger SGIOC029.
        const string source = """
            using System;
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
                public partial ValueTask<Lazy<IService>> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_PartialAccessorReturnsFuncLazy_ForAsyncInitService_ReportsDiagnostic()
    {
        // Func<Lazy<IService>> (nested wrapper) on an async-init service should trigger SGIOC029.
        const string source = """
            using System;
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
                public partial Func<Lazy<IService>> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToList();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(sgioc029[0].GetMessage()).Contains("Task<T>");
    }

    [Test]
    public async Task SGIOC029_LazyWrapper_AsyncInitService_IntegrateServiceProviderTrue_ReportsDiagnostic()
    {
        // Lazy<IService> targeting an async-init service with IntegrateServiceProvider=true.
        // SGIOC029 fires for ALL containers — async-init wrapper diagnostics are owned by SGIOC029.
        const string source = """
            using System;
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
                public partial Lazy<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToArray();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC029_LazyWrapper_AsyncInitService_IntegrateServiceProviderFalse_ReportsDiagnostic()
    {
        // Lazy<IService> targeting an async-init service with IntegrateServiceProvider=false.
        // SGIOC029 fires (not SGIOC021) — async-init wrapper diagnostics are owned by SGIOC029.
        const string source = """
            using System;
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
                public partial Lazy<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029").ToArray();

        await Assert.That(sgioc029).Count().IsEqualTo(1);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }
}
