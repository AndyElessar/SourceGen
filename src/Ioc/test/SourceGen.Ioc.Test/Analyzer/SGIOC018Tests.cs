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
    public async Task SGIOC018_OpenGenericServiceTypeOnClosedImplementation_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class StringHandler : IHandler<string> { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IHandler<string> handler) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_RegisterDefaultsImplementationTypes_ProvidesDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IDependency>(ServiceLifetime.Singleton, ImplementationTypes = [typeof(TestNamespace.Dependency)])]

            namespace TestNamespace;

            public interface IDependency { }

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
    public async Task SGIOC018_RegisterDefaultsImplementationTypes_OpenGenericTarget_ProvidesClosedDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IHandler<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(TestNamespace.StringHandler)])]

            namespace TestNamespace;

            public interface IHandler<T> { }

            public class StringHandler : IHandler<string> { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IHandler<string> handler) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_RegisterDefaultsImplementationTypes_UnregisteredDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IDependency>(ServiceLifetime.Singleton, ImplementationTypes = [typeof(TestNamespace.Dependency)])]

            namespace TestNamespace;

            public interface IDependency { }
            public interface IUnregistered { }

            public class Dependency : IDependency
            {
                public Dependency(IUnregistered unregistered) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IUnregistered' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_IncludeTags_RegisterDefaultsImplementationTypesWithMatchingTag_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IDependency>(ServiceLifetime.Singleton, Tags = ["Feature"], ImplementationTypes = [typeof(TestNamespace.Dependency)])]

            namespace TestNamespace;

            public interface IDependency { }

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
    [Arguments("[FromKeyedServices(\"A\")] IDependency dependency")]
    [Arguments("[IocInject(\"A\")] IDependency dependency")]
    [Arguments("[IocInject(Key = \"A\")] IDependency dependency")]
    [Arguments("[FromKeyedServices(\"A\")] IEnumerable<IDependency> dependencies")]
    public async Task SGIOC018_KeyedDependency_RegisteredWithSameKey_NoDiagnostic(string parameter)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton, Key = "A")]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer({{parameter}}) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments(", Key = \"B\"")]
    [Arguments(", Key = 1")]
    public async Task SGIOC018_KeyedDependency_NotRegisteredWithSameKey_ReportsDiagnostic(string registrationKey)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton{{registrationKey}})]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer([FromKeyedServices("A")] IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IDependency (key \"A\")' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_NonKeyedDependency_OnlyKeyedRegistration_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton, Key = "A")]
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

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IDependency' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_KeyedDependency_EnumKey_ComparedByValueAndType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum Region { East, West }

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton, Key = Region.West)]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(
                    [FromKeyedServices(Region.West)] IDependency west,
                    [FromKeyedServices(Region.East)] IDependency east,
                    [FromKeyedServices(1)] IDependency number) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var messages = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018")
            .Select(static d => d.GetMessage())
            .OrderBy(static m => m, StringComparer.Ordinal)
            .ToList();

        await Assert.That(messages).IsEquivalentTo(
        [
            "Unable to resolve service 'IDependency (key 1)' for container 'TestContainer'",
            "Unable to resolve service 'IDependency (key TestNamespace.Region.East)' for container 'TestContainer'"
        ]);
    }

    [Test]
    public async Task SGIOC018_KeyedInjectedProperty_NotRegisteredWithSameKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton, Key = "A")]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                [IocInject(Key = "A")]
                public IDependency First { get; set; } = null!;

                [IocInject(Key = "B")]
                public IDependency Second { get; set; } = null!;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IDependency (key \"B\")' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_CsharpKey_MatchesAnyRegistrationOfType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public static class Keys
            {
                public const string Primary = "Primary";
            }

            public interface IDependency { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton, Key = "Keys.Primary", KeyType = KeyType.Csharp)]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer([FromKeyedServices("Primary")] IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]", "IRepository<User> repository")]
    [Arguments("[IocRegister(ServiceLifetime.Singleton, RegisterAllInterfaces = true)]", "IRepository<User> repository")]
    [Arguments("[IocRegister(ServiceLifetime.Singleton)]", "Repository<User> repository")]
    [Arguments("[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)], Key = \"A\")]", "[FromKeyedServices(\"A\")] IRepository<User> repository")]
    [Arguments("[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]", "IEnumerable<IRepository<User>> repositories")]
    public async Task SGIOC018_OpenGenericRegistration_ClosedDependency_NoDiagnostic(string registration, string parameter)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }

            public interface IRepository<T> { }

            {{registration}}
            public class Repository<T> : IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer({{parameter}}) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("struct", "IRepository<User>")]
    [Arguments("struct", "IRepository<int?>")]
    [Arguments("class", "IRepository<int>")]
    [Arguments("unmanaged", "IRepository<string>")]
    [Arguments("new()", "IRepository<NoDefaultConstructor>")]
    [Arguments("new()", "IRepository<string>")]
    [Arguments("IEntity", "IRepository<User>")]
    [Arguments("struct", "Lazy<IRepository<User>>")]
    [Arguments("struct", "IEnumerable<IRepository<User>>")]
    // Like the generator, contravariant and nested variant conversions are not followed
    [Arguments("IComparer<string>", "IRepository<IComparer<object>>")]
    [Arguments("IEnumerable<IEnumerable<object>>", "IRepository<List<List<string>>>")]
    [Arguments("IEnumerable<object>", "IRepository<int[]>")]
    public async Task SGIOC018_OpenGenericRegistration_UnsatisfiedConstraint_ReportsDiagnostic(string constraint, string parameterType)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IEntity { }
            public class User { }
            public class NoDefaultConstructor(int value) { }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> where T : {{constraint}} { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer({{parameterType}} repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("struct", "IRepository<int>")]
    [Arguments("class", "IRepository<User>")]
    [Arguments("unmanaged", "IRepository<int>")]
    [Arguments("new()", "IRepository<User>")]
    [Arguments("new()", "IRepository<int>")]
    [Arguments("IEntity", "IRepository<Entity>")]
    [Arguments("IEntity", "IRepository<DerivedEntity>")]
    [Arguments("Entity", "IRepository<DerivedEntity>")]
    [Arguments("IEnumerable<object>", "IRepository<List<string>>")]
    [Arguments("struct", "Lazy<IRepository<int>>")]
    [Arguments("Enum", "IRepository<DayOfWeek>")]
    [Arguments("Delegate", "IRepository<Action>")]
    [Arguments("IEnumerable<IEnumerable<string>>", "IRepository<List<List<string>>>")]
    [Arguments("IEnumerable<int>", "IRepository<int[]>")]
    [Arguments("IEnumerable<object>", "IRepository<string[]>")]
    public async Task SGIOC018_OpenGenericRegistration_SatisfiedConstraint_NoDiagnostic(string constraint, string parameterType)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IEntity { }
            public class Entity : IEntity { }
            public class DerivedEntity : Entity { }
            public class User { }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> where T : {{constraint}} { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer({{parameterType}} repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_OpenGenericRegistration_ConstructorConstraintWithRequiredMembers_ReportsUnsatisfied()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class RequiredEntity
            {
                public required string Name { get; init; }
            }

            public class DerivedRequiredEntity : RequiredEntity;

            public class SetsRequiredEntity
            {
                [SetsRequiredMembers]
                public SetsRequiredEntity() => Name = string.Empty;

                public required string Name { get; init; }
            }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> where T : new() { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IRepository<RequiredEntity> required, IRepository<DerivedRequiredEntity> derived, IRepository<SetsRequiredEntity> setsRequired) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var messages = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").Select(d => d.GetMessage()).Order().ToList();

        await Assert.That(messages).IsEquivalentTo([
            "Unable to resolve service 'IRepository<DerivedRequiredEntity>' for container 'TestContainer'",
            "Unable to resolve service 'IRepository<RequiredEntity>' for container 'TestContainer'",
        ]);
    }

    [Test]
    public async Task SGIOC018_OpenGenericRegistration_CovariantConversionBeyondGeneratorLimit_ReportsDiagnostic()
    {
        // The generator enumerates at most 64 covariant conversions of IGrouping<Many, Many> (10 candidates per position);
        // IGrouping<object, object> is beyond them, so the generator does not close Repository<IGrouping<Many, Many>>
        const string source = """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface I1; public interface I2; public interface I3; public interface I4;
            public interface I5; public interface I6; public interface I7; public interface I8;
            public class Many : I1, I2, I3, I4, I5, I6, I7, I8;

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> where T : IGrouping<object, object> { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IRepository<IGrouping<Many, Many>> repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC018_OpenGenericRegistrationsWithDifferentConstraints_ResolvedBySatisfyingImplementation_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class StructRepository<T> : IRepository<T> where T : struct { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class ClassRepository<T> : IRepository<T> where T : class { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IRepository<int> number, IRepository<User> user) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_OpenGenericRegistration_ConstraintOnOtherTypeParameter_ChecksClosedConstraint()
    {
        // Handler<TRequest, TResponse> requires TRequest : IRequest<TResponse>; Request only implements IRequest<string>
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequest<TResponse> { }
            public class Request : IRequest<string> { }

            public interface IHandler<TRequest, TResponse> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<,>)])]
            public class Handler<TRequest, TResponse> : IHandler<TRequest, TResponse> where TRequest : IRequest<TResponse> { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IHandler<Request, string> matching, IHandler<Request, int> mismatched) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IHandler<Request, int>' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_ExplicitOnly_OpenGenericRegistration_ClosedDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }

            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T> { }

            public class Consumer
            {
                public Consumer(IRepository<User> repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor(typeof(Repository<>), ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            [IocRegisterFor<Consumer>(ServiceLifetime.Singleton)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_ExplicitOnly_OpenGenericRegistrationOutsideContainer_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            public class Consumer
            {
                public Consumer(IRepository<User> repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor<Consumer>(ServiceLifetime.Singleton)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IRepository<User>' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_OpenGenericImplementation_OnlyNonGenericDependencyChecked()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            public interface IValidator<T> { }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public Repository(IValidator<T> validator, IUnregistered unregistered) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).IsEqualTo("Unable to resolve service 'IUnregistered' for container 'TestContainer'");
    }

    [Test]
    public async Task SGIOC018_IncludeTags_OpenGenericRegistrationWithoutMatchingTag_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }

            public interface IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)], Tags = ["Other"])]
            public class Repository<T> : IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton, Tags = ["Feature"])]
            public class Consumer
            {
                public Consumer(IRepository<User> repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false, IncludeTags = ["Feature"])]
            public partial class FeatureContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC018_RegisterDefaultsFactoryWithOpenGenericTarget_ProvidesClosedDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.RepositoryFactory.Create))]

            namespace TestNamespace;

            public class User { }

            public interface IRepository<T> { }

            internal sealed class Repository<T> : IRepository<T> { }

            public static class RepositoryFactory
            {
                public static IRepository<T> Create<T>() => new Repository<T>();
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IRepository<User> repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_OpenGenericRegistrationOfImportedModule_ProvidesClosedDependency_NoDiagnostic()
    {
        const string moduleSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace ModuleNamespace;

            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T> { }

            [IocContainer]
            [IocRegisterFor(typeof(Repository<>), ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public partial class SharedModule { }
            """;

        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using ModuleNamespace;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }

            [IocImportModule<SharedModule>]
            public class ModuleImports { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer(IRepository<User> repository) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var moduleCompilation = SourceGeneratorTestHelper.CreateCompilation("ModuleAssembly", moduleSource);
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [moduleCompilation.ToMetadataReference()]);
        var diagnostics = await compilation.WithAnalyzers([new ContainerAnalyzer()]).GetAnalyzerDiagnosticsAsync();
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

    [Test]
    [Arguments("IDependency", 0)]
    [Arguments("IUnregisteredService", 1)]
    public async Task SGIOC018_Factory_ChecksFactoryParametersInsteadOfConstructor(string factoryParameter, int expected)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }
            public interface IUnregisteredService { }
            public interface IMyService { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService
            {
                public MyService(IUnregisteredService unregistered) { }
            }

            public static class MyServiceFactory
            {
                public static IMyService Create(System.IServiceProvider sp, {{factoryParameter}} dependency) => null!;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC018_RegisterDefaultsFactory_ConstructorNotChecked_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.MyServiceFactory.Create))]

            namespace TestNamespace;

            public interface IUnregisteredService { }
            public interface IMyService { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class MyService : IMyService
            {
                public MyService(IUnregisteredService unregistered) { }
            }

            public static class MyServiceFactory
            {
                public static IMyService Create() => null!;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC018_Instance_ConstructorNotChecked_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregisteredService { }
            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new(null!);

                public MyService(IUnregisteredService unregistered) { }

                [IocInject]
                public IUnregisteredService? Injected { get; set; }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("nameof(ExternalService.Dependency)", "", 1)]
    [Arguments("nameof(ExternalService.Dependency)", "ServiceLifetime.Singleton", 0)]
    [Arguments("nameof(ExternalService.Initialize)", "", 1)]
    [Arguments("nameof(ExternalService.Initialize)", "ServiceLifetime.Singleton", 0)]
    [Arguments("new object[] { nameof(ExternalService.Dependency), \"A\" }", "ServiceLifetime.Singleton, Key = \"A\"", 0)]
    [Arguments("new object[] { nameof(ExternalService.Dependency), \"A\" }", "ServiceLifetime.Singleton", 1)]
    [Arguments("new object[] { nameof(ExternalService.Initialize), \"A\" }", "ServiceLifetime.Singleton, Key = \"A\"", 0)]
    [Arguments("new object[] { nameof(ExternalService.Initialize), \"A\" }", "ServiceLifetime.Singleton", 1)]
    public async Task SGIOC018_InjectMembers_ChecksMemberDependencies(string injectMember, string dependencyRegistration, int expected)
    {
        var registration = dependencyRegistration.Length == 0
            ? ""
            : $"[assembly: IocRegisterFor<TestNamespace.Dependency>({dependencyRegistration}, ServiceTypes = [typeof(TestNamespace.IDependency)])]";

        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor<ExternalService>(ServiceLifetime.Singleton, InjectMembers = [{{injectMember}}])]
            {{registration}}

            namespace TestNamespace;

            public interface IDependency { }

            public class Dependency : IDependency { }

            public class ExternalService
            {
                public IDependency? Dependency { get; set; }

                public void Initialize(IDependency dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(expected);
        if(expected > 0)
            await Assert.That(sgioc018[0].GetMessage()).Contains("IDependency");
    }

    [Test]
    public async Task SGIOC018_InjectMembers_OfExternalImplementation_ReportedOnAttribute()
    {
        const string externalSource = """
            namespace External;

            public interface IExternalDependency { }

            public class ExternalService
            {
                public IExternalDependency? Dependency { get; set; }
            }
            """;

        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<External.ExternalService>(ServiceLifetime.Singleton, InjectMembers = [nameof(External.ExternalService.Dependency)])]

            namespace TestNamespace;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var externalCompilation = SourceGeneratorTestHelper.CreateCompilation("ExternalAssembly", externalSource);
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [externalCompilation.ToMetadataReference()]);
        var diagnostics = await compilation.WithAnalyzers([new ContainerAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).Contains("IExternalDependency");
        await Assert.That(sgioc018[0].Location.IsInSource).IsTrue();
    }

    [Test]
    [Arguments("Lazy<IDependency>")]
    [Arguments("Func<IDependency>")]
    [Arguments("Func<string, IDependency>")]
    [Arguments("Task<IDependency>")]
    [Arguments("IReadOnlyList<IDependency>")]
    [Arguments("IEnumerable<Lazy<IDependency>>")]
    [Arguments("Lazy<IEnumerable<IDependency>>")]
    [Arguments("Lazy<IRepository<User>>")]
    [Arguments("IDictionary<string, IKeyedDependency>")]
    [Arguments("IEnumerable<KeyValuePair<string, IKeyedDependency>>")]
    public async Task SGIOC018_WrapperOfRegisteredService_NoDiagnostic(string parameterType)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class User { }
            public interface IDependency { }
            public interface IKeyedDependency { }
            public interface IRepository<T> { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister<IKeyedDependency>(ServiceLifetime.Singleton, Key = "A")]
            public class KeyedDependency : IKeyedDependency { }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer({{parameterType}} dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("Lazy<IUnregisteredService>")]
    [Arguments("Func<IUnregisteredService>")]
    [Arguments("Task<IUnregisteredService>")]
    [Arguments("IReadOnlyList<Lazy<IUnregisteredService>>")]
    [Arguments("Lazy<IEnumerable<IUnregisteredService>>")]
    [Arguments("IDictionary<string, IDependency>")]
    [Arguments("Task<Lazy<IDependency>>")]
    public async Task SGIOC018_WrapperOfUnregisteredService_ReportsDiagnostic(string parameterType)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }
            public interface IUnregisteredService { }

            [IocRegister<IDependency>(ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                public Consumer({{parameterType}} dependency) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("Key = \"A\"", 0)]
    [Arguments("", 1)]
    public async Task SGIOC018_InjectedMethodKey_AppliesToParametersWithoutKey(string dependencyKey, int expected)
    {
        var registrationArguments = dependencyKey.Length == 0 ? "ServiceLifetime.Singleton" : $"ServiceLifetime.Singleton, {dependencyKey}";
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }
            public interface IOther { }

            [IocRegister<IDependency>({{registrationArguments}})]
            public class Dependency : IDependency { }

            [IocRegister<IOther>(ServiceLifetime.Singleton, Key = "B")]
            public class Other : IOther { }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer
            {
                [IocInject(Key = "A")]
                public void Initialize(IDependency dependency, [FromKeyedServices("B")] IOther other) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(expected);
        if(expected > 0)
            await Assert.That(sgioc018[0].GetMessage()).Contains("IDependency (key \"A\")");
    }
}
