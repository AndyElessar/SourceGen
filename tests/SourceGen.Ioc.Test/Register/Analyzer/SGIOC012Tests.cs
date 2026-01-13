namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC012: Duplicated IoCRegisterDefaults Detected - Same target type has multiple default settings.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC012)]
public class SGIOC012Tests
{
    [Test]
    public async Task SGIOC012_DuplicatedDefaults_SameTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_DifferentTargetTypes_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService1), ServiceLifetime.Singleton)]
            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService2), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService1 { }
            public interface IMyService2 { }

            [IoCRegister]
            public class MyService1 : IMyService1 { }

            [IoCRegister]
            public class MyService2 : IMyService2 { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_SingleDefault_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_GenericAttribute_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Singleton)]
            [assembly: IoCRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MixedGenericAndNonGeneric_SameTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IoCRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MultipleDefaults_SameTargetType_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]
            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Transient)]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        // Should report 2 diagnostics (second and third are duplicates)
        await Assert.That(sgioc012).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC012_NoDefaults_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_GenericTargetType_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Singleton)]
            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericService<T> { }

            [IoCRegister]
            public class MyGenericService<T> : IGenericService<T> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IGenericService");
    }

    [Test]
    public async Task SGIOC012_TypeLevelAttribute_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegisterDefaults<IMyService>(ServiceLifetime.Singleton)]
            [IoCRegisterDefaults<IMyService>(ServiceLifetime.Scoped)]
            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MixedAssemblyAndTypeLevelAttribute_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegisterDefaults<IMyService>(ServiceLifetime.Scoped)]
            [IoCRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }
}
