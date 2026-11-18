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

    [Test]
    public async Task SGIOC013_LaterRegistrationWithMismatchedKeyType_ReportsOnce()
    {
        // Every keyed registration passes its own key: the int keys do not match the string parameter, the string key does
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "MyKey")]
            public class MyService([ServiceKey] string key) { }

            [IocRegisterFor<MyService>(Key = 42)]
            [IocRegisterFor<MyService>(Key = 7)]
            public static class Registrations;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013").ToList();

        await Assert.That(sgioc013).Count().IsEqualTo(1);
        await Assert.That(sgioc013[0].GetMessage()).Contains("int").And.Contains("string");
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncWithArguments_ReportsDiagnostic()
    {
        // A Func with input parameters constructs the implementation even for an Instance registration, and passes the
        // registration's string key to the int [ServiceKey] parameter
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, Foo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInFactoryParameter_ReportsDiagnostic()
    {
        // The Func requested by a factory method's parameter constructs Foo too
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister(Factory = nameof(Create))]
            public class Bar
            {
                public static Bar Create(Func<string, Foo> make) => new();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInMemberOfInstanceRegistration_ReportsDiagnostic()
    {
        // The generator scans the injected members of every registration for requested Funcs, also of an Instance one
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister(Instance = nameof(Default))]
            public class Bar
            {
                public static readonly Bar Default = new();

                [IocInject]
                public Func<string, Foo> Make { get; set; } = null!;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InjectMethodOfInstanceConstructedByFunc_ReportsDiagnostic()
    {
        // The Func constructs the Instance registration's type and calls its [Inject] methods with the registration key
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo
            {
                public static readonly Foo Default = new();

                [IocInject]
                public void Init([ServiceKey] int key) { }
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, Foo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceWithAsyncInjectMethodConstructedByFunc_ReportsDiagnostic()
    {
        // An Instance registration is never async-init, so the Func is registered and constructs the type
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);

                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, Foo> factory);
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("Func<string, IFoo>", "AsyncDecorator", "", 0)]
    [Arguments("Func<string, IFoo>", "AsyncDecorator", "[IocContainer] public partial class AppContainer;", 0)]
    [Arguments("Lazy<Func<string, IFoo>>", "AsyncDecorator", "[IocContainer] public partial class AppContainer;", 0)]
    [Arguments("Func<string, Foo>", "AsyncDecorator", "", 1)]
    [Arguments("Func<string, IFoo>", "SyncDecorator", "", 1)]
    [Arguments("Func<string, IFoo>", "SyncDecorator", "[IocContainer] public partial class AppContainer;", 1)]
    public async Task SGIOC013_InstanceWithAsyncDecoratorRequestedByFunc_ReportsOnlyWhenConstructed(string funcType, string decorator, string container, int expected)
    {
        // A Func with input parameters is not generated for the decorated service type of an Instance registration whose
        // decorator has async inject methods (the Register output does not register it, a container resolves it from the
        // fallback provider), so it does not construct the type; the undecorated implementation type is still constructed
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister(Key = "a", ServiceTypes = [typeof(IFoo)], Instance = nameof(Default), Decorators = [typeof({{decorator}})])]
            public class Foo([ServiceKey] int key) : IFoo
            {
                public static readonly Foo Default = new(0);
            }

            public class AsyncDecorator(IFoo inner) : IFoo
            {
                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            public class SyncDecorator(IFoo inner) : IFoo;

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] {{funcType}} factory);

            {{container}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject" });
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC013_FactoryWithAsyncInjectMethodRequestedByFunc_NoDiagnostic()
    {
        // No Func with input parameters is registered for an async-init registration, so nothing constructs the type
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Factory = nameof(Create))]
            public class Foo([ServiceKey] int key)
            {
                public static Foo Create() => new(0);

                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, Foo> factory);
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).IsEmpty();
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInCollection_ReportsDiagnostic()
    {
        // A Func requested as a collection element is demanded too
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] IEnumerable<Func<string, Foo>> factories);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInDecorator_ReportsDiagnostic()
    {
        // A Func requested by an applied decorator is demanded too
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            public interface IService { }

            public class Decorator(IService inner, [FromKeyedServices("a")] Func<string, Foo> factory) : IService;

            [IocRegister(ServiceTypes = [typeof(IService)], Decorators = [typeof(Decorator)])]
            public class Service : IService;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("Func<int, Foo>", 0)]
    [Arguments("Func<string, Foo>", 1)]
    [Arguments("Func<string, Foo> make, Func<int, Foo>", 1)]
    public async Task SGIOC013_InstanceConstructedByFunc_ChecksKeyUnlessPassedArgument(string funcs, int expected)
    {
        // The Func passes its int argument to the [ServiceKey] parameter (new Foo(arg0)) instead of the string key; every Func
        // type that constructs the registration is checked
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] {{funcs}} factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("int", 1)]
    [Arguments("int, int", 0)]
    public async Task SGIOC013_FuncArgumentsTakenByEarlierParameters_ChecksKey(string inputTypes, int expected)
    {
        // Each argument goes to the first unused parameter of its type: the constructor's, then the injected properties',
        // then the inject methods'; with one int argument, the constructor's count takes it and the key is passed to Init
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo(int count)
            {
                public static readonly Foo Default = new(0);

                [IocInject]
                public void Init([ServiceKey] int key) { }
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<{{inputTypes}}, Foo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC013_FuncSkipsTaskInjectMethodWithoutAsyncFeature_NoDiagnostic()
    {
        // The Func never calls a Task-returning inject method, whatever the features, so the argument goes to Init
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo
            {
                public static readonly Foo Default = new();

                [IocInject]
                public Task Setup(int x) => Task.CompletedTask;

                [IocInject]
                public void Init([ServiceKey] int key) { }
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<int, Foo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).IsEmpty();
    }

    [Test]
    public async Task SGIOC013_FuncArgumentTakenByInjectedPropertyBeforeMethod_ReportsDiagnostic()
    {
        // Injected properties take their arguments before the inject methods, also when declared after them
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo
            {
                public static readonly Foo Default = new();

                [IocInject]
                public void Init([ServiceKey] int key) { }

                [IocInject]
                public int Count { get; set; }
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<int, Foo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInSelfConstructedGenericFactoryRegistration_ReportsDiagnostic()
    {
        // The generic factory's mapping does not match Handler itself, so its self-registration is constructed and its
        // constructor requests the Func
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IHandler<int>), typeof(int))]
                public static IHandler<T> Create<T>() => throw new NotImplementedException();
            }

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister(ServiceTypes = [typeof(IHandler<string>)], Factory = nameof(FactoryContainer.Create))]
            public sealed class Handler([FromKeyedServices("a")] Func<string, Foo> make) : IHandler<string>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInInjectMembers_ReportsDiagnostic()
    {
        // A member listed in InjectMembers is injected like an [IocInject] member, so it requests the Func too
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.Consumer), InjectMembers = [nameof(TestNamespace.Consumer.Make)])]

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            public class Consumer
            {
                public Func<string, Foo> Make { get; set; } = null!;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("public partial Func<string, Foo> Make();", "Register,Container", 1)]
    [Arguments("public partial Func<string, Foo> Make { get; }", "Register,Container", 1)]
    [Arguments("public partial Func<string, Foo> Make();", "Register", 0)]
    [Arguments("public Func<string, Foo> Make() => null!;", "Register,Container", 0)]
    [Arguments("public partial Func<string, Foo> Make();", "Register,Container", 0, "")]
    public async Task SGIOC013_InstanceConstructedByFuncInContainerAccessor_ReportsDiagnostic(string accessor, string features, int expected, string modifier = "partial ")
    {
        // The generated container implements its partial accessors, so a Func it returns constructs Foo too; the generator
        // implements no other member, and no container that is not a partial class
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocContainer]
            public {{modifier}}class AppContainer
            {
                [IocInject(Key = "a")]
                {{accessor}}
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = features });
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncOfUnboundServiceType_ReportsDiagnostic()
    {
        // typeof(IFoo<>) registers Foo as each closed form it implements, IFoo<int>, which the Func returns
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo<T>;

            [IocRegister(Key = "a", Instance = nameof(Default), ServiceTypes = [typeof(IFoo<>)])]
            public class Foo([ServiceKey] int key) : IFoo<int>
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, IFoo<int>> make);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("[IocContainer(ExplicitOnly = true)]", "", 0)]
    [Arguments("[IocContainer(ExplicitOnly = true)]", "[IocRegisterFor(typeof(Foo), Key = \"a\", Instance = nameof(Foo.Default))]", 1)]
    [Arguments("[IocContainer(IncludeTags = [\"app\"])]", "", 0)]
    [Arguments("[IocContainer(IncludeTags = [\"other\"])]", "", 1)]
    [Arguments("[IocContainer]", "", 1)]
    public async Task SGIOC013_InstanceConstructedByFuncInContainerAccessor_OnlyInContainerScope(string container, string explicitRegistration, int expected)
    {
        // Like the generator, the accessor's Func constructs a registration of the container's own registrations
        // (ExplicitOnly: its [IocRegisterFor]; with IncludeTags: those with one of them); otherwise the Func is resolved
        // from the fallback provider, where nothing registers it
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default), Tags = ["other"])]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister(Tags = ["app"])]
            public class Bar;

            {{container}}
            [IocRegisterFor(typeof(Bar))]
            {{explicitRegistration}}
            public partial class AppContainer
            {
                [IocInject(Key = "a")]
                public partial Func<string, Foo> Make();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(true, 1)]
    public async Task SGIOC013_InstanceConstructedByFuncInContainerAccessor_OnlyLastRegistration(bool instanceLast, int expected)
    {
        // Like the generator, the accessor's Func constructs only the last registration of its return type and key
        const string instanceRegistration = "[assembly: IocRegisterFor(typeof(TestNamespace.Foo1), Key = \"a\", Instance = nameof(TestNamespace.Foo1.Default), ServiceTypes = [typeof(TestNamespace.IFoo)])]";
        const string constructedRegistration = "[assembly: IocRegisterFor(typeof(TestNamespace.Foo2), Key = \"a\", ServiceTypes = [typeof(TestNamespace.IFoo)])]";
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{(instanceLast ? constructedRegistration : instanceRegistration)}}
            {{(instanceLast ? instanceRegistration : constructedRegistration)}}

            namespace TestNamespace;

            public interface IFoo;

            public class Foo1([ServiceKey] int key) : IFoo
            {
                public static readonly Foo1 Default = new(0);
            }

            public class Foo2([ServiceKey] string key) : IFoo;

            [IocContainer]
            public partial class AppContainer
            {
                [IocInject(Key = "a")]
                public partial Func<string, IFoo> Make();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments(true, 0)]
    [Arguments(false, 1)]
    public async Task SGIOC013_InstanceConstructedByFuncInExplicitOnlyContainerAccessor_UsesAttributeOrder(bool instanceFirst, int expected)
    {
        // Like the generator, an ExplicitOnly container adds its [IocRegisterFor] in their order, unlike the Register output,
        // which adds the non-generic attributes before the generic ones
        const string instanceRegistration = "[IocRegisterFor<Foo1>(Key = \"a\", Instance = nameof(Foo1.Default), ServiceTypes = [typeof(IFoo)])]";
        const string constructedRegistration = "[IocRegisterFor(typeof(Foo2), Key = \"a\", ServiceTypes = [typeof(IFoo)])]";
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            public class Foo1([ServiceKey] int key) : IFoo
            {
                public static readonly Foo1 Default = new(0);
            }

            public class Foo2([ServiceKey] string key) : IFoo;

            [IocContainer(ExplicitOnly = true)]
            {{(instanceFirst ? instanceRegistration : constructedRegistration)}}
            {{(instanceFirst ? constructedRegistration : instanceRegistration)}}
            public partial class AppContainer
            {
                [IocInject(Key = "a")]
                public partial Func<string, IFoo> Make { get; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncInContainerAccessorWithOtherKey_NoDiagnostic()
    {
        // The accessor resolves the non-keyed Foo, which is not registered, so its Func is resolved from the fallback provider
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocContainer]
            public partial class AppContainer
            {
                public partial Func<string, Foo> Make();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).IsEmpty();
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByRegisteredFuncInContainerAccessor_NoDiagnostic()
    {
        // The container registers the Func type itself, which the accessor returns instead of constructing Foo
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(Func<string, TestNamespace.Foo>), Key = "a", Instance = nameof(TestNamespace.Factories.MakeFoo))]

            namespace TestNamespace;

            public static class Factories
            {
                public static readonly Func<string, Foo> MakeFoo = _ => Foo.Default;
            }

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocContainer]
            public partial class AppContainer
            {
                [IocInject(Key = "a")]
                public partial Func<string, Foo> Make();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).IsEmpty();
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFuncCollectionInContainerAccessor_NoDiagnostic()
    {
        // The container resolves a collection of Funcs with input parameters from the fallback provider
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocContainer]
            public partial class AppContainer
            {
                [IocInject(Key = "a")]
                public partial IEnumerable<Func<string, Foo>> Make();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).IsEmpty();
    }

    [Test]
    [Arguments("Func<int, Foo>", "nameof(TestNamespace.Foo.Count)", 1)]
    [Arguments("Func<int, int, Foo>", "nameof(TestNamespace.Foo.Count)", 0)]
    [Arguments("Func<string, Foo>", "nameof(TestNamespace.Foo.Init)", 1)]
    public async Task SGIOC013_InjectMembersOfInstanceConstructedByFunc_ReportsDiagnostic(string funcType, string injectMember, int expected)
    {
        // Like the generator, the Func injects the members listed in InjectMembers with the [IocInject] members: Count takes
        // the int argument before Init's key parameter, which then receives the key "a"; a listed Init receives it too
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.Foo), Key = "a", Instance = nameof(TestNamespace.Foo.Default), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public class Foo
            {
                public static readonly Foo Default = new();

                public int Count { get; set; }

                {{(injectMember.Contains("Init") ? "" : "[IocInject]")}}
                public void Init([ServiceKey] int key) { }
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] {{funcType}} make);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("Lazy<Func<string, Foo>>", "[IocContainer]", 1)]
    [Arguments("Func<Func<string, Foo>>", "[IocContainer]", 1)]
    [Arguments("KeyValuePair<string, Func<string, Foo>>", "[IocContainer]", 1)]
    [Arguments("Lazy<Lazy<Func<string, Foo>>>", "[IocContainer]", 1)]
    [Arguments("Task<Func<string, Foo>>", "[IocContainer]", 0)]
    [Arguments("Lazy<IEnumerable<Func<string, Foo>>>", "[IocContainer]", 0)]
    [Arguments("Lazy<Func<string, Foo>>", "[IocContainer(ExplicitOnly = true)]", 0)]
    [Arguments("Lazy<Func<string, Foo>>", "", 0)]
    public async Task SGIOC013_InstanceConstructedByNestedFuncInContainer_ReportsDiagnostic(string dependencyType, string container, int expected)
    {
        // Like the generator, a container constructs a Func with input parameters inline inside Lazy, a parameterless Func or a
        // KeyValuePair, for its own registrations; the Register output resolves the inner Func, which it does not register
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] {{dependencyType}} make);

            {{(container.Length == 0 ? "" : container + "\npublic partial class AppContainer;")}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("[IocInject(Key = \"a\")] public partial Lazy<Func<string, Foo>> Make();")]
    [Arguments("[IocInject(Key = \"a\")] public partial KeyValuePair<string, Func<string, Foo>> Make { get; }")]
    public async Task SGIOC013_InstanceConstructedByNestedFuncInContainerAccessor_ReportsDiagnostic(string accessor)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocContainer]
            public partial class AppContainer
            {
                {{accessor}}
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByNestedFuncInContainerInjectedProperty_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister]
            public class Consumer
            {
                [IocInject(Key = "a")]
                public Lazy<Func<string, Foo>> Make { get; set; } = null!;
            }

            [IocContainer]
            public partial class AppContainer;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("[IocRegister] public class Consumer { [IocInject(Key = \"a\")] public void Init(Lazy<Func<string, Foo>> make) { } }")]
    [Arguments("[IocRegister(Factory = nameof(Create))] public class Consumer { public static Consumer Create([FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) => new(); }")]
    [Arguments("[IocRegister(Instance = nameof(Default))] public class Bar([FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) { public static readonly Bar Default = new(null!); }\n[IocRegister] public class Consumer(Func<int, Bar> make);")]
    [Arguments("[IocRegister(Instance = nameof(Default))] public class Bar([FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) { public static readonly Bar Default = new(null!); }\n[IocRegister] public class Consumer(Lazy<Func<int, Bar>> make);")]
    [Arguments("public interface IHandler<T>;\n[IocRegister(ServiceTypes = [typeof(IHandler<int>)], Decorators = [typeof(Deco<>)])] public class Handler : IHandler<int>;\npublic class Deco<T>(IHandler<T> inner, [FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) : IHandler<T>;")]
    [Arguments("public interface IHandler;\n[IocRegister(ServiceTypes = [typeof(IHandler)], Decorators = [typeof(Deco)])] public class Handler : IHandler;\npublic class Deco(IHandler inner, [FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) : IHandler;")]
    [Arguments("public interface IBar;\n[IocRegister(Instance = nameof(Default), ServiceTypes = [typeof(IBar)], Decorators = [typeof(Deco)])] public class Bar(int id) : IBar { public static readonly Bar Default = new(0); }\npublic class Deco(IBar inner, [FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) : IBar;\n[IocRegister] public class Consumer(Func<int, IBar> make);", 1)]
    [Arguments("public interface IBar;\n[IocRegister(Instance = nameof(Default), ServiceTypes = [typeof(IBar)], Decorators = [typeof(Deco)])] public class Bar(int id) : IBar { public static readonly Bar Default = new(0); }\npublic class Deco(IBar inner, [FromKeyedServices(\"a\")] Lazy<Func<string, Foo>> make) : IBar;\n[IocRegister] public class Consumer(IBar bar);", 0)]    public async Task SGIOC013_InstanceConstructedByNestedFuncInContainerDependency_ReportsDiagnostic(string declarations, int expected = 1)
    {
        // Like the generator, the container resolves inline the dependencies of an inject method, a factory, a decorator (an
        // open generic one closed for the service type), and of an Instance registration that a Func constructs, but not of an
        // Instance registration resolved as is
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            {{declarations}}

            [IocContainer]
            public partial class AppContainer;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("[IocContainer(ExplicitOnly = true)]\n[IocRegisterFor(typeof(Consumer))]\n[IocRegisterFor(typeof(Foo), Key = \"a\", Instance = nameof(Foo.Default))]", 1)]
    [Arguments("[IocContainer(IncludeTags = [\"app\"])]", 1)]
    [Arguments("[IocContainer(IncludeTags = [\"other\"])]", 0)]
    public async Task SGIOC013_InstanceConstructedByNestedFuncInContainerScope_ReportsDiagnostic(string container, int expected)
    {
        // Only the registrations of the container resolve their dependencies there
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default), Tags = ["app", "other"])]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            [IocRegister(Tags = ["app"])]
            public class Consumer([FromKeyedServices("a")] Lazy<Func<string, Foo>> make);

            {{container}}
            public partial class AppContainer;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByNestedFuncInContainerInjectMembers_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.Consumer), InjectMembers = [new object[] { nameof(TestNamespace.Consumer.Make), "a" }])]

            namespace TestNamespace;

            [IocRegister(Key = "a", Instance = nameof(Default))]
            public class Foo([ServiceKey] int key)
            {
                public static readonly Foo Default = new(0);
            }

            public class Consumer
            {
                public Lazy<Func<string, Foo>> Make { get; set; } = null!;
            }

            [IocContainer]
            public partial class AppContainer;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc013 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013");

        await Assert.That(sgioc013).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("\"a\"", 0)]
    [Arguments("\"b\"", 1)]
    public async Task SGIOC013_KeyedInstanceConstructedByFunc_ExplicitFuncRegistration_FollowsGeneratedFunc(string explicitKey, int expected)
    {
        // An explicit Func<string, Foo> under Foo's key replaces the generated Func, which then does not pass the string key
        // to the int [ServiceKey] parameter; under another key, the generated Func is still registered
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Instance = nameof(Default), Key = "a")]
            public class Foo(string name, [ServiceKey] int key)
            {
                public static readonly Foo Default = new("", 0);
            }

            public static class Wrappers
            {
                public static readonly Func<string, Foo> Make = _ => Foo.Default;
            }

            [IocRegisterFor<Func<string, Foo>>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.Wrappers.Make", Key = {{explicitKey}})]
            public static class Registrations;

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, Foo> make);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013")).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC013_InstanceConstructedByFunc_AsyncDecoratorOnOtherRegistration_ReportsDiagnostic()
    {
        // Like the generator (CollectFuncEntries), whether a registration is async-init is decided per registration: the
        // async decorator of the "async" registration does not keep the Func<string, IFoo> of the "sync" Instance
        // registration from constructing Foo with the string key "a"
        var source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            public class Foo([ServiceKey] int key) : IFoo
            {
                public static readonly Foo Default = new(0);
                public static Foo Create() => Default;
            }

            public class AsyncDecorator(IFoo inner) : IFoo
            {
                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegisterFor<Foo>(ServiceTypes = [typeof(IFoo)], Key = "a", Tags = ["sync"], Instance = "global::TestNamespace.Foo.Default")]
            [IocRegisterFor<Foo>(ServiceTypes = [typeof(IFoo)], Key = "a", Tags = ["async"], Factory = nameof(Foo.Create), Decorators = [typeof(AsyncDecorator)])]
            public static class Registrations;

            [IocRegister]
            public class Consumer([FromKeyedServices("a")] Func<string, IFoo> make);
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source, analyzerConfigOptions: analyzerConfigOptions);
        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, analyzerConfigOptions: analyzerConfigOptions);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("new global::System.Func<string, global::TestNamespace.IFoo>((string arg0) =>");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC013")).Count().IsEqualTo(1);
    }
}