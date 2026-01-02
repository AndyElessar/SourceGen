namespace SourceGen.Ioc.Test.Register.Analyzer;

partial class RegisterAnalyzerTests
{
    [Test]
    [Category(Constants.SGIOC005)]
    public async Task SGIOC005_ScopedDependsOnTransient_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005").ToList();

        await Assert.That(sgioc005).Count().IsEqualTo(1);
        await Assert.That(sgioc005[0].GetMessage()).Contains("Scoped").And.Contains("Transient");
    }

    [Test]
    [Category(Constants.SGIOC005)]
    public async Task SGIOC005_ScopedDependsOnTransientViaInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ITransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
            public class TransientService : ITransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(ITransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005").ToList();

        await Assert.That(sgioc005).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.SGIOC005)]
    public async Task SGIOC005_TransientDependsOnTransient_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService
            {
                public TransientService(TransientDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005").ToList();

        await Assert.That(sgioc005).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.SGIOC005)]
    public async Task SGIOC005_ScopedDependsOnScoped_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(ScopedDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005").ToList();

        await Assert.That(sgioc005).Count().IsEqualTo(0);
    }
}
