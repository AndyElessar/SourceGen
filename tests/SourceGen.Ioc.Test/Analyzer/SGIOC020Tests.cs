namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC020: UseSwitchStatement is ignored when importing modules.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC020)]
public class SGIOC020Tests
{
    [Test]
    public async Task SGIOC020_UseSwitchStatementTrue_WithImportModule_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class SomeModule { }

            [IocContainer(UseSwitchStatement = true)]
            [IocImportModule(typeof(SomeModule))]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020").ToList();

        await Assert.That(sgioc020).Count().IsEqualTo(1);
        await Assert.That(sgioc020[0].GetMessage()).Contains("TestContainer").And.Contains("UseSwitchStatement");
    }

    [Test]
    public async Task SGIOC020_UseSwitchStatementTrue_WithGenericImportModule_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class SomeModule { }

            [IocContainer(UseSwitchStatement = true)]
            [IocImportModule<SomeModule>]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020").ToList();

        await Assert.That(sgioc020).Count().IsEqualTo(1);
        await Assert.That(sgioc020[0].GetMessage()).Contains("TestContainer");
    }

    [Test]
    public async Task SGIOC020_UseSwitchStatementTrue_WithMultipleImportModules_ReportsSingleDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class ModuleA { }
            public partial class ModuleB { }

            [IocContainer(UseSwitchStatement = true)]
            [IocImportModule(typeof(ModuleA))]
            [IocImportModule<ModuleB>]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020").ToList();

        await Assert.That(sgioc020).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC020_UseSwitchStatementTrue_WithoutImportModule_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer(UseSwitchStatement = true)]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020");

        await Assert.That(sgioc020).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC020_UseSwitchStatementFalse_WithImportModule_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class SomeModule { }

            [IocContainer(UseSwitchStatement = false)]
            [IocImportModule(typeof(SomeModule))]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020");

        await Assert.That(sgioc020).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC020_DefaultUseSwitchStatement_WithImportModule_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class SomeModule { }

            [IocContainer]
            [IocImportModule(typeof(SomeModule))]
            public partial class TestContainer { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020");

        await Assert.That(sgioc020).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC020_NestedContainer_UseSwitchStatementTrue_WithImportModule_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public partial class SomeModule { }

            public partial class OuterClass
            {
                [IocContainer(UseSwitchStatement = true)]
                [IocImportModule(typeof(SomeModule))]
                public partial class NestedContainer { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc020 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC020").ToList();

        await Assert.That(sgioc020).Count().IsEqualTo(1);
        await Assert.That(sgioc020[0].GetMessage()).Contains("NestedContainer");
    }
}
