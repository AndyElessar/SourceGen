namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC030: Synchronous dependency requested for async-init-only service.
/// <para>
/// NOTE: Validation logic is deferred — the RegisterAnalyzer requires generator-phase data to
/// identify which registered services have only async-init registrations. These tests document
/// the current behavior (no diagnostic reported) until the feature is fully implemented.
/// </para>
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC030)]
public class SGIOC030Tests
{
    [Test]
    public async Task SGIOC030_ConstructorRequestsSyncTypeForAsyncInitService_NotReportedYet()
    {
        // TODO (SGIOC030): Once the analyzer can identify that IMyService has only async-init
        // registration (no sync path), it should report SGIOC030 when a constructor receives
        // IMyService synchronously — the consumer should use Task<IMyService> instead.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer : IConsumer
            {
                // This constructor requests IMyService synchronously, but MyService is async-init only.
                // Eventually SGIOC030 should fire here.
                public Consumer(IMyService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030");

        // Deferred: currently 0 — will change once validation logic is implemented
        await Assert.That(sgioc030).Count().IsEqualTo(0);
    }
}
