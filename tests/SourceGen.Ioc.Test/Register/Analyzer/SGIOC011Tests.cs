namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC011: Duplicated Registration Detected - Same implementation type and key are registered multiple times.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC011)]
public class SGIOC011Tests
{
    [Test]
    public async Task SGIOC011_DuplicatedRegistration_SameTypeWithoutKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }

            [IoCRegisterFor(typeof(MyService))]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_DuplicatedRegistration_SameTypeWithSameKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Key = "key1")]
            public class MyService : IMyService { }

            [IoCRegisterFor(typeof(MyService), Key = "key1")]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService").And.Contains("key1");
    }

    [Test]
    public async Task SGIOC011_DifferentKeys_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Key = "key1")]
            public class MyService : IMyService { }

            [IoCRegisterFor(typeof(MyService), Key = "key2")]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_DifferentTypes_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService1 : IMyService { }

            [IoCRegister]
            public class MyService2 : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SingleRegistration_NoDiagnostic()
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_GenericAttribute_DuplicatedRegistration_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }

            [IoCRegisterFor<MyService>]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_WithKeyAndWithoutKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister]
            public class MyService : IMyService { }

            [IoCRegisterFor(typeof(MyService), Key = "keyed")]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_MultipleIoCRegisterFor_SameType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService { }

            [IoCRegisterFor(typeof(MyService))]
            public interface IServiceMarker1 { }

            [IoCRegisterFor(typeof(MyService))]
            public interface IServiceMarker2 { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }
}
