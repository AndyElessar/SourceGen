namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC015: Unresolvable Constructor Parameter - Constructor contains built-in type
/// that cannot be resolved by the service provider.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC015)]
public class SGIOC015Tests
{
    [Test]
    public async Task SGIOC015_BuiltInType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(string name) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("name");
        await Assert.That(sgioc015[0].GetMessage()).Contains("string");
    }

    [Test]
    public async Task SGIOC015_IntType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(int count) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("count");
        await Assert.That(sgioc015[0].GetMessage()).Contains("int");
    }

    [Test]
    public async Task SGIOC015_GuidType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(Guid id) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("id");
    }

    [Test]
    public async Task SGIOC015_BuiltInTypeCollection_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(IEnumerable<string> names) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("names");
    }

    [Test]
    public async Task SGIOC015_WithIocInjectAttribute_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface INameProvider { }

            [IocRegister]
            public class NameProvider : INameProvider { }

            [IocRegister]
            public class MyService([IocInject(Key = "ConfigName")] string name) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_WithServiceKeyAttribute_NoDiagnostic()
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
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_WithFromKeyedServicesAttribute_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService([FromKeyedServices("ConfigName")] string name) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_WithDefaultValue_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(string name = "default") : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_WithFactory_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Factory = nameof(Create))]
            public class MyService(string name) : IMyService
            {
                public static MyService Create() => new MyService("test");
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_WithInstance_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Instance = nameof(Default))]
            public class MyService(string name) : IMyService
            {
                public static MyService Default { get; } = new MyService("default");
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_ServiceType_Constructor_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister]
            public class MyService(IDependency dependency) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_MultipleBuiltInParameters_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(string name, int count) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC015_MixedParameters_ReportsOnlyForBuiltIn()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister]
            public class MyService(IDependency dependency, string name) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("name");
    }

    [Test]
    public async Task SGIOC015_DateTimeType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(DateTime createdAt) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("createdAt");
    }

    [Test]
    public async Task SGIOC015_TimeSpanType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(TimeSpan timeout) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("timeout");
    }

    [Test]
    public async Task SGIOC015_UriType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(Uri endpoint) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("endpoint");
    }

    [Test]
    public async Task SGIOC015_StringArrayType_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(string[] items) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("items");
    }

    [Test]
    public async Task SGIOC015_InjectedProperty_BuiltInType_ReportsDiagnostic()
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
                public string Name { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("Name");
        await Assert.That(sgioc015[0].GetMessage()).Contains("string");
        await Assert.That(sgioc015[0].GetMessage()).Contains("Property");
    }

    [Test]
    public async Task SGIOC015_InjectedProperty_IntType_ReportsDiagnostic()
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
                public int Count { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("Count");
        await Assert.That(sgioc015[0].GetMessage()).Contains("int");
    }

    [Test]
    public async Task SGIOC015_InjectedProperty_WithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject(Key = "ConfigName")]
                public string Name { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedProperty_ServiceType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public IDependency Dependency { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedField_BuiltInType_ReportsDiagnostic()
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
                public string name;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("name");
        await Assert.That(sgioc015[0].GetMessage()).Contains("Field");
    }

    [Test]
    public async Task SGIOC015_InjectedField_WithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject(Key = "ConfigValue")]
                public int value;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedProperty_CollectionOfBuiltInType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public IEnumerable<string> Names { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("Names");
    }

    [Test]
    public async Task SGIOC015_MultipleInjectedMembers_ReportsMultipleDiagnostics()
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
                public string Name { get; set; }

                [IocInject]
                public int count;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC015_InjectedProperty_WithInjectAttribute_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class InjectAttribute : System.Attribute
            {
                public string Key { get; set; }
            }

            [IocRegister]
            public class MyService : IMyService
            {
                [Inject]
                public string Name { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("Name");
    }

    [Test]
    public async Task SGIOC015_ConstructorAndInjectedProperty_BothBuiltIn_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(string connectionString) : IMyService
            {
                [IocInject]
                public int Timeout { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC015_WithFactory_InjectedPropertyStillChecked()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Factory = nameof(Create))]
            public class MyService : IMyService
            {
                [IocInject]
                public string Name { get; set; }

                public static MyService Create() => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("Name");
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_BuiltInTypeParameter_ReportsDiagnostic()
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
                public void Initialize(string connectionString)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("connectionString");
        await Assert.That(sgioc015[0].GetMessage()).Contains("string");
        await Assert.That(sgioc015[0].GetMessage()).Contains("Method parameter");
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_IntTypeParameter_ReportsDiagnostic()
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
                public void Configure(int timeout)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("timeout");
        await Assert.That(sgioc015[0].GetMessage()).Contains("int");
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_MultipleBuiltInParameters_ReportsMultipleDiagnostics()
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
                public void Configure(string name, int timeout)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_WithDefaultValue_NoDiagnostic()
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
                public void Configure(string name = "default")
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_ParameterWithIocInjectKey_NoDiagnostic()
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
                public void Configure([IocInject(Key = "ConfigName")] string name)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_ParameterWithFromKeyedServices_NoDiagnostic()
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
                public void Configure([FromKeyedServices("ConfigName")] string name)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_ServiceTypeParameter_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public void Configure(IDependency dependency)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_MixedParameters_ReportsOnlyForBuiltIn()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public void Configure(IDependency dependency, string connectionString)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("connectionString");
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_CollectionOfBuiltInType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService
            {
                [IocInject]
                public void Configure(IEnumerable<string> names)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("names");
    }

    [Test]
    public async Task SGIOC015_InjectedMethod_WithInjectAttribute_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class InjectAttribute : System.Attribute
            {
                public string Key { get; set; }
            }

            [IocRegister]
            public class MyService : IMyService
            {
                [Inject]
                public void Configure(string name)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("name");
    }

    [Test]
    public async Task SGIOC015_MultipleInjectedMethods_ReportsAllDiagnostics()
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
                public void Initialize(string name)
                {
                }

                [IocInject]
                public void Configure(int timeout)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC015_ConstructorPropertyAndMethod_AllBuiltIn_ReportsAllDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService(string connectionString) : IMyService
            {
                [IocInject]
                public int Timeout { get; set; }

                [IocInject]
                public void Configure(bool enabled)
                {
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(3);
    }
}
