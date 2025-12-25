using Microsoft.CodeAnalysis;
using SourceGen.Ioc.Test.Helpers;

namespace SourceGen.Ioc.Test.Register;

/// <summary>
/// Unit tests for RegisterAnalyzer.
/// </summary>
public class RegisterAnalyzerTests
{
    #region SGIOC001 - Invalid Attribute Usage

    [Test]
    public async Task SGIOC001_PrivateClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IoCRegister]
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

            [IoCRegister]
            public abstract class AbstractService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_IoCRegisterForAttribute_PrivateTargetType_ReportsDiagnostic()
    {
        // Note: When using IoCRegisterForAttribute with typeof(), the type must be accessible
        // So we test with a nested private class where the attribute is on an outer accessible type
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IoCRegisterFor(typeof(PrivateService))]
                private class PrivateService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_IoCRegisterForAttribute_AbstractTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class AbstractService { }

            [IoCRegisterFor(typeof(AbstractService))]
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

            [IoCRegister]
            public class ValidService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC001_InternalConcreteClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            internal class InternalService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    #endregion

    #region SGIOC002 - Circular Dependency

    [Test]
    public async Task SGIOC002_DirectCircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IoCRegister]
            public class ServiceB
            {
                public ServiceB(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SGIOC002_IndirectCircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IoCRegister]
            public class ServiceB
            {
                public ServiceB(ServiceC c) { }
            }

            [IoCRegister]
            public class ServiceC
            {
                public ServiceC(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SGIOC002_NoCircularDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IoCRegister]
            public class ServiceB
            {
                public ServiceB(ServiceC c) { }
            }

            [IoCRegister]
            public class ServiceC { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC002_CircularDependencyViaInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }

            [IoCRegister(ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            [IoCRegister(ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    #endregion

    #region SGIOC003 - Service Lifetime Conflict

    [Test]
    public async Task SGIOC003_SingletonDependsOnScoped_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(SingletonDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC003_ScopedDependsOnTransient_NoErrorDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // SGIOC003 is for Error level (Singleton -> Scoped), not Warning level (Scoped -> Transient)
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC003_ScopedDependsOnSingleton_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(SingletonService singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

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

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(IScopedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    #endregion

    #region SGIOC004 - Nested Open Generic Detected

    [Test]
    public async Task SGIOC004_NestedOpenGenericInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
        await Assert.That(sgioc004[0].GetMessage()).Contains("NestedGenericHandler").And.Contains("IHandler<Wrapper<T>>");
    }

    [Test]
    public async Task SGIOC004_DeeplyNestedOpenGenericInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IProcessor<T> { }
            public class Outer<T> { }
            public class Inner<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class DeeplyNestedHandler<T> : IProcessor<Outer<Inner<T>>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC004_NestedOpenGenericBaseClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class BaseHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllBaseClasses = true)]
            public class NestedGenericHandler<T> : BaseHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC004_SimpleOpenGeneric_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IoCRegister]
            public class SimpleHandler<T> : IHandler<T> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC004_ClosedGeneric_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister]
            public class ClosedGenericHandler : IHandler<Wrapper<string>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC004_NonGenericClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class SimpleService : IService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC004_MultipleNestedOpenGenerics_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public interface IProcessor<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class MultiNestedHandler<T> : IHandler<Wrapper<T>>, IProcessor<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC004_NestedOpenGenericWithSelfRegistrationOnly_NoDiagnostic()
    {
        // When an open generic only registers itself (no ServiceTypes, no RegisterAllInterfaces, no RegisterAllBaseClasses),
        // it should not report SGIOC004 because the nested open generic interface won't be registered.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC004_NestedOpenGenericWithServiceTypes_ReportsDiagnostic()
    {
        // When ServiceTypes is specified, it will try to register the interface, so SGIOC004 should be reported.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(ServiceTypes = [typeof(IHandler<>)])]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC004_NestedOpenGenericWithRegisterAllInterfaces_ReportsDiagnostic()
    {
        // When RegisterAllInterfaces is true, SGIOC004 should be reported for nested open generics.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC004_NestedOpenGenericWithRegisterAllBaseClasses_ReportsDiagnostic()
    {
        // When RegisterAllBaseClasses is true, SGIOC004 should be reported for nested open generics in base classes.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class BaseHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllBaseClasses = true)]
            public class NestedGenericHandler<T> : BaseHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC004_NestedOpenGenericBaseClassWithSelfRegistrationOnly_NoDiagnostic()
    {
        // When only registering self (no RegisterAllBaseClasses), nested open generic base class should not cause SGIOC004.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class BaseHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister]
            public class NestedGenericHandler<T> : BaseHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC004_IoCRegisterForAttribute_NestedOpenGeneric_ReportsDiagnostic()
    {
        // IoCRegisterForAttribute always registers for service types, so SGIOC004 should be reported.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegisterFor(typeof(NestedGenericHandler<>), RegisterAllInterfaces = true)]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
    }

    #endregion

    #region SGIOC101 - Service Lifetime Conflict Warning

    [Test]
    public async Task SGIOC101_ScopedDependsOnTransient_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc101 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC101").ToList();

        await Assert.That(sgioc101).Count().IsEqualTo(1);
        await Assert.That(sgioc101[0].GetMessage()).Contains("Scoped").And.Contains("Transient");
        await Assert.That(sgioc101[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task SGIOC101_ScopedDependsOnTransientViaInterface_ReportsWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ITransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
            public class TransientService : ITransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(ITransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc101 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC101").ToList();

        await Assert.That(sgioc101).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC003_SingletonDependsOnTransient_ReportsError()
    {
        // Singleton depending on Transient is a captive dependency issue - Error
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(TransientService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("Singleton").And.Contains("Transient");
        await Assert.That(sgioc003[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
    }

    [Test]
    public async Task SGIOC101_TransientDependsOnTransient_NoWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient)]
            public class TransientService
            {
                public TransientService(TransientDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc101 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC101").ToList();

        await Assert.That(sgioc101).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC101_ScopedDependsOnScoped_NoWarning()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(ScopedDependency dep) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc101 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC101").ToList();

        await Assert.That(sgioc101).Count().IsEqualTo(0);
    }

    #endregion

    #region Combined Scenarios

    [Test]
    public async Task Combined_CircularDependencyAndLifetimeConflict_ReportsBothDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(SingletonService singleton) { }
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(ScopedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // Should report circular dependency
        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
        // Should report lifetime conflict (Singleton depending on Scoped)
        await Assert.That(sgioc003).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task NoIoCRegisterAttribute_NoDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            namespace TestNamespace;

            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            public class ServiceB
            {
                public ServiceB(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics).Count().IsEqualTo(0);
    }

    #endregion

    #region IoCRegisterDefaultSettingsAttribute - Default Lifetime

    [Test]
    public async Task DefaultSettings_OpenGenericInterface_AppliesLifetimeFromDefaultSettings()
    {
        // TestOpenGeneric2<T> implements IGenericTest2<T>
        // IoCRegisterDefaultSettings specifies Scoped for IGenericTest2<>
        // So TestOpenGeneric2<T> should be treated as Scoped (from default settings), not Singleton
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // This service should be Scoped (from default settings), not Singleton
            [IoCRegister]
            public class TestOpenGeneric2<T> : IGenericTest2<T> { }

            // Singleton service depending on Scoped service should report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
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
    public async Task DefaultSettings_OpenGenericInterface_NoConflictWhenLifetimesMatch()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // This service should be Scoped (from default settings)
            [IoCRegister]
            public class TestOpenGeneric2<T> : IGenericTest2<T> { }

            // Scoped service depending on Scoped service should NOT report error
            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            public class AnotherScopedService
            {
                public AnotherScopedService(TestOpenGeneric2<int> scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // No lifetime conflict when both are Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task DefaultSettings_ExplicitLifetime_OverridesDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // Explicit Singleton should override default Scoped
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class TestOpenGeneric2<T> : IGenericTest2<T> { }

            // Singleton service depending on Singleton service should NOT report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(TestOpenGeneric2<int> singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // No lifetime conflict when both are Singleton
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    [Test]
    public async Task DefaultSettings_ClosedGenericImplementation_AppliesLifetimeFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IGenericTest2<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericTest2<T> { }

            // Closed generic implementing IGenericTest2<int> should also get Scoped lifetime
            [IoCRegister]
            public class ClosedGenericService : IGenericTest2<int> { }

            // Singleton service depending on Scoped service should report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(ClosedGenericService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // Should report lifetime conflict: Singleton depends on Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DefaultSettings_NonGenericInterface_AppliesLifetimeFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IMyService), ServiceLifetime.Transient)]

            namespace TestNamespace;

            public interface IMyService { }

            // Should get Transient lifetime from default settings
            [IoCRegister]
            public class MyService : IMyService { }

            // Singleton service depending on Transient service should report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(MyService transient) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // Should report lifetime conflict: Singleton depends on Transient
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DefaultSettings_BaseClass_AppliesLifetimeFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(BaseService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public abstract class BaseService { }

            // Should get Scoped lifetime from base class default settings
            [IoCRegister]
            public class DerivedService : BaseService { }

            // Singleton service depending on Scoped service should report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(DerivedService scoped) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // Should report lifetime conflict: Singleton depends on Scoped
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DefaultSettings_MultipleDefaultSettings_FirstMatchWins()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IFirst), ServiceLifetime.Scoped)]
            [assembly: IoCRegisterDefaultSettings(typeof(ISecond), ServiceLifetime.Transient)]

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            // Implements both interfaces, should get Scoped from IFirst (first in AllInterfaces)
            [IoCRegister]
            public class MultiInterfaceService : IFirst, ISecond { }

            // Singleton service depending on Scoped service should report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(MultiInterfaceService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // Should report lifetime conflict based on first matching default settings
        await Assert.That(sgioc003).Count().IsEqualTo(1);
    }

    [Test]
    public async Task DefaultSettings_NoMatchingDefaultSettings_UsesSingletonDefault()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IoCRegisterDefaultSettings(typeof(IOtherInterface), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IOtherInterface { }
            public interface IMyInterface { }

            // No matching default settings, should use Singleton default
            [IoCRegister]
            public class MyService : IMyInterface { }

            // Singleton service depending on Singleton service should NOT report error
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class SingletonService
            {
                public SingletonService(MyService singleton) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        // No lifetime conflict when both are Singleton
        await Assert.That(sgioc003).Count().IsEqualTo(0);
    }

    #endregion
}
