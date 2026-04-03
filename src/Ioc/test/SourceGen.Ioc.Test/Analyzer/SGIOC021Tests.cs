namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC021: Unable to resolve partial accessor service.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC021)]
public class SGIOC021Tests
{
    // ── Positive tests (SGIOC021 expected) ────────────────────────────────────

    [Test]
    public async Task SGIOC021_PartialMethod_UnregisteredService_ReportsDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IUnregistered GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_PartialProperty_UnregisteredService_ReportsDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IUnregistered Service { get; }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_TaskWrapper_UnregisteredInnerType_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Task<IUnregistered> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_LazyWrapper_UnregisteredInnerType_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Lazy<IUnregistered> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_FuncWrapper_UnregisteredInnerType_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Func<IUnregistered> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_MultiArgFuncWrapper_UnregisteredInnerType_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Func<string, IUnregistered> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_EnumerableWrapper_UnregisteredInnerType_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IEnumerable<IUnregistered> GetServices();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_KeyedPartialMethod_UnregisteredService_ReportsDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                [IocInject("mykey")]
                public partial IUnregistered GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_KeyedAccessor_UnkeyedAsyncInitService_ReportsDiagnostic()
    {
        // Unkeyed async-init registration exists, but accessor asks for a keyed service.
        // SGIOC021 should still report because keyed registration is missing.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.MyAsyncService>(ServiceTypes = [typeof(TestNamespace.IMyService)])]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyAsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                [FromKeyedServices("myKey")]
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

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(1);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029")).Count().IsEqualTo(0);
    }

    // ── Positive tests — unsupported return types ──────────────────────────────

    [Test]
    public async Task SGIOC021_NonGenericTask_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

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
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_NonGenericValueTask_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial ValueTask GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_ValueTaskWrapper_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

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
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    // ── Negative tests (NO SGIOC021 expected) ─────────────────────────────────

    [Test]
    public async Task SGIOC021_RegisteredService_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_NullableUnregistered_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IUnregistered? Service { get; }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_IntegrateServiceProviderTrue_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = true)]
            public partial class TestContainer
            {
                public partial IUnregistered GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_EnumerableRegistered_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IEnumerable<IService> GetServices();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_AlwaysResolvableType_NoDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IServiceProvider GetServiceProvider();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_TaskWrapperRegistered_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

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

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_LazyWrapperRegistered_NoDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Lazy<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_IntegrateServiceProvider_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = true)]
            public partial class TestContainer
            {
                public partial IUnregistered GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_AssemblyLevelRegisterForGeneric_PartialAccessorRegistered_NoDiagnostic()
    {
        // IMyService is registered via assembly-level [IocRegisterFor<T>] (generic).
        // Container partial accessor returns IMyService. SGIOC021 should NOT fire.
        const string source = """
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.MyService>(ServiceTypes = [typeof(TestNamespace.IMyService)])]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IMyService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    // ── Recursive wrapper unwrap tests ───────────────────────────────────────

    [Test]
    public async Task SGIOC021_NestedLazyFunc_RegisteredInnerService_NoDiagnostic()
    {
        // Lazy<Func<IService>> — recursive unwrap reaches registered IService → no diagnostic.
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Lazy<Func<IService>> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_NestedLazyFunc_UnregisteredInnerService_ReportsDiagnostic()
    {
        // Lazy<Func<IUnregistered>> — recursive unwrap reaches unregistered service → SGIOC021.
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial Lazy<Func<IUnregistered>> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_TaskNestedWrapper_DowngradedShape_ReportsDiagnostic()
    {
        // Task<Lazy<IService>> — downgrade rule: Task<Wrapper> is unresolvable without IServiceProvider.
        // Even though IService is registered, the generator falls back for nested-Task shapes.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

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
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    // ── SGIOC021 negative tests — downgraded async-init shapes (SGIOC029 fires, not SGIOC021) ──

    [Test]
    public async Task SGIOC021_TaskLazy_AsyncInitService_IntegrateServiceProviderFalse_NoDiagnostic()
    {
        // Task<Lazy<IService>> (downgraded shape) on an async-init service.
        // SGIOC029 fires for this shape; SGIOC021 should NOT fire.
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

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_LazyTask_AsyncInitService_IntegrateServiceProviderFalse_NoDiagnostic()
    {
        // Lazy<Task<IService>> (downgraded shape) on an async-init service.
        // SGIOC029 fires for this shape; SGIOC021 should NOT fire.
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

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_ValueTaskLazy_AsyncInitService_IntegrateServiceProviderFalse_NoDiagnostic()
    {
        // ValueTask<Lazy<IService>> on an async-init service.
        // SGIOC029 fires for this shape; SGIOC021 should NOT fire.
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

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC021_CollectionAtTopNestedWrappers_ReportsDiagnostic()
    {
        // IEnumerable<Lazy<Func<IService>>> — collection-at-top downgrade rule fires because
        // IEnumerable<WrapperType> resolves to GetServices<WrapperType>() which requires IServiceProvider.
        const string source = """
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IEnumerable<Lazy<Func<IService>>> GetServices();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_LazyTaskWrapper_DowngradedShape_ReportsDiagnostic()
    {
        // Lazy<Task<IService>> — downgrade rule: Wrapper<Task> is unresolvable without IServiceProvider.
        // Even though IService is registered, the generator falls back for Wrapper<Task<T>> shapes.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

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
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC021_ValueTaskNestedWrapper_ReportsDiagnostic()
    {
        // ValueTask<Lazy<IService>> — ValueTask is not a generator-supported recursive wrapper.
        // GetAccessorServiceType unwraps once to Lazy<IService>, which is not a registered type → SGIOC021.
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class TestService : IService { }

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
        var sgioc021 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC021").ToArray();

        await Assert.That(sgioc021).Count().IsEqualTo(1);
    }
}
