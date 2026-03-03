namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC023: Invalid InjectMembers element format.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC023)]
public class SGIOC023Tests
{
    [Test]
    public async Task SGIOC023_ValidNameof_NoDiagnostic()
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
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023);

        await Assert.That(sgioc023).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC023_ValidNameofWithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegisterFor(typeof(MyService), InjectMembers = [new object[] { nameof(MyService.Dep), "myKey" }])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023);

        await Assert.That(sgioc023).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC023_StringLiteralElement_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            // Raw string literal instead of nameof() — invalid format
            [IocRegisterFor(typeof(MyService), InjectMembers = ["Dep"])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023).ToList();

        await Assert.That(sgioc023).Count().IsEqualTo(1);
        await Assert.That(sgioc023[0].GetMessage()).Contains("0");
    }

    [Test]
    public async Task SGIOC023_ArrayWithMissingFirstNameof_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            // Array where first element is a string literal, not nameof() — invalid format
            [IocRegisterFor(typeof(MyService), InjectMembers = [new object[] { "Dep", "myKey" }])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023).ToList();

        await Assert.That(sgioc023).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC023_ArrayWithSingleElement_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            // Array with only one element (needs at least nameof + key) — invalid format
            [IocRegisterFor(typeof(MyService), InjectMembers = [new object[] { nameof(MyService.Dep) }])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023).ToList();

        await Assert.That(sgioc023).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC023_MultipleInvalidElements_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            // Two invalid elements
            [IocRegisterFor(typeof(MyService), InjectMembers = ["Dep", "Dep2"])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
                public IDependency? Dep2 { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023).ToList();

        await Assert.That(sgioc023).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC023_NestedArrayWithTooManyElements_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            // Array with 4 elements (> 3) — invalid format
            [IocRegisterFor(typeof(MyService), InjectMembers = [new object[] { nameof(MyService.Dep), "myKey", KeyType.Value, "extra" }])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023).ToList();

        await Assert.That(sgioc023).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC023_NestedArrayWithInvalidThirdElement_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            // Array where 3rd element is a string (not a KeyType constant) — invalid format
            [IocRegisterFor(typeof(MyService), InjectMembers = [new object[] { nameof(MyService.Dep), "myKey", "notAKeyType" }])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023).ToList();

        await Assert.That(sgioc023).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC023_ValidNameofWithKeyAndCsharpKeyType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            public static class Keys
            {
                public static string PrimaryKey => "primary";
            }

            [IocRegisterFor(typeof(MyService), InjectMembers = [new object[] { nameof(MyService.Dep), nameof(Keys.PrimaryKey), KeyType.Csharp }])]
            public static class MyModule { }

            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc023 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC023);

        await Assert.That(sgioc023).Count().IsEqualTo(0);
    }
}
