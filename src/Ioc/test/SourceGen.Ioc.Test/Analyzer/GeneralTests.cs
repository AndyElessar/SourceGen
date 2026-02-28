namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// General tests for RegisterAnalyzer that don't belong to a specific diagnostic code.
/// </summary>
[Category(Constants.Analyzer)]
public class GeneralTests
{
    [Test]
    public async Task NoIoCRegisterAttribute_NoDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            public class ServiceB
            {
                public ServiceB(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics).Count().IsEqualTo(0);
    }
}
