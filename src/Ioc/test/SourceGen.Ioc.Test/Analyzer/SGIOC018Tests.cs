namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC018: Unable to resolve service when IntegrateServiceProvider = false.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC018)]
public class SGIOC018Tests
{
    [Test]
    public async Task SGIOC018_UnregisteredDependency_WithIntegrateServiceProviderFalse_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IUnregisteredService unregistered) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).Contains("IUnregisteredService").And.Contains("TestContainer");
    }

    [Test]
    public async Task SGIOC018_RegisteredDependency_WithIntegrateServiceProviderFalse_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_UnregisteredDependency_WithIntegrateServiceProviderTrue_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IUnregisteredService unregistered) { }
            }

            [IocContainer(IntegrateServiceProvider = true)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_DefaultIntegrateServiceProvider_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IUnregisteredService unregistered) { }
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_IServiceProvider_WellKnownType_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IServiceProvider serviceProvider) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_IEnumerable_WithRegisteredElementType_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister<IPlugin>(ServiceLifetime.Singleton)]
            public class MyPlugin : IPlugin { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IEnumerable<IPlugin> plugins) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_IEnumerable_WithUnregisteredElementType_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IEnumerable<IPlugin> plugins) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).Contains("IEnumerable<IPlugin>");
    }

    [Test]
    public async Task SGIOC018_ParameterWithDefaultValue_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IOptionalService { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IOptionalService? optional = null) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_InjectedProperty_UnregisteredType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                [IocInject]
                public IUnregisteredService Dependency { get; set; } = default!;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).Contains("IUnregisteredService");
    }

    [Test]
    public async Task SGIOC018_InjectedMethod_UnregisteredParameter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize(IUnregisteredService dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).Contains("IUnregisteredService");
    }

    [Test]
    public async Task SGIOC018_MultipleUnregisteredDependencies_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered1 { }
            public interface IUnregistered2 { }

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IUnregistered1 dep1, IUnregistered2 dep2) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(2);
    }
}

