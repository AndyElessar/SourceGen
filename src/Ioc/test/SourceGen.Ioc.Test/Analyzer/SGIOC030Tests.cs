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
}
