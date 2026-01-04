using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC011: Duplicated attribute usage when both [FromKeyedServices] and [Inject] are on same parameter.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC011)]
public class SGIOC011Tests
{
    [Test]
    public async Task SGIOC011_BothAttributesOnParameter_ReportsWarning()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("dependency");
        await Assert.That(sgioc011[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task SGIOC011_OnlyFromKeyedServicesAttribute_NoDiagnostic()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_OnlyInjectAttribute_NoDiagnostic()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_NoAttributesOnParameter_NoDiagnostic()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_MultipleParametersWithBothAttributes_ReportsMultipleWarnings()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC011_MixedParametersWithAndWithoutBothAttributes_ReportsOnlyForDuplicated()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("dep1");
    }

    [Test]
    public async Task SGIOC011_BothAttributesOnMethodParameter_ReportsWarning()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("dependency");
    }

    [Test]
    public async Task SGIOC011_BothAttributesOnPrimaryConstructor_ReportsWarning()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("dependency");
    }
}
