namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC008: Invalid Attribute Usage - Factory or Instance uses nameof() but the referenced member is not static or is inaccessible.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC008)]
public class SGIOC008Tests
{
    #region Factory Tests

    [Test]
    public async Task SGIOC008_Factory_WithNonStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public class MyServiceFactory
            {
                public IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("not static");
    }

    [Test]
    public async Task SGIOC008_Factory_WithPrivateStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyService.Create))]
            public class MyService : IMyService
            {
                private static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("private");
    }

    [Test]
    public async Task SGIOC008_Factory_WithStaticMethodInPrivateClass_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class OuterClass
            {
                [IocRegister(
                    Lifetime = ServiceLifetime.Singleton,
                    ServiceTypes = [typeof(IMyService)],
                    Factory = nameof(PrivateFactory.Create))]
                public class MyService : IMyService { }

                private class PrivateFactory
                {
                    public static IMyService Create(IServiceProvider sp) => new MyService();
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("private type");
    }

    [Test]
    public async Task SGIOC008_Factory_WithPublicStaticMethod_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC008_Factory_WithInternalStaticMethod_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            internal static class MyServiceFactory
            {
                internal static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC008_Factory_WithStringPath_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = "MyServiceFactory.Create")]
            public class MyService : IMyService { }

            public class MyServiceFactory
            {
                public IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        // String paths are not validated by the analyzer - they are validated at runtime
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(0);
    }

    #endregion

    #region Instance Tests

    [Test]
    public async Task SGIOC008_Instance_WithNonStaticField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Instance").And.Contains("Default").And.Contains("not static");
    }

    [Test]
    public async Task SGIOC008_Instance_WithPrivateStaticField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                private static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Instance").And.Contains("Default").And.Contains("private");
    }

    [Test]
    public async Task SGIOC008_Instance_WithNonStaticProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public MyService Default { get; } = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Instance").And.Contains("Default").And.Contains("not static");
    }

    [Test]
    public async Task SGIOC008_Instance_WithPublicStaticField_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC008_Instance_WithPublicStaticProperty_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static MyService Default { get; } = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC008_Instance_WithStringPath_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = "MyService.Default")]
            public class MyService : IMyService
            {
                public readonly MyService Default = new MyService();
            }
            """;

        // String paths are not validated by the analyzer - they are validated at runtime
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(0);
    }

    #endregion

    #region IocRegisterForAttribute Tests

    [Test]
    public async Task SGIOC008_IoCRegisterFor_Factory_WithNonStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService { }

            [IocRegisterFor(
                typeof(MyService),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyServiceFactory
            {
                public IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("not static");
    }

    [Test]
    public async Task SGIOC008_IoCRegisterFor_Instance_WithNonStaticField_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public readonly MyService Default = new MyService();
            }

            [IocRegisterFor(
                typeof(MyService),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public static class ServiceConfigurator { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Instance").And.Contains("Default").And.Contains("not static");
    }

    #endregion

    #region Assembly-Level Attribute Tests

    [Test]
    public async Task SGIOC008_AssemblyLevel_Factory_WithNonStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(
                typeof(TestNamespace.MyService),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(TestNamespace.IMyService)],
                Factory = nameof(TestNamespace.MyServiceFactory.Create))]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService { }

            public class MyServiceFactory
            {
                public IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("not static");
    }

    #endregion

    #region Protected Member Tests

    [Test]
    public async Task SGIOC008_Factory_WithProtectedStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyService.Create))]
            public class MyService : IMyService
            {
                protected static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("protected");
    }

    #endregion
}
