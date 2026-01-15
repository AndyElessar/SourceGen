namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC014: Key does not exist - ServiceKeyAttribute is marked on parameter but no Key is registered.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC014)]
public class SGIOC014Tests
{
    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_NoKey_InjectMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] string? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_NoKey_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService([ServiceKey] string? key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_WithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Key = "MyKey")]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_WithIntKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Key = 42)]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] int key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_NoKey_MultipleParameters_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IoCRegister]
            public class Dependency : IDependency { }

            [IoCRegister]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize(IDependency dep, [ServiceKey] string? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_IoCRegisterForAttribute_NoKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterFor(typeof(TestNamespace.MyService))]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService([ServiceKey] string? key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_IoCRegisterForAttribute_WithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterFor(typeof(TestNamespace.MyService), Key = "MyKey")]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService([ServiceKey] string key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_GenericAttribute_NoKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister<IMyService>]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] string? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC014_GenericAttribute_WithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister<IMyService>(Key = "MyKey")]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_NoServiceKeyAttribute_NoKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize(string? name) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_KeyTypeCsharp_NoKey_NoDiagnostic()
    {
        // When KeyType is Csharp, a Key is always expected to be provided
        // This tests that KeyType.Csharp with Key still works
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(KeyType = KeyType.Csharp, Key = "nameof(MyService)")]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_MultipleServiceKeyAttributes_NoKey_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService([ServiceKey] string? ctorKey) : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] string? methodKey) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC014_EnumKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum ServiceKeyType { Key1, Key2 }

            public interface IMyService { }

            [IoCRegister(Key = ServiceKeyType.Key1)]
            public class MyService : IMyService
            {
                [Inject]
                public void Initialize([ServiceKey] ServiceKeyType key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }
}
