namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC009: Invalid Attribute Usage - Instance is specified but Lifetime is not Singleton.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC009)]
public class SGIOC009Tests
{
    [Test]
    public async Task SGIOC009_Instance_WithTransientLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("Default").And.Contains("Transient");
    }

    [Test]
    public async Task SGIOC009_Instance_WithScopedLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("Default").And.Contains("Scoped");
    }

    [Test]
    public async Task SGIOC009_Instance_WithSingletonLifetime_NoDiagnostic()
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
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009");

        await Assert.That(sgioc009).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC009_Instance_WithDefaultLifetime_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        // Default lifetime is Transient, Instance requires Singleton, so diagnostic should fire
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009");

        await Assert.That(sgioc009).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC009_Instance_WithStringPath_AndTransientLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService)],
                Instance = "MyService.Default")]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("MyService.Default").And.Contains("Transient");
    }

    [Test]
    public async Task SGIOC009_Factory_WithTransientLifetime_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        // Factory with Transient is allowed - only Instance requires Singleton
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009");

        await Assert.That(sgioc009).Count().IsEqualTo(0);
    }

    #region IocRegisterForAttribute Tests

    [Test]
    public async Task SGIOC009_IoCRegisterFor_Instance_WithTransientLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }

            [IocRegisterFor(
                typeof(MyService),
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public static class ServiceConfigurator { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("Default").And.Contains("Transient");
    }

    [Test]
    public async Task SGIOC009_IoCRegisterFor_Instance_WithScopedLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }

            [IocRegisterFor(
                typeof(MyService),
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public static class ServiceConfigurator { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("Default").And.Contains("Scoped");
    }

    #endregion

    #region Assembly-Level Attribute Tests

    [Test]
    public async Task SGIOC009_AssemblyLevel_Instance_WithTransientLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(
                typeof(TestNamespace.MyService),
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(TestNamespace.IMyService)],
                Instance = nameof(TestNamespace.MyService.Default))]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("Default").And.Contains("Transient");
    }

    [Test]
    public async Task SGIOC009_AssemblyLevel_Instance_WithSingletonLifetime_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(
                typeof(TestNamespace.MyService),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(TestNamespace.IMyService)],
                Instance = nameof(TestNamespace.MyService.Default))]

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009");

        await Assert.That(sgioc009).Count().IsEqualTo(0);
    }

    #endregion

    #region Effective lifetime

    [Test]
    [Category(Constants.Defaults)]
    [Arguments("AssemblyLevelSingleton", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]", "", null, 0, null)]
    [Arguments("TypeLevelSingleton", "", "[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton)]", null, 0, null)]
    [Arguments("MsBuildSingleton", "", "", "Singleton", 0, null)]
    [Arguments("DefaultsOverMsBuild", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]", "", "Singleton", 1, "Scoped")]
    [Arguments("MsBuildScoped", "", "", "scoped", 1, "Scoped")]
    [Arguments("NoDefaults", "", "", null, 1, "Transient")]
    public async Task SGIOC009_InstanceWithoutExplicitLifetime_UsesEffectiveLifetime(
        string scenario, string assemblyDefaults, string typeDefaults, string? msBuildLifetime, int expected, string? reportedLifetime)
    {
        // Like the generator, the lifetime of a registration without one comes from its best default settings, otherwise
        // from SourceGenIocDefaultLifetime, otherwise Transient; an Instance registration is only registered as Singleton
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{assemblyDefaults}}

            namespace TestNamespace;

            {{typeDefaults}}
            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: msBuildLifetime is null ? null : new Dictionary<string, string> { ["build_property.SourceGenIocDefaultLifetime"] = msBuildLifetime });
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC009).ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(expected);
        if(reportedLifetime is not null)
        {
            await Assert.That(sgioc009[0].GetMessage()).Contains($"'{reportedLifetime}'");
        }
    }

    #endregion
}
