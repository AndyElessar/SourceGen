namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC025: Circular module import detected.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC025)]
public class SGIOC025Tests
{
    [Test]
    public async Task CircularImport_DirectCycle_ReportsDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            [IocImportModule<ModuleB>]
            public partial class ModuleA { }

            [IocContainer]
            [IocImportModule<ModuleA>]
            public partial class ModuleB { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc025 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC025).ToList();

        // Both containers in the direct cycle should be reported
        await Assert.That(sgioc025).Count().IsGreaterThanOrEqualTo(2);
        await Assert.That(sgioc025[0].GetMessage()).Contains("→");
    }

    [Test]
    public async Task CircularImport_TransitiveCycle_ReportsDiagnostic()
    {
        // A → B → C → A
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            [IocImportModule<ModuleB>]
            public partial class ModuleA { }

            [IocContainer]
            [IocImportModule<ModuleC>]
            public partial class ModuleB { }

            [IocContainer]
            [IocImportModule<ModuleA>]
            public partial class ModuleC { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc025 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC025).ToList();

        // All three containers in the transitive cycle should be reported
        await Assert.That(sgioc025).Count().IsGreaterThanOrEqualTo(3);
        var firstMessage = sgioc025[0].GetMessage();
        await Assert.That(firstMessage).Contains("TestNamespace.ModuleA");
        await Assert.That(firstMessage).Contains("TestNamespace.ModuleB");
        await Assert.That(firstMessage).Contains("TestNamespace.ModuleC");
    }

    [Test]
    public async Task CircularImport_NoCircle_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            [IocImportModule<ModuleB>]
            public partial class ModuleA { }

            [IocContainer]
            public partial class ModuleB { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc025 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC025).ToList();

        await Assert.That(sgioc025).Count().IsEqualTo(0);
    }

    [Test]
    public async Task CircularImport_NonGenericSyntax_ReportsDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            [IocImportModule(typeof(ModuleB))]
            public partial class ModuleA { }

            [IocContainer]
            [IocImportModule(typeof(ModuleA))]
            public partial class ModuleB { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc025 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC025).ToList();

        await Assert.That(sgioc025).Count().IsGreaterThanOrEqualTo(2);
    }
}
