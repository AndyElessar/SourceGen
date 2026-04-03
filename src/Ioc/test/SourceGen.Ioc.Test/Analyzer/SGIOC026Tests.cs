namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC026: AsyncMethodInject feature requires MethodInject to be enabled.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC026)]
public class SGIOC026Tests
{
    [Test]
    public async Task SGIOC026_AsyncMethodInjectWithoutMethodInject_ReportsDiagnostic()
    {
        // SGIOC026 fires at compilation level when AsyncMethodInject is ON but MethodInject is OFF.
        // Any source referencing SourceGen.Ioc is sufficient — diagnostic has no specific location.
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class TestService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc026 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC026").ToList();

        await Assert.That(sgioc026).Count().IsEqualTo(1);
        await Assert.That(sgioc026[0].GetMessage()).Contains("AsyncMethodInject").And.Contains("MethodInject");
    }

    [Test]
    public async Task SGIOC026_AsyncMethodInjectWithMethodInject_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class TestService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc026 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC026");

        await Assert.That(sgioc026).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC026_OnlyMethodInject_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class TestService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc026 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC026");

        await Assert.That(sgioc026).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC026_DefaultFeatures_NoDiagnostic()
    {
        // Default features include MethodInject but NOT AsyncMethodInject — SGIOC026 must not fire.
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class TestService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc026 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC026");

        await Assert.That(sgioc026).Count().IsEqualTo(0);
    }
}
