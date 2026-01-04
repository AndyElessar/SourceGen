namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC010: Invalid Attribute Usage - Both Factory and Instance are specified on the same attribute.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC010)]
public class SGIOC010Tests
{
    [Test]
    public async Task SGIOC010_BothFactoryAndInstance_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(
                Factory = nameof(MyService.Create),
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
                public static MyService Create() => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(1);
        await Assert.That(sgioc010[0].GetMessage()).Contains("Factory").And.Contains("Instance");
    }

    [Test]
    public async Task SGIOC010_OnlyFactory_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Factory = nameof(MyService.Create))]
            public class MyService : IMyService
            {
                public static MyService Create() => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC010_OnlyInstance_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC010_BothFactoryAndInstance_WithStringPath_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(
                Factory = "MyService.Create",
                Instance = "MyService.Default")]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
                public static MyService Create() => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC010_IoCRegisterFor_BothFactoryAndInstance_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
                public static MyService Create() => new MyService();
            }

            [IoCRegisterFor(
                typeof(MyService),
                Factory = nameof(MyService.Create),
                Instance = nameof(MyService.Default))]
            public partial class Config { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC010_AssemblyLevel_BothFactoryAndInstance_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterFor(
                typeof(TestNamespace.MyService),
                Factory = nameof(TestNamespace.MyService.Create),
                Instance = nameof(TestNamespace.MyService.Default))]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
                public static MyService Create() => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC010_NoFactoryOrInstance_NoDiagnostic()
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
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(0);
    }
}
