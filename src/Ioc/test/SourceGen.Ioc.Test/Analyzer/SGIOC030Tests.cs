namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC030: Synchronous dependency requested for async-init-only service.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC030)]
public class SGIOC030Tests
{
    [Test]
    public async Task SGIOC030_ConstructorRequestsSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer(IMyService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_ConstructorRequestsTaskTypeForAsyncInitService_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer(Task<IMyService> service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC030_ConstructorServiceTypes_FallbackRecognized_NoDiagnosticWhenSyncRegistrationExists()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class AsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceLifetime.Singleton, typeof(IMyService))]
            public class SyncService : IMyService { }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer(IMyService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC030_ServiceTypesNamedArgument_PrecedesConstructorParams()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConstructorService { }
            public interface INamedService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IConstructorService)])]
            public class AsyncService : IConstructorService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceLifetime.Singleton, typeof(IConstructorService), ServiceTypes = [typeof(INamedService)])]
            public class SyncService : IConstructorService, INamedService { }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer(IConstructorService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IConstructorService");
    }

    [Test]
    public async Task SGIOC030_GenericServiceType_IsRecognized()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class AsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer(IMyService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_PropertyInjectionRequestsSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                [IocInject]
                public IMyService Service { get; set; } = default!;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,PropertyInject,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("Service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_FieldInjectionRequestsSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                [IocInject]
                public IMyService ServiceField = default!;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,FieldInject,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("ServiceField").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_MethodInjectionRequestsSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                [IocInject]
                public void Initialize(IMyService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_ConstructorRequestsKeyedSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "special")]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer([FromKeyedServices("special")] IMyService service) : IConsumer;
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_ConstructorRequestsSyncType_WhenSyncRegistrationAlsoExists_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class AsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class SyncService : IMyService
            {
                [IocInject]
                public void Initialize() { }
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer(IMyService service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC030_MultiKeyedRegistration_AsyncInitKeyReportsDiagnostic()
    {
        // AsyncService registered under key "async" (async-init); SyncService under key "sync".
        // A consumer requesting IService with key "async" has no sync resolution path → SGIOC030.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "async")]
            public class AsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "sync")]
            public class SyncService : IMyService { }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer([FromKeyedServices("async")] IMyService service) : IConsumer;
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_MultiKeyedRegistration_SyncKeyNoDiagnostic()
    {
        // AsyncService registered under key "async" (async-init); SyncService under key "sync".
        // A consumer requesting IService with key "sync" has a sync resolution path → no SGIOC030.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "async")]
            public class AsyncService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "sync")]
            public class SyncService : IMyService { }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer([FromKeyedServices("sync")] IMyService service) : IConsumer;
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030")).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC030_KeyedPropertyInjectionRequestsSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IConsumer { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "special")]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                [IocInject("special")]
                public IMyService Service { get; set; } = default!;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,PropertyInject,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030").ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("Service").And.Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_SameImplRegisteredForTwoKeyedServices_AsyncInitConsumerPathReportsDiagnostic()
    {
        // A single implementation class is registered for two different keyed service types
        // via assembly-level IocRegisterFor attributes. This exercises the AllRegistrations
        // duplicate-impl path in RegisterAnalyzer.ServiceCollection.cs:
        //   first TryAdd succeeds; second TryAdd fails → adds to existingInfo.AllRegistrations.
        // The consumer requests IServiceA (key1) synchronously → SGIOC030 because
        // the only registration for (IServiceA, "key1") is async-init.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MultiKeyImpl), ServiceTypes = [typeof(TestNamespace.IServiceA)], Key = "key1")]
            [assembly: IocRegisterFor(typeof(TestNamespace.MultiKeyImpl), ServiceTypes = [typeof(TestNamespace.IServiceB)], Key = "key2")]

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }
            public interface IConsumer { }

            public class MultiKeyImpl : IServiceA, IServiceB
            {
                [IocInject]
                public Task InjectAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IConsumer)])]
            public class Consumer : IConsumer
            {
                public Consumer([FromKeyedServices("key1")] IServiceA service) { }
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("service").And.Contains("IServiceA");
    }

    private const string AsyncDecoratorFeatures = "Register,Container,MethodInject,AsyncMethodInject";

    [Test]
    [Arguments(AsyncDecoratorFeatures, "IMyService service", 1)]
    [Arguments(AsyncDecoratorFeatures, "Task<IMyService> service", 0)]
    [Arguments(AsyncDecoratorFeatures, "MyService implementation", 0)]
    [Arguments(AsyncDecoratorFeatures, "IPlain plain", 0)]
    [Arguments("Register,Container,MethodInject", "IMyService service", 0)]
    public async Task SGIOC030_AsyncDecoratorOnSyncImplementation_ReportsOnlySyncDecoratedServiceType(
        string features,
        string parameter,
        int expectedCount)
    {
        // The async decorator makes only the decorated service type async-init: not the implementation type, nor the
        // service types it does not implement.
        var source = $$"""
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IPlain { }

            [IocRegister(ServiceTypes = [typeof(IMyService), typeof(IPlain)], Decorators = [typeof(AsyncDecorator)])]
            public class MyService : IMyService, IPlain { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer({{parameter}}) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = features });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(expectedCount);
    }

    [Test]
    [Arguments("ValidRequest", 1)]
    [Arguments("OtherRequest", 0)]
    public async Task SGIOC030_ConstrainedGenericAsyncDecorator_ReportsOnlyWhenDecoratorApplies(string request, int expectedCount)
    {
        // The generic decorator only applies to handlers whose request satisfies its constraint.
        var source = $$"""
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IValidated { }
            public interface IHandler<TRequest> { }

            public class ValidRequest : IValidated { }
            public class OtherRequest { }

            [IocRegister(ServiceTypes = [typeof(IHandler<{{request}}>)], Decorators = [typeof(ValidationDecorator<>)])]
            public class Handler : IHandler<{{request}}> { }

            public class ValidationDecorator<TRequest>(IHandler<TRequest> inner) : IHandler<TRequest>
                where TRequest : IValidated
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer(IHandler<{{request}}> handler) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(expectedCount);
    }

    [Test]
    public async Task SGIOC030_AsyncDecoratorFromDefaultSettings_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.AsyncDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer(IMyService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    [Arguments("Lazy<IMyService> service", 1)]
    [Arguments("Func<IMyService> service", 1)]
    [Arguments("Func<string, IMyService> service", 1)]
    [Arguments("Lazy<Func<IMyService>> service", 1)]
    [Arguments("Lazy<Task<IMyService>> service", 0)]
    [Arguments("Func<Task<IMyService>> service", 0)]
    [Arguments("IEnumerable<IMyService> services", 0)]
    public async Task SGIOC030_LazyOrFuncOfAsyncInitService_ReportsDiagnostic(string parameter, int expectedCount)
    {
        // Lazy<T> and Func<...,T> create T synchronously, so they cannot resolve an async-init service either.
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer({{parameter}}) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(expectedCount);
        if(expectedCount > 0)
            await Assert.That(sgioc030[0].GetMessage()).Contains("'IMyService'");
    }

    [Test]
    public async Task SGIOC030_FuncWithArgumentsOfAsyncDecoratedService_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(AsyncDecorator)])]
            public class MyService(string name) : IMyService { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer(Func<string, IMyService> factory) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]")]
    [Arguments("[assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Singleton)]")]
    [Arguments("[assembly: IocRegisterDefaults(typeof(TestNamespace.IMarker), ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IMyService)])]")]
    [Arguments("[assembly: IocRegisterDefaults(typeof(TestNamespace.IMarker), ServiceLifetime.Singleton, RegisterAllInterfaces = true)]")]
    [Arguments("[assembly: IocRegisterDefaults(typeof(TestNamespace.BaseService), ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IMyService)])]")]
    public async Task SGIOC030_ServiceTypeFromDefaultSettings_ReportsDiagnostic(string defaults)
    {
        // The defaults' target type, the best default's ServiceTypes and its RegisterAllInterfaces add service types,
        // like the generator, so a sync dependency on them is checked too.
        var source = $$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{defaults}}

            namespace TestNamespace;

            public interface IMyService { }
            public interface IMarker { }
            public abstract class BaseService { }

            [IocRegister]
            public class MyService : BaseService, IMyService, IMarker
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer(IMyService service) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_ClosedServiceTypeFromOpenGenericDefaults_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IHandler<>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IHandler<TRequest> { }
            public class Request { }

            [IocRegister]
            public class Handler : IHandler<Request>
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer(IHandler<Request> handler) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC030_AsyncDecoratorFromDefaultsWithoutExplicitServiceTypes_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.AsyncDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer
            {
                public Consumer(IMyService service, MyService implementation) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    [Arguments("public MyServiceDecorator(IMyService inner, IAsyncService dependency) { }", "", "dependency")]
    [Arguments("public MyServiceDecorator(IAsyncService dependency, IMyService inner) { }", "", "dependency")]
    [Arguments("public MyServiceDecorator(IMyService inner) { }", "[IocInject] public Task InitializeAsync(IAsyncService dependency) => Task.CompletedTask;", "dependency")]
    [Arguments("public MyServiceDecorator(IMyService inner) { }", "[IocInject] public void Initialize(Lazy<IAsyncService> dependency) { }", "dependency")]
    [Arguments("public MyServiceDecorator(IMyService inner) { }", "[IocInject] public IAsyncService? Dependency { get; set; }", "Dependency")]
    public async Task SGIOC030_DecoratorRequestsSyncTypeForAsyncInitService_ReportsDiagnostic(string constructor, string member, string memberName)
    {
        // Decorators are not registered services, but the generator resolves their dependencies like a service's.
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IAsyncService { }

            [IocRegister(ServiceTypes = [typeof(IAsyncService)])]
            public class AsyncService : IAsyncService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Key = "other", Decorators = [typeof(MyServiceDecorator)])]
            public class OtherService : IMyService { }

            public class MyServiceDecorator : IMyService
            {
                {{constructor}}

                {{member}}
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        // Reported once, although the decorator decorates two registrations
        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains($"'{memberName}'").And.Contains("IAsyncService");
    }

    [Test]
    public async Task SGIOC030_DecoratorAlsoImplementingDependencyType_ReportsDependency()
    {
        // Only the first parameter of the decorated service type receives the decorated instance; a parameter of another
        // type the decorator implements is resolved as a dependency.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IAsyncService { }

            [IocRegister(ServiceTypes = [typeof(IAsyncService)])]
            public class AsyncService : IAsyncService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyDecorator)])]
            public class MyService : IMyService { }

            public class MyDecorator(IMyService inner, IAsyncService dependency) : IMyService, IAsyncService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("'dependency'");
    }

    [Test]
    public async Task SGIOC030_AsyncDecoratorWrappingItsOwnServiceType_DoesNotReportInnerParameter()
    {
        // The decorator makes IMyService async-init, but its inner IMyService parameter receives the wrapped instance.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(AsyncDecorator)])]
            public class MyService : IMyService { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).IsEmpty();
    }

    [Test]
    public async Task SGIOC030_DefaultDecoratorRequestsSyncTypeForAsyncInitService_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.MyServiceDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }
            public interface IAsyncService { }

            [IocRegister(ServiceTypes = [typeof(IAsyncService)])]
            public class AsyncService : IAsyncService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner, IAsyncService dependency) : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("'dependency'").And.Contains("IAsyncService");
    }

    [Test]
    public async Task SGIOC030_AsyncDecoratedService_MessageDoesNotBlameImplementation()
    {
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(AsyncDecorator)])]
            public class MyService : IMyService { }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer(IMyService service);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("async-init").And.Contains("decorator");
    }
}
