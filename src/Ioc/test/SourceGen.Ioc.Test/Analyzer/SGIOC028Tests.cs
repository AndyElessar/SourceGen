namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC028: [IocInject] method is declared as async void, which cannot be awaited.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC028)]
public class SGIOC028Tests
{
    [Test]
    public async Task SGIOC028_AsyncVoidMethod_ReportsDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public async void InitializeAsync(IService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc028 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC028").ToList();

        await Assert.That(sgioc028).Count().IsEqualTo(1);
        await Assert.That(sgioc028[0].GetMessage()).Contains("InitializeAsync").And.Contains("async void");
    }

    [Test]
    public async Task SGIOC028_AsyncVoidMethod_NeitherSGIOC007NorSGIOC022AlsoFires()
    {
        // SGIOC028 fires first and returns early - the return-type check (SGIOC007) and
        // feature-gate check (SGIOC022) must NOT duplicate the diagnostic.
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public async void InitializeAsync(IService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        var sgioc007 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC007");
        var sgioc022 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC022");

        await Assert.That(sgioc007).Count().IsEqualTo(0);
        await Assert.That(sgioc022).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC028_AsyncTaskMethod_WithAsyncMethodInjectEnabled_NoDiagnostic()
    {
        // async Task is a valid signature when AsyncMethodInject is ON - SGIOC028 must not fire.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public async Task InitializeAsync(IService service) => await Task.CompletedTask;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc028 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC028");

        await Assert.That(sgioc028).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC028_SyncVoidMethod_NoDiagnostic()
    {
        // Regular void method (non-async) must NOT trigger SGIOC028.
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

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc028 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC028");

        await Assert.That(sgioc028).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC028_AsyncVoidMethod_FeaturesDisabled_StillReportsDiagnostic()
    {
        // SGIOC028 fires regardless of feature flags - async void is never acceptable.
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class TestService : IService
            {
                [IocInject]
                public async void InitializeAsync(IService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            // MethodInject and AsyncMethodInject both disabled
            ["build_property.SourceGenIocFeatures"] = "Register"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc028 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC028").ToList();

        await Assert.That(sgioc028).Count().IsEqualTo(1);
        await Assert.That(sgioc028[0].GetMessage()).Contains("InitializeAsync");
    }
}
