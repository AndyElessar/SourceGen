using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC100: Duplicated attribute usage when both [FromKeyedServices] and [Inject] are on same parameter.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC100)]
public class SGIOC100Tests
{
    [Test]
    public async Task SGIOC100_BothAttributesOnParameter_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class TestService : IService
            {
                public TestService([FromKeyedServices("key")] [Inject(Key = "otherKey")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(1);
        await Assert.That(sgioc100[0].GetMessage()).Contains("dependency");
        await Assert.That(sgioc100[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task SGIOC100_OnlyFromKeyedServicesAttribute_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class TestService : IService
            {
                public TestService([FromKeyedServices("key")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC100_OnlyInjectAttribute_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class TestService : IService
            {
                public TestService([Inject(Key = "key")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC100_NoAttributesOnParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class TestService : IService
            {
                public TestService(IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC100_MultipleParametersWithBothAttributes_ReportsMultipleWarnings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IOtherService { }

            [IoCRegister]
            public class TestService : IService
            {
                public TestService(
                    [FromKeyedServices("key1")] [Inject(Key = "otherKey1")] IService dep1,
                    [FromKeyedServices("key2")] [Inject(Key = "otherKey2")] IOtherService dep2) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC100_MixedParametersWithAndWithoutBothAttributes_ReportsOnlyForDuplicated()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IOtherService { }

            [IoCRegister]
            public class TestService : IService
            {
                public TestService(
                    [FromKeyedServices("key1")] [Inject(Key = "otherKey1")] IService dep1,
                    [FromKeyedServices("key2")] IOtherService dep2) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(1);
        await Assert.That(sgioc100[0].GetMessage()).Contains("dep1");
    }

    [Test]
    public async Task SGIOC100_BothAttributesOnMethodParameter_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class TestService : IService
            {
                [Inject]
                public void Initialize([FromKeyedServices("key")] [Inject(Key = "otherKey")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(1);
        await Assert.That(sgioc100[0].GetMessage()).Contains("dependency");
    }

    [Test]
    public async Task SGIOC100_BothAttributesOnPrimaryConstructor_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class TestService([FromKeyedServices("key")] [Inject(Key = "otherKey")] IService dependency) : IService;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc100 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC100").ToList();

        await Assert.That(sgioc100).Count().IsEqualTo(1);
        await Assert.That(sgioc100[0].GetMessage()).Contains("dependency");
    }
}
