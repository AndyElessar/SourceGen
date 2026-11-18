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
    public async Task SGIOC030_DecoratorOfTwoServiceTypes_ReportsParameterResolvedForTheOtherServiceType()
    {
        // Decorating IFoo, foo receives the decorated instance and bar is resolved; decorating IBar, the reverse.
        // The async decorator makes both service types async-init, so each resolved parameter is reported.
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo { }
            public interface IBar { }

            [IocRegister(ServiceTypes = [typeof(IFoo), typeof(IBar)], Decorators = [typeof(FooBarDecorator)])]
            public class Service : IFoo, IBar { }

            public class FooBarDecorator(IFoo foo, IBar bar) : IFoo, IBar
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var messages = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).Select(static d => d.GetMessage()).ToList();

        await Assert.That(messages).Count().IsEqualTo(2);
        await Assert.That(messages.Any(static m => m.Contains("'foo'"))).IsTrue();
        await Assert.That(messages.Any(static m => m.Contains("'bar'"))).IsTrue();
    }

    [Test]
    [Arguments("[IocRegister(ServiceTypes = [typeof(IHandler<int>)], Decorators = [typeof(Decorator<>)])]")]
    [Arguments("[IocRegister(ServiceTypes = [typeof(IHandler<int>)], Decorators = [typeof(Decorator<>)])] [IocRegister(ServiceTypes = [typeof(IHandler<int>)], Key = \"other\", Decorators = [typeof(Decorator<>)])]")]
    public async Task SGIOC030_OpenGenericDecoratorRequestsClosedAsyncInitService_ReportsDiagnostic(string registrations)
    {
        // The decorator's dependency is checked as closed for the decorated service type: IAsyncService<int>
        var source = $$"""
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public interface IAsyncService<T> { }

            [IocRegister(ServiceTypes = [typeof(IAsyncService<int>)])]
            public class AsyncService : IAsyncService<int>
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            {{registrations}}
            public class Handler : IHandler<int> { }

            public class Decorator<T>(IHandler<T> inner, IAsyncService<T> dependency) : IHandler<T>
            {
                [IocInject]
                public IAsyncService<T>? Property { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,MethodInject,AsyncMethodInject" });
        var messages = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).Select(static d => d.GetMessage()).ToList();

        // Reported once per dependency, however many registrations the decorator decorates
        await Assert.That(messages).Count().IsEqualTo(2);
        await Assert.That(messages.Any(static m => m.Contains("'dependency'") && m.Contains("IAsyncService<int>"))).IsTrue();
        await Assert.That(messages.Any(static m => m.Contains("'Property'") && m.Contains("IAsyncService<int>"))).IsTrue();
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

    [Test]
    [Arguments("IHandler<(Entity M, string N)> handler")]
    [Arguments("Lazy<IHandler<(Entity M, string N)>> handler")]
    [Arguments("Func<IHandler<(Entity, string)>> handler")]
    [Arguments("Func<int, IHandler<(Entity M, string N)>> handler")]
    public async Task SGIOC030_TupleElementNamesDiffer_ReportsDiagnostic(string parameter)
    {
        // Tuple element names are not part of the runtime type, so the dependency resolves the async-init registration
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public class AsyncHandler : IHandler<(Entity X, string Y)>
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer({{parameter}});
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("'handler'");
    }

    [Test]
    public async Task SGIOC030_SyncRegistrationWithOtherTupleElementNames_NoDiagnostic()
    {
        // The synchronous registration has the same runtime type, so the service can be resolved synchronously
        const string source = """
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public class AsyncHandler : IHandler<(Entity X, string Y)>
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity A, string B)>)])]
            public class SyncHandler : IHandler<(Entity A, string B)>;

            [IocRegister]
            public class Consumer(IHandler<(Entity X, string Y)> handler);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030)).IsEmpty();
    }

    [Test]
    [Arguments("(int A, int B)", "(int X, int Y)")]
    [Arguments("(int X, int Y)", "(int A, int B)")]
    public async Task SGIOC030_ImplementationTypesDifferingOnlyInTupleElementNames_MergeRegistrations(string first, string second)
    {
        // Both attributes register the same runtime implementation type, so the service type of the second one is a
        // registration of that implementation too
        var source = $$"""
            using System.Threading.Tasks;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Impl<{{first}}>))]
            [assembly: IocRegisterFor(typeof(Impl<{{second}}>), ServiceTypes = [typeof(IHandler<{{second}}>)])]

            namespace TestNamespace;

            public interface IHandler<T>;

            public sealed class Impl<T> : IHandler<T>
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocRegister]
            public class Consumer(IHandler<(int M, int N)> handler);
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = AsyncDecoratorFeatures });
        var sgioc030 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC030).ToList();

        await Assert.That(sgioc030).Count().IsEqualTo(1);
        await Assert.That(sgioc030[0].GetMessage()).Contains("'handler'");
        await Assert.That(diagnostics.Where(d => d.Id is Constants.SGIOC001 or Constants.SGIOC002 or Constants.SGIOC003 or Constants.SGIOC004 or Constants.SGIOC005 or Constants.SGIOC032)).IsEmpty();

        // The same runtime implementation type is registered twice with the same key and tags
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC011)).Count().IsEqualTo(1);
    }

    [Test]
    [Category(Constants.Defaults)]
    [Arguments("[IocRegisterDefaults(typeof(IMarker), ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]\npublic interface IMarker;")]
    [Arguments("public interface IMarker;\n[IocRegisterDefaults<IMarker>(ServiceLifetime.Singleton, RegisterAllInterfaces = true)]\npublic sealed class Holder;")]
    [Arguments("public interface IMarker;\n[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton)]\npublic struct Holder;")]
    public async Task SGIOC030_ServiceTypeFromTypeLevelDefaultSettings_ReportsDiagnostic(string declarations)
    {
        // Type-level default settings add service types like assembly-level ones
        var source = $$"""
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;
            {{declarations}}

            [IocRegister]
            public class MyService : IMyService, IMarker
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
        await Assert.That(sgioc030[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC030_InstanceWithAsyncInjectMethod_IsNotAsyncInit_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            // An Instance is registered as is, without calling its inject methods, so it is not async-init
            public sealed class AsyncThing
            {
                public static readonly AsyncThing Default = new();

                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegisterFor<AsyncThing>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.AsyncThing.Default")]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Consumer(AsyncThing thing, Lazy<AsyncThing> lazy);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial AsyncThing GetThing();
                public partial Lazy<AsyncThing> GetLazyThing();
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,MethodInject,AsyncMethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC030")).IsEmpty();
    }

    [Test]
    public async Task SGIOC030_SameTypeInstanceAndAsyncInitRegistrations_InstanceDeclaredFirst_ReportsOnlyAsyncInitKey()
    {
        // Async-init is decided per registration, like the generator: the Instance under "i" is synchronous, while the
        // non-keyed registration of the same type is async-init, whichever is declared first
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegisterFor<AsyncThing>(ServiceLifetime.Singleton, Key = "i", Instance = "global::TestNamespace.AsyncThing.Default")]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class AsyncThing
            {
                public static readonly AsyncThing Default = new();

                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class UnkeyedConsumer(AsyncThing unkeyed);

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class KeyedConsumer([FromKeyedServices("i")] AsyncThing keyed);
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
        await Assert.That(sgioc030[0].GetMessage()).Contains("'unkeyed'");
    }

    [Test]
    public async Task SGIOC030_SameTypeInstanceAndAsyncInitRegistrations_AsyncInitDeclaredFirst_ReportsOnlyAsyncInitKey()
    {
        // Async-init is decided per registration, like the generator: the Instance under "i" is synchronous, while the
        // non-keyed registration of the same type is async-init, whichever is declared first
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class AsyncThing
            {
                public static readonly AsyncThing Default = new();

                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegisterFor<AsyncThing>(ServiceLifetime.Singleton, Key = "i", Instance = "global::TestNamespace.AsyncThing.Default")]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class UnkeyedConsumer(AsyncThing unkeyed);

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class KeyedConsumer([FromKeyedServices("i")] AsyncThing keyed);
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
        await Assert.That(sgioc030[0].GetMessage()).Contains("'unkeyed'");
    }
}
