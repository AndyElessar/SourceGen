using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC004: Singleton depends on Transient service.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC004)]
public class SGIOC004Tests
{
    [Test]
    public async Task SGIOC004_SingletonDependsOnTransient_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
        await Assert.That(sgioc004[0].GetMessage()).Contains("Singleton").And.Contains("Transient");
    }

    [Test]
    public async Task SGIOC004_SingletonDependsOnTransientViaInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ITransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
            public class TransientService : ITransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(ITransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC004_SingletonDependsOnTransient_ReportsError()
    {
        // Singleton depending on Transient is a captive dependency issue - Error
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
        await Assert.That(sgioc004[0].GetMessage()).Contains("Singleton").And.Contains("Transient");
        await Assert.That(sgioc004[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC004_DefaultSettings_NonGenericInterface_AppliesLifetimeFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Transient)]

            namespace TestNamespace;

            public interface IMyService { }

            // Should get Transient lifetime from default settings
            [IocRegister]
            public class MyService : IMyService { }

            // Singleton service depending on Transient service should report error (SGIOC004)
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(MyService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        // Should report lifetime conflict: Singleton depends on Transient
        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }
}
