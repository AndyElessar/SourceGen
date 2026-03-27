namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC029: Unsupported async partial accessor type (e.g., ValueTask&lt;T&gt;).
/// <para>
/// NOTE: Validation logic is deferred — the ContainerAnalyzer requires generator-phase data to
/// identify which registered services have async inject methods. These tests document the current
/// behavior (no diagnostic reported) until the feature is fully implemented.
/// </para>
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC029)]
public class SGIOC029Tests
{
    [Test]
    public async Task SGIOC029_PartialAccessorReturnsValueTask_ForAsyncInitService_NotReportedYet()
    {
        // TODO (SGIOC029): Once the ContainerAnalyzer can identify async-init services from
        // generator-phase data, this test should assert Count().IsEqualTo(1) with message
        // containing the service type name and mentioning Task<T>.
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
                // ValueTask<IService> is unsupported — only Task<IService> is allowed for async-init services
                // but SGIOC029 is not yet implemented
                public partial ValueTask<IService> GetService();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc029 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC029");

        // Deferred: currently 0 — will change to 1 once validation logic is implemented
        await Assert.That(sgioc029).Count().IsEqualTo(0);
    }
}
