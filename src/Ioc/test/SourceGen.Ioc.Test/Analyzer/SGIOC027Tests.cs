namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC027: Partial accessor must return Task&lt;T&gt; for an async-init service.
/// <para>
/// NOTE: Validation logic is deferred — the ContainerAnalyzer requires generator-phase data to
/// identify which registered services have async inject methods. These tests document the current
/// behavior (no diagnostic reported) until the feature is fully implemented.
/// </para>
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC027)]
public class SGIOC027Tests
{
    [Test]
    public async Task SGIOC027_PartialAccessorReturnsSyncType_ForAsyncInitService_NotReportedYet()
    {
        // TODO (SGIOC027): Once the ContainerAnalyzer can identify async-init services from
        // generator-phase data, this test should assert Count().IsEqualTo(1) with message
        // containing the service type name.
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

            [IocContainer]
            public partial class TestContainer
            {
                // Should eventually require Task<IService> — but SGIOC027 is not yet implemented
                public partial IService GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc027 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC027");

        // Deferred: currently 0 — will change to 1 once validation logic is implemented
        await Assert.That(sgioc027).Count().IsEqualTo(0);
    }
}
