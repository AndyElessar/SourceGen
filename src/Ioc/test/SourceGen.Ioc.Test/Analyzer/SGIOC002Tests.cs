namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC002: Circular dependency detected.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC002)]
public class SGIOC002Tests
{
    [Test]
    public async Task SGIOC002_DirectCircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IocRegister]
            public class ServiceB
            {
                public ServiceB(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SGIOC002_IndirectCircularDependency_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IocRegister]
            public class ServiceB
            {
                public ServiceB(ServiceC c) { }
            }

            [IocRegister]
            public class ServiceC
            {
                public ServiceC(ServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SGIOC002_NoCircularDependency_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public class ServiceA
            {
                public ServiceA(ServiceB b) { }
            }

            [IocRegister]
            public class ServiceB
            {
                public ServiceB(ServiceC c) { }
            }

            [IocRegister]
            public class ServiceC { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

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

            [IocRegister(ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA
            {
                public ServiceA(IServiceB b) { }
            }

            [IocRegister(ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB
            {
                public ServiceB(IServiceA a) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SGIOC002_KeyedServicesDifferentKeys_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyed { }

            [IocRegister<IKeyed>(Key = "Key")]
            public class Keyed : IKeyed { }

            [IocRegister<IKeyed>(Key = "Other")]
            public class KeyedOther([IocInject(Key = "Key")] IKeyed keyed) : IKeyed { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsEqualTo(0);
    }

    [Test]
    [Category(Constants.SGIOC003)]
    public async Task SGIOC002_Combined_CircularDependencyAndLifetimeConflict_ReportsBothDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class ScopedService
            {
                public ScopedService(SingletonService singleton) { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
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
    public async Task SGIOC002_CircularDependencyViaInterface_TupleElementNamesDiffer_ReportsDiagnostic()
    {
        // Tuple element names are not part of the runtime type, so each dependency resolves the other registration
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public class HandlerA(IHandler<(Entity, int)> other) : IHandler<(Entity X, string Y)>;

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity X, int Y)>)])]
            public class HandlerB(IHandler<(Entity M, string N)> other) : IHandler<(Entity X, int Y)>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SGIOC002_InstanceOfCollectionTypes_NoDiagnostic()
    {
        // An Instance registration is never constructed, so the List(IEnumerable<T>) / Dictionary(IDictionary<TKey, TValue>, ...)
        // constructors that resolve to the registrations themselves are not dependencies. The constructor selected among those
        // with the most parameters depends on metadata order: reference assemblies select them, the runtime assemblies that
        // these tests compile against may not, so SGIOC002_InstanceWithSelfReferencingConstructor_NoDiagnostic covers it too.
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler;

            public static class Collections
            {
                public static readonly List<Lazy<IHandler>> Lazies = [];
                public static readonly Dictionary<string, IHandler> Dictionary = [];
            }

            [IocRegisterFor<List<Lazy<IHandler>>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<Lazy<IHandler>>)], Instance = "Collections.Lazies")]
            [IocRegisterFor<Dictionary<string, IHandler>>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IDictionary<string, IHandler>)], Instance = "Collections.Dictionary")]
            [IocContainer(ExplicitOnly = true)]
            public sealed partial class Container;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC002_FactoryWithSelfReferencingConstructor_NoDiagnostic()
    {
        // A Factory registration is created by its factory method, so its constructor parameters are not dependencies
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService;

            [IocRegister(ServiceTypes = [typeof(IService)], Factory = nameof(Create))]
            public class Service : IService
            {
                public Service(IService inner) { }

                public static Service Create() => new(null!);
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC002_InstanceWithSelfReferencingConstructor_NoDiagnostic()
    {
        // Like List<T>, whose List(IEnumerable<T>) constructor resolves to the registration itself
        const string source = """
            using System.Collections;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler;

            public sealed class HandlerCollection(IEnumerable<IHandler> handlers) : IEnumerable<IHandler>
            {
                public static readonly HandlerCollection Empty = new([]);

                public IEnumerator<IHandler> GetEnumerator() => handlers.GetEnumerator();

                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            [IocRegisterFor<HandlerCollection>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IHandler>)], Instance = "HandlerCollection.Empty")]
            [IocContainer(ExplicitOnly = true)]
            public sealed partial class Container;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002");

        await Assert.That(sgioc002).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC002_InstanceFirst_ThenConstructorRegistration_ReportsAtConstructorRegistration()
    {
        // The cycle runs through the constructor of the second registration, so it is reported there, not at the Instance
        const string source = """
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.ServiceA>(Key = "default", Instance = nameof(TestNamespace.ServiceA.Default))]

            namespace TestNamespace;

            [IocRegister]
            public class ServiceA(ServiceB b)
            {
                public static readonly ServiceA Default = null!;
            }

            [IocRegister]
            public class ServiceB(ServiceA a);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc002 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC002").ToList();
        var locations = sgioc002.Select(static d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan)).ToList();

        await Assert.That(locations).Count().IsEqualTo(2);
        await Assert.That(locations).All().Satisfy(static location => location.IsEqualTo("IocRegister"));
    }
}
