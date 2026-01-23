namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC013: Key type is unmatched - ServiceKeyAttribute parameter type does not match the registered key type.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC013)]
public class SGIOC013Tests
{
    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_MatchingStringType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "MyKey")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_MismatchedType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "MyKey")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] int key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013").ToList();

        await Assert.That(sgioc013).Count().IsEqualTo(1);
        await Assert.That(sgioc013[0].GetMessage()).Contains("key").And.Contains("int").And.Contains("string");
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_IntKeyWithIntParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = 42)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] int key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_IntKeyWithStringParameter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = 42)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013").ToList();

        await Assert.That(sgioc013).Count().IsEqualTo(1);
        await Assert.That(sgioc013[0].GetMessage()).Contains("key").And.Contains("string").And.Contains("int");
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_ObjectParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "MyKey")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] object key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_NullableIntKeyWithNullableIntParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = 42)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] int? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_ConstructorParameter_MismatchedType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "MyKey")]
            public class MyService([ServiceKey] int key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013").ToList();

        await Assert.That(sgioc013).Count().IsEqualTo(1);
        await Assert.That(sgioc013[0].GetMessage()).Contains("key").And.Contains("int").And.Contains("string");
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_ConstructorParameter_MatchingType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "MyKey")]
            public class MyService([ServiceKey] string key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_NoKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_EnumKey_MatchingEnumParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum ServiceKey { Key1, Key2 }

            public interface IMyService { }

            [IocRegister(Key = ServiceKey.Key1)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] ServiceKey key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_EnumKey_MismatchedEnumParameter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum ServiceKeyType { Key1, Key2 }
            public enum OtherEnum { Value1, Value2 }

            public interface IMyService { }

            [IocRegister(Key = ServiceKeyType.Key1)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] OtherEnum key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_ServiceKeyAttribute_MultipleParameters_OnlyMismatchedReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister(Key = "MyKey")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize(IDependency dep, [ServiceKey] int key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013").ToList();

        await Assert.That(sgioc013).Count().IsEqualTo(1);
        await Assert.That(sgioc013[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC013_IocRegisterForAttribute_ServiceKeyAttribute_MismatchedType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyService), Key = "MyKey")]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService([ServiceKey] int key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013").ToList();

        await Assert.That(sgioc013).Count().IsEqualTo(1);
        await Assert.That(sgioc013[0].GetMessage()).Contains("key").And.Contains("int").And.Contains("string");
    }

    [Test]
    public async Task SGIOC013_GenericAttribute_ServiceKeyAttribute_MismatchedType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(Key = "MyKey")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] int key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_KeyTypeCsharp_ServiceKeyAttribute_MatchingType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public static class KeyHolder
            {
                public const string Key = "MyKey";
            }

            public interface IMyService { }

            [IocRegister(KeyType = KeyType.Csharp, Key = nameof(KeyHolder.Key))]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_KeyTypeCsharp_WithMismatchedParameterType_NoDiagnostic()
    {
        // When KeyType is Csharp, the analyzer should skip type checking
        // because the actual key type cannot be determined at compile time
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public static class KeyHolder
            {
                public static readonly int Key = 42;
            }

            public interface IMyService { }

            [IocRegister(KeyType = KeyType.Csharp, Key = "KeyHolder.Key")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        // No diagnostic because KeyType.Csharp means the key is an expression, type cannot be determined
        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_KeyTypeCsharp_ConstructorParameter_NoDiagnostic()
    {
        // When KeyType is Csharp, the analyzer should skip type checking for constructor parameters
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(KeyType = KeyType.Csharp, Key = "nameof(MyService)")]
            public class MyService([ServiceKey] int key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        // No diagnostic because KeyType.Csharp skips type checking
        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC013_KeyTypeCsharp_IocRegisterForAttribute_NoDiagnostic()
    {
        // When KeyType is Csharp, the analyzer should skip type checking for IoCRegisterFor
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyService), KeyType = KeyType.Csharp, Key = "SomeExpression")]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService([ServiceKey] double key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        // No diagnostic because KeyType.Csharp skips type checking
        await Assert.That(sgioc013).Count().IsEqualTo(0);
    }
}
