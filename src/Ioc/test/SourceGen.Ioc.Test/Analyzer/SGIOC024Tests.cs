namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC024: InjectMembers specifies non-injectable member.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC024)]
public class SGIOC024Tests
{
    [Test]
    public async Task SGIOC024_NonStaticProperty_WithSetter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024);

        await Assert.That(sgioc024).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC024_NonStaticField_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024);

        await Assert.That(sgioc024).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC024_VoidMethod_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.InjectDep)])]
            public static class MyModule { }

            public class MyService
            {
                public void InjectDep(IDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024);

        await Assert.That(sgioc024).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC024_StaticProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public static IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("static");
    }

    [Test]
    public async Task SGIOC024_PropertyWithoutSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("setter");
    }

    [Test]
    public async Task SGIOC024_PropertyWithPrivateSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; private set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("private");
    }

    [Test]
    public async Task SGIOC024_ReadonlyField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.Dep)])]
            public static class MyModule { }

            public class MyService
            {
                public readonly IDependency? Dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("readonly");
    }

    [Test]
    public async Task SGIOC024_PrivateField_ReportsDiagnostic()
    {
        // When [IocRegisterFor] is on the class itself, nameof can resolve private members
        // but the generator cannot set them — SGIOC024 should fire
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(_dep)])]
            public class MyService
            {
                private IDependency? _dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("_dep").And.Contains("private");
    }

    [Test]
    public async Task SGIOC024_NonVoidMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.GetDep)])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? GetDep(IDependency dep) => dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("GetDep").And.Contains("void");
    }

    [Test]
    public async Task SGIOC024_NonVoidMethod_WithAsyncMethodInjectEnabled_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.GetDepAsync)])]
            public static class MyModule { }

            public class MyService
            {
                public Task<int> GetDepAsync(IDependency dep) => Task.FromResult(0);
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage())
            .Contains("GetDepAsync")
            .And.Contains("void or non-generic Task");
    }

    [Test]
    public async Task SGIOC024_GenericMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(MyService.InjectDep)])]
            public static class MyModule { }

            public class MyService
            {
                public void InjectDep<T>(T dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("InjectDep").And.Contains("generic");
    }

    [Test]
    public async Task SGIOC024_ProtectedProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(Dep)])]
            public class MyService
            {
                protected IDependency? Dep { get; protected set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_ProtectedField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(Dep)])]
            public class MyService
            {
                protected IDependency? Dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_PrivateProtectedField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(_dep)])]
            public class MyService
            {
                private protected IDependency? _dep;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("_dep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_ProtectedMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(InjectDep)])]
            public class MyService
            {
                protected void InjectDep(IDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("InjectDep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_PrivateProtectedMethod_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(InjectDep)])]
            public class MyService
            {
                private protected void InjectDep(IDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("InjectDep").And.Contains("not accessible");
    }

    [Test]
    public async Task SGIOC024_PrivateProtectedProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [nameof(Dep)])]
            public class MyService
            {
                private protected IDependency? Dep { get; private protected set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("Dep").And.Contains("not accessible");
    }
}
