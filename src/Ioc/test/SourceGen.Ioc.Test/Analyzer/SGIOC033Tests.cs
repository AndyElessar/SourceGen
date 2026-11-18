namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC033: Func with arguments ignores the service lifetime.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC033)]
public class SGIOC033Tests
{
    private static string CreateSource(string lifetime, string dependency) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface IService { }

        [IocRegister(Lifetime = ServiceLifetime.{{lifetime}}, ServiceTypes = [typeof(IService)])]
        public class Service(string name) : IService { }

        [IocRegister(Lifetime = ServiceLifetime.Transient)]
        public class Consumer({{dependency}} dependency) { }
        """;

    [Test]
    [Arguments("Singleton")]
    [Arguments("Scoped")]
    public async Task SGIOC033_FuncWithArguments_OfSingletonOrScopedService_ReportsDiagnostic(string lifetime)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(lifetime, "Func<string, IService>"));
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033).ToList();

        await Assert.That(sgioc033).Count().IsEqualTo(1);
        await Assert.That(sgioc033[0].Severity).IsEqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        await Assert.That(sgioc033[0].GetMessage())
            .Contains("'Consumer' depends on 'Func<string, IService>'")
            .And.Contains($"registered as {lifetime}");
    }

    [Test]
    public async Task SGIOC033_FuncWithArguments_OfTransientService_NoDiagnostic()
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource("Transient", "Func<string, IService>"));
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033).ToList();

        await Assert.That(sgioc033).IsEmpty();
    }

    [Test]
    [Arguments("Func<IService>")]
    [Arguments("Lazy<IService>")]
    [Arguments("IService")]
    public async Task SGIOC033_DependencyFollowingLifetime_OfSingletonService_NoDiagnostic(string dependency)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource("Singleton", dependency));
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033).ToList();

        await Assert.That(sgioc033).IsEmpty();
    }

    [Test]
    public async Task SGIOC033_FuncWithArguments_OfUnregisteredService_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnregistered { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Func<string, IUnregistered> factory) { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033).ToList();

        await Assert.That(sgioc033).IsEmpty();
    }

    [Test]
    public async Task SGIOC033_InjectedMembers_WithFuncWithArguments_OfSingletonService_ReportsDiagnosticPerMember()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class Service(string name, int count) : IService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer
            {
                [IocInject]
                public Func<string, int, IService> Factory { get; set; } = default!;

                [IocInject]
                public void Initialize(Func<string, int, IService> factory) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033).ToList();

        await Assert.That(sgioc033).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC033_FuncWithArguments_TupleElementNamesDiffer_ReportsDiagnostic()
    {
        // Tuple element names are not part of the runtime type, so the Func creates the registered handler
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public sealed class NamedHandler(int id) : IHandler<(Entity X, string Y)>;

            [IocRegister]
            public sealed class Consumer(Func<int, IHandler<(Entity M, string N)>> create);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033).ToList();

        await Assert.That(sgioc033).Count().IsEqualTo(1);
        await Assert.That(sgioc033[0].GetMessage()).Contains("registered as Singleton");
    }

    [Test]
    [Arguments("Instance = nameof(Default)")]
    [Arguments("Factory = nameof(Create)")]
    public async Task SGIOC033_FuncWithArguments_InConstructorOfInstanceOrFactory_NoDiagnostic(string activation)
    {
        // Instance and Factory registrations are not constructed by the container, so their constructor parameters are not resolved
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class Service(string name) : IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, {{activation}})]
            public class Consumer(Func<string, IService> dependency)
            {
                public static readonly Consumer Default = new(static name => new Service(name));

                public static Consumer Create() => Default;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC033);

        await Assert.That(sgioc033).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC033_InstanceFirst_ThenConstructorRegistration_ReportsAtConstructorRegistration()
    {
        // The Func is a constructor parameter, so it is reported at the registration that is constructed
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class Service(string name) : IService { }

            [IocRegister(Key = "default", Instance = nameof(Default))]
            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Func<string, IService> dependency)
            {
                public static readonly Consumer Default = new(static name => new Service(name));
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc033 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC033").ToList();

        await Assert.That(sgioc033).Count().IsEqualTo(1);
        await Assert.That(sgioc033[0].Location.SourceTree!.GetText().ToString(sgioc033[0].Location.SourceSpan))
            .IsEqualTo("IocRegister(Lifetime = ServiceLifetime.Transient)");
    }
}
