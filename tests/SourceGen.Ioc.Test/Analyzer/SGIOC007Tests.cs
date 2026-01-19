namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC007: Invalid usage of InjectAttribute.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC007)]
public class SGIOC007Tests
{
    [Test]
    public async Task SGIOC007_InjectAttribute_OnStaticProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public static IService? StaticProperty { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("StaticProperty").And.Contains("static");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnStaticField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public static IService? _staticField;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("_staticField").And.Contains("static");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public static void StaticInitialize(IService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("StaticInitialize").And.Contains("static");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnNonVoidMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public int Initialize(IService service) => 0;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("Initialize").And.Contains("void");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnPrivateMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                private void Initialize(IService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("Initialize").And.Contains("private");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnPropertyWithPrivateSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public IService? Dependency { get; private set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("Dependency").And.Contains("setter is private");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnPropertyWithNoSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public IService? Dependency { get; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("Dependency").And.Contains("no setter");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnReadonlyField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public readonly IService? _dependency;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("_dependency").And.Contains("readonly");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnPrivateField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                private IService? _dependency;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("_dependency").And.Contains("private");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnVoidMethod_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public void Initialize(IService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnInternalMethod_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                internal void Initialize(IService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnInstanceProperty_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public IService? Dependency { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnPropertyWithInternalSetter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public IService? Dependency { get; internal set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnInstanceField_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public IService? _dependency;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnInternalField_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                internal IService? _dependency;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnAsyncMethod_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public async Task InitializeAsync(IService service) => await Task.CompletedTask;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007").ToList();

        await Assert.That(sgioc007).Count().IsEqualTo(1);
        await Assert.That(sgioc007[0].GetMessage()).Contains("InitializeAsync").And.Contains("void");
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_MultipleViolations_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public static IService? StaticProperty { get; set; }

                [IocInject]
                public int NonVoidMethod(IService service) => 0;

                [IocInject]
                public static void StaticMethod() { }

                [IocInject]
                private IService? _privateField;

                [IocInject]
                public readonly IService? _readonlyField;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(5);
    }

    [Test]
    public async Task SGIOC007_InjectAttribute_OnTypeWithoutIoCRegister_StillReportsDiagnostic()
    {
        // SGIOC007 should report diagnostic even if the type doesn't have IoCRegister attribute
        // because the InjectAttribute itself is invalid regardless of IoC registration
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            public class TestService : IService
            {
                [IocInject]
                public static IService? StaticProperty { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC007_ThirdPartyInjectAttribute_OnStaticMember_ReportsDiagnostic()
    {
        // Test that InjectAttribute from other libraries (by name only) also triggers diagnostic
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace ThirdParty
            {
                // Simulating Microsoft.AspNetCore.Components.InjectAttribute
                [System.AttributeUsage(System.AttributeTargets.Property | System.AttributeTargets.Field | System.AttributeTargets.Method)]
                public sealed class InjectAttribute : System.Attribute
                {
                }
            }

            namespace TestNamespace
            {
                public interface IService { }

                [IocRegister]
                public class TestService : IService
                {
                    [ThirdParty.Inject]
                    public static IService? StaticProperty { get; set; }
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");

        await Assert.That(sgioc007).Count().IsEqualTo(1);
    }
}
