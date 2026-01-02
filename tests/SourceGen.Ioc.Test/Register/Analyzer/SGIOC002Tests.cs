namespace SourceGen.Ioc.Test.Register.Analyzer;

partial class RegisterAnalyzerTests
{
    [Test]
    [Category(Constants.SGIOC002)]
    public async Task SGIOC002_DirectCircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IoCRegister]
            public class ServiceB
            {
                public ServiceB(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    [Category(Constants.SGIOC002)]
    public async Task SGIOC002_IndirectCircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IoCRegister]
            public class ServiceB
            {
                public ServiceB(ServiceC c) { }
            }

            [IoCRegister]
            public class ServiceC
            {
                public ServiceC(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    [Category(Constants.SGIOC002)]
    public async Task SGIOC002_NoCircularDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IoCRegister]
            public class ServiceB
            {
                public ServiceB(ServiceC c) { }
            }

            [IoCRegister]
            public class ServiceC { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.SGIOC002)]
    public async Task SGIOC002_CircularDependencyViaInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }

            [IoCRegister(ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            [IoCRegister(ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }
}
