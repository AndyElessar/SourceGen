namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC019: Container class must be declared as partial and cannot be static.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC019)]
public class SGIOC019Tests
{
    [Test]
    public async Task SGIOC019_NonPartialContainerClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019").ToList();

        await Assert.That(sgioc019).Count().IsEqualTo(1);
        await Assert.That(sgioc019[0].GetMessage()).Contains("TestContainer").And.Contains("partial").And.Contains("static");
    }

    [Test]
    public async Task SGIOC019_PartialContainerClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019");

        await Assert.That(sgioc019).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC019_NestedNonPartialContainerClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class OuterClass
            {
                [IocContainer]
                public class NestedContainer { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019").ToList();

        await Assert.That(sgioc019).Count().IsEqualTo(1);
        await Assert.That(sgioc019[0].GetMessage()).Contains("NestedContainer");
    }

    [Test]
    public async Task SGIOC019_NestedPartialContainerClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class OuterClass
            {
                [IocContainer]
                public partial class NestedContainer { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019");

        await Assert.That(sgioc019).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC019_ContainerWithOptions_NonPartial_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer(ResolveIServiceCollection = false)]
            public class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019").ToList();

        await Assert.That(sgioc019).Count().IsEqualTo(1);
        await Assert.That(sgioc019[0].GetMessage()).Contains("TestContainer");
    }

    [Test]
    public async Task SGIOC019_InternalNonPartialContainerClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            internal class InternalContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019").ToList();

        await Assert.That(sgioc019).Count().IsEqualTo(1);
        await Assert.That(sgioc019[0].GetMessage()).Contains("InternalContainer");
    }

    [Test]
    public async Task SGIOC019_SealedPartialContainerClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public sealed partial class SealedContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019");

        await Assert.That(sgioc019).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC019_StaticContainerClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public static partial class StaticContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019").ToList();

        await Assert.That(sgioc019).Count().IsEqualTo(1);
        await Assert.That(sgioc019[0].GetMessage()).Contains("StaticContainer").And.Contains("static");
    }

    [Test]
    public async Task SGIOC019_StaticNonPartialContainerClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public static class StaticNonPartialContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc019 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC019").ToList();

        await Assert.That(sgioc019).Count().IsEqualTo(1);
        await Assert.That(sgioc019[0].GetMessage()).Contains("StaticNonPartialContainer");
    }
}
