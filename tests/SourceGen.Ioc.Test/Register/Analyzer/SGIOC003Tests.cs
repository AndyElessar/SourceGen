namespace SourceGen.Ioc.Test.Register.Analyzer;

partial class RegisterAnalyzerTests
{
    [Test]
    [Category(Constants.SGIOC003)]
    public async Task SGIOC003_SingletonDependsOnScoped_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(ScopedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("Singleton").And.Contains("Scoped");
    }

    [Test]
    [Category(Constants.SGIOC003)]
    public async Task SGIOC003_SingletonDependsOnSingleton_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(SingletonDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.SGIOC003)]
    public async Task SGIOC003_ScopedDependsOnTransient_ShouldReportSGIOC005NotSGIOC003()
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
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005").ToList();

        // SGIOC003 is for Singleton -> Scoped, SGIOC005 is for Scoped -> Transient
        await Assert.That(sgioc003).Count().IsEqualTo(0);
        await Assert.That(sgioc005).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.SGIOC003)]
    public async Task SGIOC003_ScopedDependsOnSingleton_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(SingletonService singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.SGIOC003)]
    public async Task SGIOC003_SingletonDependsOnScopedViaInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }
}
