namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC022: Inject attribute ignored due to disabled feature.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC022)]
public class SGIOC022Tests
{
    [Test]
    public async Task SGIOC022_InjectAttribute_WhenPropertyInjectDisabled_ReportsDiagnostic()
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
                public IService? Dependency { get; set; }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,FieldInject,MethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc022 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC022").ToList();

        await Assert.That(sgioc022).Count().IsEqualTo(1);
        await Assert.That(sgioc022[0].GetMessage()).Contains("Dependency").And.Contains("PropertyInject");
    }
}
