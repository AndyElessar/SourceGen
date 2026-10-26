namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC031: Container ignored due to disabled feature.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC031)]
public class SGIOC031Tests
{
    private const string ContainerSource = """
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        [IocContainer]
        public partial class TestContainer { }
        """;

    [Test]
    public async Task SGIOC031_ContainerFeatureDisabled_ReportsDiagnostic()
    {
        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,PropertyInject,MethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            ContainerSource,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc031 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC031).ToList();

        await Assert.That(sgioc031).Count().IsEqualTo(1);
        await Assert.That(sgioc031[0].GetMessage()).Contains("TestContainer").And.Contains("Container");
    }

    [Test]
    public async Task SGIOC031_DefaultFeatures_NoDiagnostic()
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(ContainerSource);
        var sgioc031 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC031);

        await Assert.That(sgioc031).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC031_ContainerFeatureEnabled_NoDiagnostic()
    {
        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Container"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            ContainerSource,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc031 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC031);

        await Assert.That(sgioc031).Count().IsEqualTo(0);
    }
}
