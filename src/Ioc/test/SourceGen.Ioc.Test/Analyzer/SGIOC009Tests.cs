using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC009: Invalid Attribute Usage - Instance is specified with an explicit Lifetime that is not Singleton.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC009)]
public class SGIOC009Tests
{
    [Test]
    public async Task SGIOC009_Descriptor_IsNotCompilationEnd()
    {
        // SGIOC009 depends on the attribute only, so it is reported live by the attribute's file analysis
        await Assert.That(RegisterAnalyzer.InstanceRequiresSingleton.CustomTags).DoesNotContain(WellKnownDiagnosticTags.CompilationEnd);
    }

    [Test]
    [Arguments("IocRegister", "", "[IocRegister(ServiceLifetime.Scoped, Instance = nameof(Default))] public sealed class MyService : IMyService { public static readonly MyService Default = new(); }")]
    [Arguments("IocRegisterOfT", "", "[IocRegister<IMyService>(ServiceLifetime.Scoped, Instance = nameof(Default))] public sealed class MyService : IMyService { public static readonly MyService Default = new(); }")]
    [Arguments("IocRegisterFor", "", "[IocRegisterFor(typeof(MyService), Lifetime = ServiceLifetime.Scoped, Instance = nameof(MyService.Default))] public static class Config; public sealed class MyService : IMyService { public static readonly MyService Default = new(); }")]
    [Arguments("IocRegisterForOfT", "", "[IocRegisterFor<MyService>(ServiceLifetime.Scoped, Instance = nameof(MyService.Default))] public static class Config; public sealed class MyService : IMyService { public static readonly MyService Default = new(); }")]
    [Arguments("AssemblyLevel", "[assembly: IocRegisterFor(typeof(TestNamespace.MyService), Lifetime = ServiceLifetime.Scoped, Instance = nameof(TestNamespace.MyService.Default))]", "public sealed class MyService : IMyService { public static readonly MyService Default = new(); }")]
    [Arguments("AssemblyLevelOfT", "[assembly: IocRegisterFor<TestNamespace.MyService>(ServiceLifetime.Scoped, Instance = nameof(TestNamespace.MyService.Default))]", "public sealed class MyService : IMyService { public static readonly MyService Default = new(); }")]
    [Arguments("OpenGeneric", "", "public interface INode<T>; [IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(INode<>)], Instance = nameof(Default))] public sealed class Node<T> : INode<T> { public static readonly Node<T> Default = new(); }")]
    public async Task SGIOC009_ExplicitNonSingletonLifetime_ReportedOnceByFileAnalysis(string scenario, string assemblyAttributes, string declarations)
    {
        // Reported exactly once per attribute by the file analysis (RunAnalyzerAsync also checks that a diagnostic reported
        // by file analysis is not tagged CompilationEnd), also with default settings that are known only at compilation end
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{assemblyAttributes}}
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IMyService;

            {{declarations}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC009).ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("'Scoped'");
        await Assert.That(sgioc009[0].Descriptor.CustomTags).DoesNotContain(WellKnownDiagnosticTags.CompilationEnd);
    }

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

        // Without an explicit Lifetime, an Instance registration is Singleton (not the Transient fallback)
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009");

        await Assert.That(sgioc009).Count().IsEqualTo(0);
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
    [Arguments("AssemblyLevelSingleton", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]", "", null)]
    [Arguments("TypeLevelSingleton", "", "[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton)]", null)]
    [Arguments("MsBuildSingleton", "", "", "Singleton")]
    [Arguments("AssemblyLevelScoped", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]", "", null)]
    [Arguments("TypeLevelTransient", "", "[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Transient)]", null)]
    [Arguments("DefaultsOverMsBuild", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]", "", "Singleton")]
    [Arguments("MsBuildScoped", "", "", "scoped")]
    [Arguments("MsBuildTransient", "", "", "Transient")]
    [Arguments("NoDefaults", "", "", null)]
    public async Task SGIOC009_InstanceWithoutExplicitLifetime_IsSingleton(
        string scenario, string assemblyDefaults, string typeDefaults, string? msBuildLifetime)
    {
        // Like the generator, an Instance registration without an explicit Lifetime is Singleton: its default settings,
        // SourceGenIocDefaultLifetime and the Transient fallback do not apply (issue #207)
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

            [IocRegisterFor(typeof(OtherService), Instance = nameof(OtherService.Default))]
            public static class Config { }

            public class OtherService : IMyService
            {
                public static readonly OtherService Default = new OtherService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: msBuildLifetime is null ? null : new Dictionary<string, string> { ["build_property.SourceGenIocDefaultLifetime"] = msBuildLifetime });
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC009);

        await Assert.That(sgioc009).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.Defaults)]
    [Arguments("ConstructorArgument", "ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)]", "Scoped")]
    [Arguments("NamedArgument", "Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)]", "Transient")]
    public async Task SGIOC009_InstanceWithExplicitNonSingletonLifetime_ReportsDespiteSingletonDefaults(string scenario, string arguments, string reportedLifetime)
    {
        // An explicit Lifetime of the attribute still applies, whatever the default settings and SourceGenIocDefaultLifetime
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister({{arguments}}, Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocDefaultLifetime"] = "Singleton" });
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC009).ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains($"'{reportedLifetime}'");
    }

    [Test]
    [Category(Constants.Defaults)]
    [Category(Constants.SGIOC004)]
    public async Task InstanceWithoutExplicitLifetime_SingletonDependent_NoLifetimeConflict()
    {
        // The Instance is Singleton for the lifetime conflict checks too, not the Transient of SourceGenIocDefaultLifetime
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer(IMyService service) { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocDefaultLifetime"] = "Transient" });

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC004)).Count().IsEqualTo(0);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC009)).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.Defaults)]
    [Category(Constants.SGIOC003)]
    public async Task OpenGenericInstanceWithoutExplicitLifetime_SingletonDependent_NoLifetimeConflict()
    {
        // The Scoped default settings do not apply to the lifetime of an open generic Instance either
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.INode<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface INode<T> { }

            [IocRegister(ServiceTypes = [typeof(INode<>)], Instance = nameof(Default))]
            public sealed class Node<T> : INode<T>
            {
                public static readonly Node<T> Default = new();
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public class Consumer(INode<string> node) { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC003)).Count().IsEqualTo(0);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC009)).Count().IsEqualTo(0);
    }

    #endregion
}
