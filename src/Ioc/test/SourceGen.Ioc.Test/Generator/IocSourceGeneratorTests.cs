namespace SourceGen.Ioc.Test.Generator;

public class IocSourceGeneratorTests
{
    [Test]
    public async Task IocSourceGenerator_NoAnnotations_GeneratesNothingAndReportsNothing()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>("public class Plain;");

        await Assert.That(result.GeneratedSources).IsEmpty();
        await Assert.That(result.ReportedDiagnostics).IsEmpty();
        await result.VerifyCompilableAsync();
    }
}
