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
}
