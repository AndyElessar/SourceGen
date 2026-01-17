namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for analyzer support of generic attribute variants (IoCRegisterAttribute&lt;T&gt;, IocRegisterForAttribute&lt;T&gt;, etc.).
/// These tests verify that diagnostics are correctly reported for generic attribute variants.
/// </summary>
[Category(Constants.Analyzer)]
public class GenericAttributeAnalyzerTests
{
    #region SGIOC001 - Invalid Attribute Usage (Generic Variants)

    [Test]
    public async Task SGIOC001_IoCRegisterAttribute_T1_PrivateClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class OuterClass
            {
                [IocRegister<IMyService>(ServiceLifetime.Singleton)]
                private class PrivateService : IMyService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_IoCRegisterAttribute_T2_AbstractClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            [IocRegister<IFirst, ISecond>(ServiceLifetime.Singleton)]
            public abstract class AbstractService : IFirst, ISecond { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_T1_PrivateTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                [IocRegisterFor<PrivateService>(ServiceLifetime.Singleton)]
                private class PrivateService { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("PrivateService").And.Contains("private");
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_T1_AbstractTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class AbstractService { }

            [IocRegisterFor<AbstractService>(ServiceLifetime.Singleton)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_IoCRegisterAttribute_T1_ValidClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class ValidService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC001_IocRegisterForAttribute_T1_ValidClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class ExternalService { }

            [IocRegisterFor<ExternalService>(ServiceLifetime.Singleton)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    #endregion

    #region SGIOC002 - Circular Dependency (Generic Variants)

    [Test]
    public async Task SGIOC002_IoCRegisterAttribute_T1_CircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }

            [IocRegister<IServiceA>(ServiceLifetime.Singleton)]
            public class ServiceA(ServiceB b) : IServiceA { }

            [IocRegister<IServiceB>(ServiceLifetime.Singleton)]
            public class ServiceB(ServiceA a) : IServiceB { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    #endregion

    #region SGIOC003-005 - Lifetime Conflicts (Generic Variants)

    [Test]
    public async Task SGIOC003_IoCRegisterAttribute_T1_SingletonDependsOnScoped_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ISingleton { }
            public interface IScoped { }

            [IocRegister<ISingleton>(ServiceLifetime.Singleton)]
            public class SingletonService(ScopedService scoped) : ISingleton { }

            [IocRegister<IScoped>(ServiceLifetime.Scoped)]
            public class ScopedService : IScoped { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc003 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC003").ToList();

        await Assert.That(sgioc003).Count().IsEqualTo(1);
        await Assert.That(sgioc003[0].GetMessage()).Contains("SingletonService").And.Contains("ScopedService");
    }

    [Test]
    public async Task SGIOC004_IoCRegisterAttribute_T2_SingletonDependsOnTransient_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ISingleton { }
            public interface ITransient { }

            [IocRegister<ISingleton>(ServiceLifetime.Singleton)]
            public class SingletonService(TransientService transient) : ISingleton { }

            [IocRegister<ITransient>(ServiceLifetime.Transient)]
            public class TransientService : ITransient { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc004 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC004").ToList();

        await Assert.That(sgioc004).Count().IsEqualTo(1);
        await Assert.That(sgioc004[0].GetMessage()).Contains("SingletonService").And.Contains("TransientService");
    }

    [Test]
    public async Task SGIOC005_IocRegisterForAttribute_T1_ScopedDependsOnTransient_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScoped { }
            public interface ITransient { }

            public class ScopedService(TransientService transient) : IScoped { }

            [IocRegisterFor<ScopedService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IScoped)])]
            public sealed class Module;

            [IocRegister<ITransient>(ServiceLifetime.Transient)]
            public class TransientService : ITransient { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc005 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC005").ToList();

        await Assert.That(sgioc005).Count().IsEqualTo(1);
        await Assert.That(sgioc005[0].GetMessage()).Contains("ScopedService").And.Contains("TransientService");
    }

    #endregion

    #region SGIOC008 - Factory/Instance nameof() validation (Generic Variants)

    [Test]
    public async Task SGIOC008_IoCRegisterAttribute_T1_Factory_NonStaticMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Factory = nameof(MyServiceFactory.Create))]
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
    public async Task SGIOC008_IocRegisterForAttribute_T1_Factory_PrivateMethod_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class ExternalService : IMyService
            {
                private static IMyService Create(IServiceProvider sp) => new ExternalService();
            }

            [IocRegisterFor<ExternalService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Factory = nameof(ExternalService.Create))]
            public sealed class Module;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Factory").And.Contains("Create").And.Contains("private");
    }

    [Test]
    public async Task SGIOC008_IoCRegisterAttribute_T1_Instance_NonStaticProperty_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Instance = nameof(ServiceProvider.Instance))]
            public class MyService : IMyService { }

            public class ServiceProvider
            {
                public IMyService Instance { get; } = new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC008").ToList();

        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("Instance").And.Contains("Instance").And.Contains("not static");
    }

    [Test]
    public async Task SGIOC008_IoCRegisterAttribute_T1_ValidStaticFactory_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Factory = nameof(MyServiceFactory.Create))]
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

    #endregion

    #region SGIOC009 - Instance requires Singleton lifetime (Generic Variants)

    [Test]
    public async Task SGIOC009_IoCRegisterAttribute_T1_InstanceWithScopedLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public static class ServiceInstances
            {
                public static readonly IMyService MyInstance = new MyService();
            }

            [IocRegister<IMyService>(ServiceLifetime.Scoped, Instance = nameof(ServiceInstances.MyInstance))]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("MyInstance").And.Contains("Scoped");
    }

    [Test]
    public async Task SGIOC009_IocRegisterForAttribute_T1_InstanceWithTransientLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class ExternalService : IMyService { }

            public static class ServiceInstances
            {
                public static readonly ExternalService MyInstance = new();
            }

            [IocRegisterFor<ExternalService>(ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)], Instance = nameof(ServiceInstances.MyInstance))]
            public sealed class Module;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("MyInstance").And.Contains("Transient");
    }

    [Test]
    public async Task SGIOC009_IoCRegisterAttribute_T1_InstanceWithSingletonLifetime_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public static class ServiceInstances
            {
                public static readonly IMyService MyInstance = new MyService();
            }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Instance = nameof(ServiceInstances.MyInstance))]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(0);
    }

    #endregion

    #region SGIOC010 - Factory and Instance conflict (Generic Variants)

    [Test]
    public async Task SGIOC010_IoCRegisterAttribute_T1_BothFactoryAndInstance_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public static class ServiceProvider
            {
                public static readonly IMyService Instance = new MyService();
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }

            [IocRegister<IMyService>(
                ServiceLifetime.Singleton,
                Factory = nameof(ServiceProvider.Create),
                Instance = nameof(ServiceProvider.Instance))]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(1);
        await Assert.That(sgioc010[0].GetMessage()).Contains("Factory").And.Contains("Instance");
    }

    [Test]
    public async Task SGIOC010_IocRegisterForAttribute_T1_BothFactoryAndInstance_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class ExternalService : IMyService { }

            public static class ServiceProvider
            {
                public static readonly ExternalService Instance = new();
                public static ExternalService Create(IServiceProvider sp) => new();
            }

            [IocRegisterFor<ExternalService>(
                ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(ServiceProvider.Create),
                Instance = nameof(ServiceProvider.Instance))]
            public sealed class Module;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc010 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC010").ToList();

        await Assert.That(sgioc010).Count().IsEqualTo(1);
        await Assert.That(sgioc010[0].GetMessage()).Contains("Factory").And.Contains("Instance");
    }

    #endregion

    #region Assembly-level IocRegisterForAttribute<T>

    [Test]
    public async Task SGIOC001_AssemblyLevel_IocRegisterForAttribute_T1_AbstractType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.AbstractService>(ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public abstract class AbstractService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(1);
        await Assert.That(sgioc001[0].GetMessage()).Contains("AbstractService").And.Contains("abstract");
    }

    [Test]
    public async Task SGIOC001_AssemblyLevel_IocRegisterForAttribute_T1_ValidType_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.ExternalService>(ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public class ExternalService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        await Assert.That(sgioc001).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC009_AssemblyLevel_IocRegisterForAttribute_T1_InstanceWithScopedLifetime_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.ExternalService>(ServiceLifetime.Scoped, Instance = nameof(TestNamespace.ServiceInstances.MyInstance))]

            namespace TestNamespace;

            public class ExternalService { }

            public static class ServiceInstances
            {
                public static readonly ExternalService MyInstance = new();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc009 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC009").ToList();

        await Assert.That(sgioc009).Count().IsEqualTo(1);
        await Assert.That(sgioc009[0].GetMessage()).Contains("MyInstance").And.Contains("Scoped");
    }

    #endregion

    #region Mixed Generic and Non-Generic Attributes

    [Test]
    public async Task MixedAttributes_GenericAndNonGeneric_BothAnalyzed()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService1 { }
            public interface IService2 { }

            // Non-generic attribute on private class
            public class Outer1
            {
                [IocRegister]
                private class PrivateService1 { }
            }

            // Generic attribute on private class
            public class Outer2
            {
                [IocRegister<IService1>(ServiceLifetime.Singleton)]
                private class PrivateService2 : IService1 { }
            }

            // Non-generic IoCRegisterFor on abstract class
            public abstract class AbstractService1 { }
            [IocRegisterFor(typeof(AbstractService1))]
            public interface IMarker1 { }

            // Generic IoCRegisterFor on abstract class
            public abstract class AbstractService2 { }
            [IocRegisterFor<AbstractService2>(ServiceLifetime.Singleton)]
            public interface IMarker2 { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc001 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC001").ToList();

        // Should report 4 diagnostics: 2 private classes, 2 abstract classes
        await Assert.That(sgioc001).Count().IsEqualTo(4);
    }

    #endregion
}
