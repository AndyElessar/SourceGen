namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC001: Invalid type for IoC registration (private/abstract).
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC001)]
public class SGIOC001Tests
{
    [Test]
    public async Task SGIOC001_PrivateClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IocRegister]
                private class PrivateService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_AbstractClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public abstract class AbstractService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_PrivateTargetType_ReportsDiagnostic()
    {
        // Note: When using IocRegisterForAttribute with typeof(), the type must be accessible
        // So we test with a nested private class where the attribute is on an outer accessible type
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IocRegisterFor(typeof(PrivateService))]
                private class PrivateService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_AbstractTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class AbstractService { }

            [IocRegisterFor(typeof(AbstractService))]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_PublicConcreteClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class ValidService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC001_InternalConcreteClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            internal class InternalService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }
}
