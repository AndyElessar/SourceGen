using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC001: Invalid type for IoC registration (private/abstract, or not constructible without a Factory or Instance).
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC001)]
public class SGIOC001Tests
{
    [Test]
    public async Task SGIOC001_PrivateClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IocRegister]
                private class PrivateService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_AbstractClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public abstract class AbstractService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_InterfaceTargetTypeWithFactory_ReportsNoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class FastService : IMyService { }

            public class SafeService : IMyService { }

            [IocRegisterFor<IMyService>(ServiceLifetime.Singleton, Factory = nameof(MyServiceFactory.Create))]
            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) =>
                    Environment.ProcessorCount > 1 ? new FastService() : new SafeService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgiocDiagnostics = diagnostics.Where(static d => d.Id.StartsWith("SGIOC", StringComparison.Ordinal)).ToList();

        await Assert.That(sgiocDiagnostics).IsEmpty();
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_PrivateTargetType_ReportsDiagnostic()
    {
        // Note: When using IocRegisterForAttribute with typeof(), the type must be accessible
        // So we test with a nested private class where the attribute is on an outer accessible type
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IocRegisterFor(typeof(PrivateService))]
                private class PrivateService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_AbstractTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class AbstractService { }

            [IocRegisterFor(typeof(AbstractService))]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_PublicConcreteClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class ValidService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001");

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC001_InternalConcreteClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            internal class InternalService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001");

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("PrivateConstructor", "[IocRegister] public sealed class Service { private Service() { } }", 1)]
    [Arguments("ProtectedConstructor", "[IocRegister] public class Service { protected Service() { } }", 1)]
    [Arguments("OpenPrivateConstructor", "[IocRegister] public sealed class Service<T> { private Service() { } }", 1)]
    [Arguments("InterfaceTarget", "public interface Service; [IocRegisterFor<Service>] public sealed class Module;", 1)]
    [Arguments("InternalConstructor", "[IocRegister] public sealed class Service { internal Service() { } }", 0)]
    [Arguments("ImplicitConstructor", "[IocRegister] public sealed class Service;", 0)]
    [Arguments("OwnFactory", "[IocRegister(Factory = nameof(Create))] public sealed class Service { private Service() { } public static Service Create() => new(); }", 0)]
    [Arguments("DefaultFactory", "public interface IService; [IocRegister(RegisterAllInterfaces = true)] public sealed class Service : IService { private Service() { } public static IService Create() => new Service(); }", 0)]
    [Arguments("Instance", "[IocRegister(ServiceLifetime.Singleton, Instance = nameof(Default))] public sealed class Service { private Service() { } public static readonly Service Default = new(); }", 0)]
    [Arguments("DefaultsImplementationTypes", "public interface IService; public sealed class Service : IService { private Service() { } }", 1)]
    [Arguments("DefaultsImplementationTypesWithFactory", "public interface IService; public sealed class Service : IService { private Service() { } public static IService Create() => new Service(); }", 1)]
    [Arguments("DefaultsOpenImplementationTypesWithFactory", "public interface IService<T>; public sealed class Service<T> : IService<T> { private Service() { } } public static class ServiceFactory { [IocGenericFactory(typeof(IService<int>), typeof(int))] public static IService<T> Create<T>() => throw new System.NotImplementedException(); }", 0)]
    public async Task SGIOC001_ImplementationWithoutAccessibleConstructor_ReportsWithoutFactoryOrInstance(string scenario, string registration, int expected)
    {
        var defaults = scenario switch
        {
            "DefaultFactory" => "[assembly: IocRegisterDefaults(typeof(IService), ServiceLifetime.Singleton, Factory = nameof(Service.Create))]",
            "DefaultsImplementationTypes" => "[assembly: IocRegisterDefaults(typeof(IService), ServiceLifetime.Singleton, ImplementationTypes = [typeof(Service)])]",
            "DefaultsOpenImplementationTypesWithFactory" => "[assembly: IocRegisterDefaults(typeof(IService<>), ServiceLifetime.Singleton, Factory = nameof(ServiceFactory.Create), ImplementationTypes = [typeof(Service<>)])]",
            "DefaultsImplementationTypesWithFactory" => "[assembly: IocRegisterDefaults(typeof(IService), ServiceLifetime.Singleton, Factory = nameof(Service.Create), ImplementationTypes = [typeof(Service)])]",
            _ => "",
        };
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            {{defaults}}

            namespace TestNamespace;

            {{registration}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(expected);
        if(expected > 0)
        {
            await Assert.That(sgioc001[0].GetMessage()).Contains("'Service'").And.Contains("; specify a Factory or Instance");
        }
    }

    [Test]
    [Arguments("NoInternalsVisibleTo", "", 1)]
    [Arguments("InternalsVisibleTo", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]", 0)]
    public async Task SGIOC001_InternalConstructorInReferencedAssembly_ReportsWhenInaccessible(string scenario, string internalsVisibleTo, int expected)
    {
        var librarySource = $$"""
            {{internalsVisibleTo}}

            namespace Library;

            public sealed class Service
            {
                internal Service() { }
            }
            """;
        var library = SourceGeneratorTestHelper.CreateCompilation("Library", librarySource);

        const string source = """
            using SourceGen.Ioc;
            using Library;

            [assembly: IocRegisterFor<Service>]
            """;
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [library.ToMetadataReference()]);

        var diagnostics = await compilation.WithAnalyzers([new RegisterAnalyzer()]).GetAnalyzerDiagnosticsAsync();

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001")).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("NoInternalsVisibleTo", "", 1)]
    [Arguments("InternalsVisibleTo", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]", 0)]
    public async Task SGIOC001_ImportedOpenRegistrationWithInternalConstructor_ReportsAtImportWhenInaccessible(string scenario, string internalsVisibleTo, int expected)
    {
        var librarySource = $$"""
            using SourceGen.Ioc;

            {{internalsVisibleTo}}

            namespace Library;

            public interface IRepo<T>;

            public sealed class Repo<T> : IRepo<T>
            {
                internal Repo() { }
            }

            [IocRegisterFor(typeof(Repo<>), ServiceTypes = [typeof(IRepo<>)])]
            public sealed class InnerModule;

            [IocImportModule(typeof(InnerModule))]
            public sealed class Module;
            """;
        var library = SourceGeneratorTestHelper.CreateCompilation("Library", librarySource);

        const string source = """
            using SourceGen.Ioc;

            [IocImportModule(typeof(Library.Module))]
            public sealed class App;
            """;
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [library.ToMetadataReference()]);

        var diagnostics = await compilation.WithAnalyzers([new RegisterAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(expected);
        if(expected > 0)
        {
            await Assert.That(sgioc001[0].GetMessage()).Contains("'Repo'").And.Contains("imported from 'Module'");
        }
    }

    [Test]
    public async Task SGIOC001_ImportedModuleInSameCompilation_ReportsOnlyAtRegistration()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            public sealed class Repo<T> : IRepo<T>
            {
                private Repo() { }
            }

            [IocRegisterFor(typeof(Repo<>), ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Module;

            [IocImportModule(typeof(Module))]
            public sealed class App;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).DoesNotContain("imported from");
    }
}