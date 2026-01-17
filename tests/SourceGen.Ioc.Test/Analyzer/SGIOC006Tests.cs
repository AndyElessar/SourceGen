using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC006: Duplicated attribute usage when both [FromKeyedServices] and [IocInject] are on same parameter.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC006)]
public class SGIOC006Tests
{
    [Test]
    public async Task SGIOC006_BothAttributesOnParameter_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                public TestService([FromKeyedServices("key")] [IocInject(Key = "otherKey")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
        await Assert.That(sgioc006[0].GetMessage()).Contains("dependency");
        await Assert.That(sgioc006[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task SGIOC006_OnlyFromKeyedServicesAttribute_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                public TestService([FromKeyedServices("key")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_OnlyInjectAttribute_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                public TestService([IocInject(Key = "key")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_NoAttributesOnParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                public TestService(IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_MultipleParametersWithBothAttributes_ReportsMultipleWarnings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IOtherService { }

            [IocRegister]
            public class TestService : IService
            {
                public TestService(
                    [FromKeyedServices("key1")] [IocInject(Key = "otherKey1")] IService dep1,
                    [FromKeyedServices("key2")] [IocInject(Key = "otherKey2")] IOtherService dep2) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC006_MixedParametersWithAndWithoutBothAttributes_ReportsOnlyForDuplicated()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IOtherService { }

            [IocRegister]
            public class TestService : IService
            {
                public TestService(
                    [FromKeyedServices("key1")] [IocInject(Key = "otherKey1")] IService dep1,
                    [FromKeyedServices("key2")] IOtherService dep2) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
        await Assert.That(sgioc006[0].GetMessage()).Contains("dep1");
    }

    [Test]
    public async Task SGIOC006_BothAttributesOnMethodParameter_ReportsWarning()
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
                public void Initialize([FromKeyedServices("key")] [IocInject(Key = "otherKey")] IService dependency) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
        await Assert.That(sgioc006[0].GetMessage()).Contains("dependency");
    }

    [Test]
    public async Task SGIOC006_BothAttributesOnPrimaryConstructor_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService([FromKeyedServices("key")] [IocInject(Key = "otherKey")] IService dependency) : IService;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
        await Assert.That(sgioc006[0].GetMessage()).Contains("dependency");
    }
}
