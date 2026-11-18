namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC014: Key does not exist - ServiceKeyAttribute is marked on parameter but no Key is registered.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC014)]
public class SGIOC014Tests
{
    [Test]
    public async Task SGIOC014_FactoryServiceKeyParameter_NoKey_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, [ServiceKey] string key) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_FactoryServiceKeyParameter_WithKey_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "MyKey", Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, [ServiceKey] string key) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).IsEmpty();
    }

    [Test]
    public async Task SGIOC014_DefaultsFactoryServiceKeyParameter_NoKey_NoDiagnostic()
    {
        // A defaults Factory is shared by keyed and non-keyed registrations, so its [ServiceKey] parameter is not reported
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.MyServiceFactory.Create))]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, [ServiceKey] object? key) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).IsEmpty();
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_NoKey_InjectMethod_ReportsDiagnostic()
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
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_NoKey_Constructor_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService([ServiceKey] string? key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_WithKey_NoDiagnostic()
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
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_WithIntKey_NoDiagnostic()
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
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_ServiceKeyAttribute_NoKey_MultipleParameters_ReportsDiagnostic()
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
                public void Initialize(IDependency dep, [ServiceKey] string? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_IocRegisterForAttribute_NoKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyService))]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService([ServiceKey] string? key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014").ToList();

        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC014_IocRegisterForAttribute_WithKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyService), Key = "MyKey")]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService([ServiceKey] string key) : IMyService
            {
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_GenericAttribute_NoKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string? key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC014_GenericAttribute_WithKey_NoDiagnostic()
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
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_NoServiceKeyAttribute_NoKey_NoDiagnostic()
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
                public void Initialize(string? name) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_KeyTypeCsharp_NoKey_NoDiagnostic()
    {
        // When KeyType is Csharp, a Key is always expected to be provided
        // This tests that KeyType.Csharp with Key still works
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(KeyType = KeyType.Csharp, Key = "nameof(MyService)")]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_MultipleServiceKeyAttributes_NoKey_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService([ServiceKey] string? ctorKey) : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] string? methodKey) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC014_EnumKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum ServiceKeyType { Key1, Key2 }

            public interface IMyService { }

            [IocRegister(Key = ServiceKeyType.Key1)]
            public class MyService : IMyService
            {
                [IocInject]
                public void Initialize([ServiceKey] ServiceKeyType key) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("Instance = nameof(Default)")]
    [Arguments("Factory = nameof(Create)")]
    public async Task SGIOC014_InstanceOrFactoryConstructorServiceKeyParameter_NoKey_NoDiagnostic(string activation)
    {
        // Instance and Factory registrations are not constructed by the container, so the constructor's [ServiceKey]
        // parameter receives no key
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister({{activation}})]
            public class MyService
            {
                public static readonly MyService Default = new("default");

                public MyService([ServiceKey] string key) { }

                public static MyService Create() => Default;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC014_KeyedInstanceFirst_ThenConstructorRegistrationWithoutKey_ReportsDiagnostic()
    {
        // The constructed registration has no key, although the first registration of the type has one
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.MyService>(Key = "default", Instance = nameof(TestNamespace.MyService.Default))]

            namespace TestNamespace;

            [IocRegister]
            public class MyService([ServiceKey] string key)
            {
                public static readonly MyService Default = new("default");
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC014_TwoRegistrationsWithoutKey_ReportsOnce()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.MyService>(Tags = ["b"])]

            namespace TestNamespace;

            [IocRegister(Tags = ["a"])]
            public class MyService([ServiceKey] string key) { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("int", 1)]
    [Arguments("string", 0)]
    public async Task SGIOC014_InjectMethodOfUnkeyedInstanceConstructedByFunc_ReportsUnlessPassedArgument(string inputType, int expected)
    {
        // The Func constructs the Instance registration's type and calls its [Inject] methods: it passes its string argument
        // to the [ServiceKey] parameter (s0.Init(arg0)), otherwise the key, but no key is registered
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Instance = nameof(Default))]
            public class Foo
            {
                public static readonly Foo Default = new();

                [IocInject]
                public void Init([ServiceKey] string key) { }
            }

            [IocRegister]
            public class Consumer(Func<{{inputType}}, Foo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("AsyncDecorator", 0)]
    [Arguments("SyncDecorator", 1)]
    public async Task SGIOC014_InstanceWithAsyncDecoratorRequestedByFunc_ReportsOnlyWhenConstructed(string decorator, int expected)
    {
        // No Func with input parameters is generated for the decorated service type of an Instance registration whose
        // decorator has async inject methods, so it does not construct the type and resolve its [ServiceKey] parameter
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister(ServiceTypes = [typeof(IFoo)], Instance = nameof(Default), Decorators = [typeof({{decorator}})])]
            public class Foo([ServiceKey] string key) : IFoo
            {
                public static readonly Foo Default = new("");
            }

            public class AsyncDecorator(IFoo inner) : IFoo
            {
                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            public class SyncDecorator(IFoo inner) : IFoo;

            [IocRegister]
            public class Consumer(Func<int, IFoo> factory);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject" });
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC014");

        await Assert.That(sgioc014).Count().IsEqualTo(expected);
    }
}
