namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC015: KeyValuePair's Key type is unmatched -
/// Injected KeyValuePair/Dictionary key type does not match any registered keyed service's key type.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC015)]
public class SGIOC015Tests
{
    [Test]
    public async Task SGIOC015_KVP_MatchingStringKey_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<string, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_KVP_MismatchedIntKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_KVP_ObjectKey_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<object, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_KVP_NoKeyedServices_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<string, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_Dictionary_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(IDictionary<int, IService> dict)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("dict").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_ReadOnlyDictionary_MatchingKey_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(IReadOnlyDictionary<string, IService> dict)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_IEnumerable_KVP_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(IEnumerable<KeyValuePair<int, IService>> kvps)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvps").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_KVP_WithFromKeyedServices_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer([FromKeyedServices("Key1")] KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_KVP_CsharpKeyType_Nameof_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = nameof(ServiceKey), KeyType = KeyType.Csharp)]
            public class ServiceA : IService
            {
                public static Guid ServiceKey => Guid.Empty;
            }

            [IocRegister]
            public class Consumer(KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    [Arguments("GuidFirst", "public static Guid ServiceKey() => Guid.Empty; public static int ServiceKey(int x) => x;")]
    [Arguments("IntFirst", "public static int ServiceKey(int x) => x; public static Guid ServiceKey() => Guid.Empty;")]
    public async Task SGIOC015_KVP_CsharpKeyType_NameofOfOverloadedMethod_KeyTypeUnknown_NoDiagnostic(string scenario, string methods)
    {
        // nameof() of a method group cannot choose an overload, so the key type is unknown (treated as compatible) rather
        // than the return type of whichever overload comes first
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = nameof(ServiceKey), KeyType = KeyType.Csharp)]
            public class ServiceA : IService
            {
                {{methods}}
            }

            [IocRegister]
            public class Consumer(KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0).Because(scenario);
    }

    [Test]
    public async Task SGIOC015_ReadOnlyDictionary_CsharpKeyType_Nameof_MatchingGuid_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = nameof(ServiceKey), KeyType = KeyType.Csharp)]
            public class ServiceA : IService
            {
                public static Guid ServiceKey => Guid.Empty;
            }

            [IocRegister]
            public class Consumer(IReadOnlyDictionary<Guid, IService> services)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_KVP_CsharpKeyType_StringLiteral_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "\"Key1\"", KeyType = KeyType.Csharp)]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_KVP_MultipleKeyedServices_OneMatches_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister(Key = 42)]
            public class ServiceB : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<string, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_KVP_MultipleKeyedServices_NoneMatches_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister(Key = "Key2")]
            public class ServiceB : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_KVP_EnumKey_Matching_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum MyKey { A, B }

            public interface IService { }

            [IocRegister(Key = MyKey.A)]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<MyKey, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015");

        await Assert.That(sgioc015).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC015_InjectMethod_KVP_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer
            {
                [IocInject]
                public void Initialize(KeyValuePair<int, IService> kvp) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_InjectProperty_KVP_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer
            {
                [IocInject]
                public KeyValuePair<int, IService> Kvp { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("Kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_InjectField_KVP_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IService { }

            [IocRegister]
            public class Consumer
            {
                [IocInject]
                public KeyValuePair<int, IService> kvp;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_KVP_BaseClassValueType_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class ServiceBase { }

            public class IntermediateService : ServiceBase { }

            [IocRegister(Key = "Key1")]
            public class ServiceA : IntermediateService { }

            [IocRegister]
            public class Consumer(KeyValuePair<int, ServiceBase> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("ServiceBase");
    }

    [Test]
    public async Task SGIOC015_KVP_AssemblyLevelCsharpKeyType_Nameof_MismatchedKey_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.ServiceB), ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IService)])]
            [assembly: IocRegisterFor(typeof(TestNamespace.ServiceA), ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IService)], Key = nameof(TestNamespace.ServiceA.ServiceKey), KeyType = KeyType.Csharp)]

            namespace TestNamespace;

            public interface IService { }

            public class ServiceA : IService
            {
                public static Guid ServiceKey => Guid.Empty;
            }

            public class ServiceB : IService { }

            [IocRegister]
            public class Consumer(KeyValuePair<int, IService> kvp)
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int").And.Contains("IService");
    }

    [Test]
    public async Task SGIOC015_KVP_TupleElementNamesDiffer_MismatchedKey_ReportsDiagnostic()
    {
        // Tuple element names are not part of the runtime type, so the keyed registration is a value of the pair
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(Key = "Key1", ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public class KeyedHandler : IHandler<(Entity X, string Y)>;

            [IocRegister]
            public class Consumer(KeyValuePair<int, IHandler<(Entity M, string N)>> kvp);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc015 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC015").ToList();

        await Assert.That(sgioc015).Count().IsEqualTo(1);
        await Assert.That(sgioc015[0].GetMessage()).Contains("kvp").And.Contains("int");
    }
}
