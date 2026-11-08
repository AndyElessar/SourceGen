using Microsoft.CodeAnalysis.Diagnostics;

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

    [Test]
    public async Task SGIOC018_AssemblyLevelRegisterFor_NonGeneric_DependencyRegistered_NoDiagnostic()
    {
        // IDependency is registered via assembly-level [IocRegisterFor] (non-generic).
        // IMyService depends on IDependency. SGIOC018 should NOT fire.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyDependency), ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IDependency)])]

            namespace TestNamespace;

            public interface IDependency { }

            public class MyDependency : IDependency { }

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
    [Arguments("RegisterAllBaseClasses = true")]
    [Arguments("typeof(HandlerBase)")]
    public async Task SGIOC018_BaseClassServiceTypeConstructor_NoDiagnostic(string registration)
    {
        // The generator only calls the implementation constructor; the base constructor arguments come from it.
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public sealed class HandlerOptions { }

            public class HandlerBase
            {
                public HandlerBase(HandlerOptions options) { }
            }

            [IocRegister(ServiceLifetime.Singleton, {{registration}})]
            public class MyHandler : HandlerBase
            {
                public MyHandler() : base(new HandlerOptions()) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_BaseClassServiceTypeInjectedProperty_NoDiagnostic()
    {
        // The generator only injects members declared on the implementation type.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            public abstract class HandlerBase
            {
                [IocInject]
                public IUnregistered Dependency { get; set; } = null!;
            }

            [IocRegister(ServiceLifetime.Singleton, RegisterAllBaseClasses = true)]
            public class MyHandler : HandlerBase { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018");

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_MultipleContainersWithIntegrateServiceProviderFalse_ReportsDiagnosticForEachContainer()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class MyService
            {
                public MyService(IUnregisteredService unregistered) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class FirstContainer { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class SecondContainer { }

            [IocContainer]
            public partial class FallbackContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var messages = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018")
            .Select(static d => d.GetMessage())
            .OrderBy(static m => m, StringComparer.Ordinal)
            .ToList();

        await Assert.That(messages).IsEquivalentTo(
        [
            "Unable to resolve service 'IUnregisteredService' for container 'FirstContainer'",
            "Unable to resolve service 'IUnregisteredService' for container 'SecondContainer'"
        ]);
    }

    [Test]
    public async Task SGIOC018_ExplicitOnly_UnresolvedDependencyOfOtherRegistration_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IExternalService { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class FallbackService
            {
                public FallbackService(IExternalService external) { }
            }

            public class StandaloneService { }

            [IocContainer]
            public partial class FallbackContainer { }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor<StandaloneService>(ServiceLifetime.Singleton)]
            public partial class StandaloneContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_ExplicitOnly_DependencyRegisteredOutsideContainer_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            public class Consumer
            {
                public Consumer(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor<Consumer>(ServiceLifetime.Singleton)]
            public partial class StandaloneContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IDependency' for container 'StandaloneContainer'");
    }

    [Test]
    public async Task SGIOC018_ExplicitOnly_DependencyRegisteredOnContainerWithAllInterfaces_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            public class Dependency : IFirst, ISecond { }

            public class Consumer
            {
                public Consumer(ISecond second) { }
            }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor<Dependency>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFirst)], RegisterAllInterfaces = true)]
            [IocRegisterFor<Consumer>(ServiceLifetime.Singleton)]
            public partial class StandaloneContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_IncludeTags_UnresolvedDependencyOfExcludedRegistration_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IExternalService { }

            [IocRegister(ServiceLifetime.Singleton, Tags = ["Other"])]
            public class OtherService
            {
                public OtherService(IExternalService external) { }
            }

            [IocRegister(ServiceLifetime.Singleton, Tags = ["Feature"])]
            public class FeatureService { }

            [IocContainer(IntegrateServiceProvider = false, IncludeTags = ["Feature"])]
            public partial class FeatureContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_IncludeTags_DependencyWithoutMatchingTag_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton, Tags = ["Feature"])]
            public class FeatureService
            {
                public FeatureService(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false, IncludeTags = ["Feature"])]
            public partial class FeatureContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IDependency' for container 'FeatureContainer'");
    }

    [Test]
    public async Task SGIOC018_IncludeTags_TagsFromRegisterDefaults_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IDependency>(ServiceLifetime.Singleton, Tags = ["Feature"])]

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton, Tags = ["Feature"])]
            public class FeatureService
            {
                public FeatureService(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false, IncludeTags = ["Feature"])]
            public partial class FeatureContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_RegisterDefaultsTargetServiceType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IDependency>(ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_ImportedModuleInSameAssembly_ProvidesDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            public class Dependency : IDependency { }

            public class Consumer
            {
                public Consumer(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor<Dependency>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IDependency)])]
            public partial class DependencyModule { }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocImportModule<DependencyModule>]
            [IocRegisterFor<Consumer>(ServiceLifetime.Singleton)]
            public partial class AppContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_ImportedModuleFromOtherAssembly_NoDiagnostic()
    {
        const string moduleSource = """
            using SourceGen.Ioc;

            namespace ModuleNamespace;

            [IocContainer]
            public partial class SharedModule { }
            """;

        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IModuleService { }

            public class Consumer
            {
                public Consumer(IModuleService service) { }
            }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocImportModule<ModuleNamespace.SharedModule>]
            [IocRegisterFor<Consumer>(ServiceLifetime.Singleton)]
            public partial class AppContainer { }
            """;

        var moduleCompilation = SourceGeneratorTestHelper.CreateCompilation("ModuleAssembly", moduleSource);
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [moduleCompilation.ToMetadataReference()]);
        var diagnostics = await compilation.WithAnalyzers([new ContainerAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }
}

