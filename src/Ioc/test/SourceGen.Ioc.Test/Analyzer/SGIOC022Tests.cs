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

    [Test]
    public async Task SGIOC022_InjectAttribute_WhenMethodInjectDisabled_ReportsDiagnostic()
    {
        // Void-returning method with MethodInject OFF → SGIOC022 with MethodInject feature name.
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public void Initialize(IService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            // MethodInject explicitly disabled
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc022 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC022").ToList();

        await Assert.That(sgioc022).Count().IsEqualTo(1);
        await Assert.That(sgioc022[0].GetMessage()).Contains("Initialize").And.Contains("MethodInject");
    }

    [Test]
    public async Task SGIOC022_InjectAttribute_TaskMethodWhenAsyncMethodInjectDisabled_ReportsDiagnosticWithAsyncMethodInjectName()
    {
        // Task-returning method with MethodInject ON but AsyncMethodInject OFF → SGIOC022 with AsyncMethodInject feature name.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public Task InitializeAsync(IService service) => Task.CompletedTask;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            // MethodInject ON, AsyncMethodInject OFF
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc022 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC022").ToList();

        await Assert.That(sgioc022).Count().IsEqualTo(1);
        await Assert.That(sgioc022[0].GetMessage()).Contains("InitializeAsync").And.Contains("AsyncMethodInject");
    }

    [Test]
    public async Task SGIOC022_InjectAttribute_TaskMethodWhenAsyncMethodInjectEnabled_NoDiagnostic()
    {
        // Task-returning method with both MethodInject and AsyncMethodInject ON → no SGIOC022.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public Task InitializeAsync(IService service) => Task.CompletedTask;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc022 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC022");

        await Assert.That(sgioc022).Count().IsEqualTo(0);
    }
}
