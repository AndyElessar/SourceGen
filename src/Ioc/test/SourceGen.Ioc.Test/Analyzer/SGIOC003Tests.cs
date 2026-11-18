using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC003: Singleton depends on Scoped service.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC003)]
public class SGIOC003Tests
{
    [Test]
    public async Task SGIOC003_SingletonDependsOnScoped_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(ScopedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("Singleton").And.Contains("Scoped");
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnSingleton_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(SingletonDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC003_ScopedDependsOnTransient_ShouldReportSGIOC005NotSGIOC003()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005");

        // SGIOC003 is for Singleton -> Scoped, SGIOC005 is for Scoped -> Transient
        await Assert.That(sgioc003).Count().IsEqualTo(0);
        await Assert.That(sgioc005).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_ScopedDependsOnSingleton_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(SingletonService singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnScopedViaInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_OpenGenericInterface_AppliesLifetimeFromDefaultSettings()
    {
        // TestOpenGeneric2<T> implements IGenericTest2<T>
        // IocRegisterDefaults specifies Scoped for IGenericTest2<>
        // So TestOpenGeneric2<T> should be treated as Scoped (from default settings), not Singleton
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // This service should be Scoped (from default settings), not Singleton
            [IocRegister]
            public class TestOpenGeneric2<T> : IGenericTest2<T> { }

            // Singleton service depending on Scoped service should report error
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(TestOpenGeneric2<int> scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // Should report lifetime conflict: Singleton depends on Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("Singleton").And.Contains("Scoped");
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_OpenGenericInterface_NoConflictWhenLifetimesMatch()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // This service should be Scoped (from default settings)
            [IocRegister]
            public class TestOpenGeneric2<T> : IGenericTest2<T> { }

            // Scoped service depending on Scoped service should NOT report error
            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class AnotherScopedService
            {
                public AnotherScopedService(TestOpenGeneric2<int> scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        // No lifetime conflict when both are Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_ExplicitLifetime_OverridesDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // Explicit Singleton should override default Scoped
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class TestOpenGeneric2<T> : IGenericTest2<T> { }

            // Singleton service depending on Singleton service should NOT report error
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(TestOpenGeneric2<int> singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        // No lifetime conflict when both are Singleton
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_ClosedGenericImplementation_AppliesLifetimeFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // Closed generic implementing IGenericTest2<int> should also get Scoped lifetime
            [IocRegister]
            public class ClosedGenericService : IGenericTest2<int> { }

            // Singleton service depending on Scoped service should report error
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(ClosedGenericService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        // Should report lifetime conflict: Singleton depends on Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_BaseClass_AppliesLifetimeFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(BaseService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public abstract class BaseService { }

            // Should get Scoped lifetime from base class default settings
            [IocRegister]
            public class DerivedService : BaseService { }

            // Singleton service depending on Scoped service should report error
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(DerivedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        // Should report lifetime conflict: Singleton depends on Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_MultipleDefaultSettings_FirstMatchWins()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IFirst), ServiceLifetime.Scoped)]
            [assembly: IocRegisterDefaults(typeof(ISecond), ServiceLifetime.Transient)]

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            // Implements both interfaces, should get Scoped from IFirst (first in AllInterfaces)
            [IocRegister]
            public class MultiInterfaceService : IFirst, ISecond { }

            // Singleton service depending on Scoped service should report error
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(MultiInterfaceService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        // Should report lifetime conflict based on first matching default settings
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettings_NoMatchingDefaultSettings_UsesSingletonDefault()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IOtherInterface), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IOtherInterface { }
            public interface IMyInterface { }

            // No matching default settings, should use Singleton default
            [IocRegister]
            public class MyService : IMyInterface { }

            // Singleton service depending on Singleton service should NOT report error
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(MyService singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        // No lifetime conflict when both are Singleton
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnScopedViaFunc_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(Func<IScopedService> scopedFactory) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnScopedViaLazy_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(Lazy<IScopedService> scopedLazy) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnScopedViaMultiParamFunc_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService
            {
                public ScopedService(string name, int id) { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(Func<string, int, IScopedService> scopedFactory) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnSingletonViaFunc_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ISingletonService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISingletonService)])]
            public class SingletonDependency : ISingletonService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(Func<ISingletonService> factory) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("IHandler<(Entity M, string N)>")]
    [Arguments("Func<IHandler<(Entity M, string N)>>")]
    [Arguments("Lazy<IHandler<(Entity, string)>>")]
    public async Task SGIOC003_SingletonDependsOnScoped_TupleElementNamesDiffer_ReportsDiagnostic(string dependency)
    {
        // Tuple element names are not part of the runtime type, so the dependency resolves the Scoped registration
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public class ScopedHandler : IHandler<(Entity X, string Y)>;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService({{dependency}} dependency);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("SingletonService").And.Contains("ScopedHandler");
    }

    [Test]
    [Category(Constants.Defaults)]
    [Arguments("interface", "[IocRegisterDefaults(typeof(IScopedService), ServiceLifetime.Scoped)]\npublic interface IScopedService;")]
    [Arguments("class", "public interface IScopedService;\n[IocRegisterDefaults<IScopedService>(ServiceLifetime.Scoped)]\npublic sealed class Holder;")]
    [Arguments("struct", "public interface IScopedService;\n[IocRegisterDefaults(typeof(IScopedService), ServiceLifetime.Scoped)]\npublic struct Holder;")]
    public async Task SGIOC003_TypeLevelDefaultSettings_AppliesLifetime(string scenario, string declarations)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            {{declarations}}

            [IocRegister]
            public class ScopedService : IScopedService;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService(IScopedService scoped);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC003)).Count().IsEqualTo(1);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC004)).IsEmpty();
    }

    [Test]
    [Category(Constants.Defaults)]
    [Category(Constants.ImportModule)]
    [Arguments("ModuleType", "", "[IocRegisterDefaults(typeof(IScopedService), ServiceLifetime.Scoped)]")]
    [Arguments("ModuleAssembly", "[assembly: IocRegisterDefaults(typeof(Library.IScopedService), ServiceLifetime.Scoped)]", "")]
    public async Task SGIOC003_ImportedModuleDefaultSettings_AppliesLifetime(string scenario, string assemblyDefaults, string moduleDefaults)
    {
        // Like the generator, the default settings of an imported module (its type, then its assembly) apply to registrations
        var librarySource = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{assemblyDefaults}}

            namespace Library;

            public interface IScopedService;

            {{moduleDefaults}}
            public sealed class Module;
            """;
        var library = SourceGeneratorTestHelper.CreateCompilation("Library", librarySource);

        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using Library;

            namespace TestNamespace;

            [IocImportModule(typeof(Module))]
            public sealed class App;

            [IocRegister]
            public class ScopedService : IScopedService;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService(IScopedService scoped);
            """;
        var compilation = SourceGeneratorTestHelper.CreateCompilation("TestAssembly", source, [library.ToMetadataReference()]);

        var diagnostics = await SourceGeneratorTestHelper.GetCheckedAnalyzerDiagnosticsAsync(compilation.WithAnalyzers([new RegisterAnalyzer()]));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC003)).Count().IsEqualTo(1);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC004)).IsEmpty();
    }

    [Test]
    [Arguments("Scoped", "", Constants.SGIOC003)]
    [Arguments(" scoped ", "", Constants.SGIOC003)]
    [Arguments("Singleton", "", null)]
    [Arguments("Invalid", "", Constants.SGIOC004)]
    [Arguments("Scoped", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IService), ServiceLifetime.Transient)]", Constants.SGIOC004)]
    public async Task SGIOC003_MsBuildDefaultLifetime_AppliesWithoutLifetimeOrDefaultSettings(string msBuildLifetime, string defaults, string? expectedId)
    {
        // Like the generator, SourceGenIocDefaultLifetime is the lifetime of a registration that neither sets one nor matches
        // default settings; an unrecognized value is ignored
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{defaults}}

            namespace TestNamespace;

            public interface IService;

            [IocRegister(ServiceTypes = [typeof(IService)])]
            public class Service : IService;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService(IService service);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocDefaultLifetime"] = msBuildLifetime });
        var lifetimeDiagnostics = diagnostics.Where(d => d.Id is Constants.SGIOC003 or Constants.SGIOC004 or Constants.SGIOC005).ToList();

        await Assert.That(lifetimeDiagnostics).Count().IsEqualTo(expectedId is null ? 0 : 1);
        if(expectedId is not null)
        {
            await Assert.That(lifetimeDiagnostics[0].Id).IsEqualTo(expectedId);
        }
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC003_DefaultSettingsOfBaseClassAndInterface_BaseClassLifetimeWins()
    {
        // Like the generator, base classes are matched before interfaces, so the base class default is the best default
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IService), ServiceLifetime.Transient)]
            [assembly: IocRegisterDefaults(typeof(ServiceBase), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IService;
            public abstract class ServiceBase;

            [IocRegister]
            public class Service : ServiceBase, IService;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService(Service service);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC003)).Count().IsEqualTo(1);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC004)).IsEmpty();
    }

    [Test]
    public async Task SGIOC003_TransientRegistrationFirst_ThenSingletonRegistrationOfSameType_ReportsAtSingletonRegistration()
    {
        // Each registration of an implementation type has its own lifetime: only the Singleton one captures the Scoped service
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.Foo>(ServiceLifetime.Transient, Key = "a")]

            namespace TestNamespace;

            public interface IScopedDep { }

            [IocRegister<IScopedDep>(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedDep : IScopedDep { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Foo(IScopedDep dep) { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].Location.SourceTree!.GetText().ToString(sgioc003[0].Location.SourceSpan))
            .IsEqualTo("IocRegister(Lifetime = ServiceLifetime.Singleton)");
    }

    [Test]
    public async Task SGIOC003_TwoSingletonRegistrationsOfSameType_ReportsAtEachRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.SingletonService>(ServiceLifetime.Singleton, Key = "a")]

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "b")]
            public class SingletonService(ScopedService scoped) { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();
        var locations = sgioc003.Select(static d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)).ToList();

        await Assert.That(locations).Count().IsEqualTo(2);
        await Assert.That(locations).Contains("IocRegisterFor<TestNamespace.SingletonService>(ServiceLifetime.Singleton, Key = \"a\")");
        await Assert.That(locations).Contains("IocRegister(Lifetime = ServiceLifetime.Singleton, Key = \"b\")");
    }

    [Test]
    public async Task SGIOC003_KeyedRegistrationFirst_ThenNonKeyedRegistrationOfInterface_ReportsDiagnostic()
    {
        // IFoo resolves to the non-keyed registration, although the first registration of Foo is keyed
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.Foo>(ServiceLifetime.Scoped, Key = "a", ServiceTypes = [typeof(TestNamespace.IFoo)])]

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister<IFoo>(Lifetime = ServiceLifetime.Scoped)]
            public class Foo : IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Bar(IFoo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("Bar").And.Contains("Foo");
    }

    [Test]
    public async Task SGIOC003_DependencyOnInterfaceNotRegistered_NoDiagnostic()
    {
        // Like the generator, Foo is registered as itself only, so IFoo does not resolve to it
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class Foo : IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Bar(IFoo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC003_KeyedDependency_UsesKeyedRegistration()
    {
        // [FromKeyedServices("a")] resolves the keyed Scoped registration, not the non-keyed Singleton one
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister<IFoo>(Lifetime = ServiceLifetime.Singleton)]
            public class Foo : IFoo;

            [IocRegisterFor<Foo>(ServiceLifetime.Scoped, Key = "a", ServiceTypes = [typeof(IFoo)])]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Bar([FromKeyedServices("a")] IFoo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_LastRegistrationInGeneratorOrder_IsTheDependency()
    {
        // The generator adds [IocRegister], then [IocRegisterFor], then [IocRegisterFor<T>] registrations, so the Scoped
        // [IocRegisterFor<T>] registration wins, although it is neither the first nor the last registration in source order
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Foo;

            [IocRegisterFor<Foo>(ServiceLifetime.Scoped)]
            public static class GenericRegistrations;

            [IocRegisterFor(typeof(Foo), ServiceLifetime.Singleton)]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Bar(Foo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_WinnerOfTagGroupConflicts_ReportsDiagnostic()
    {
        // With the tag "x", the Scoped registration is the last one of Foo
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Foo;

            [IocRegisterFor<Foo>(ServiceLifetime.Scoped, Tags = ["x"])]
            [IocRegisterFor<Foo>(ServiceLifetime.Singleton, Tags = ["y"])]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Bar(Foo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_SameWinnerOfSeveralTagGroups_ReportsOnce()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Scoped, Tags = ["x", "y"])]
            public class Foo;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Bar(Foo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_TagGroupsOfRegisterOutput_LastRegistrationOfLastGroupWins()
    {
        // The Register output adds the registrations grouped by their tags (["a"], then ["a", "b"], then ["b"]), so with the
        // tag "a" IFoo resolves to the Scoped X, although Z is the last registration with the tag "a" in source order
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IFoo)], Tags = ["a", "b"])]
            public class X : IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)], Tags = ["b"])]
            public class Y : IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)], Tags = ["a"])]
            public class Z : IFoo;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Tags = ["a"])]
            public class C(IFoo foo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_UnboundImplementationWithRegisterAllInterfaces_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.Repo<>), ServiceLifetime.Scoped, RegisterAllInterfaces = true)]

            namespace TestNamespace;

            public interface IRepo<T>;

            public class Repo<T> : IRepo<T>;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(IRepo<int> repo);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003");

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }
}
