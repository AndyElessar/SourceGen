using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC045: Container does not dispose transient service.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC045)]
public class SGIOC045Tests
{
    private static async Task<List<Diagnostic>> RunAsync(string source, Dictionary<string, string>? analyzerConfigOptions = null)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        return [.. SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC045)];
    }

    [Test]
    [Arguments("System.IDisposable", "public void Dispose() { }")]
    [Arguments("System.IAsyncDisposable", "public System.Threading.Tasks.ValueTask DisposeAsync() => default;")]
    public async Task SGIOC045_TransientDisposable_ReportsDiagnostic(string disposableInterface, string disposeMethod)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            [IocRegister<IMyService>(ServiceLifetime.Transient)]
            public sealed class MyService : IMyService, {{disposableInterface}}
            {
                {{disposeMethod}}
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
        await Assert.That(sgioc045[0].GetMessage()).Contains("'TestContainer'").And.Contains("'MyService'");
        await Assert.That(sgioc045[0].Location.SourceTree!.ToString().Substring(sgioc045[0].Location.SourceSpan.Start))
            .StartsWith("IocRegister<IMyService>");
    }

    [Test]
    public async Task SGIOC045_DisposableThroughBaseClass_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class DisposableBase : IDisposable
            {
                public void Dispose() { }
            }

            [IocRegister]
            public sealed class MyService : DisposableBase;

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("Scoped")]
    [Arguments("Singleton")]
    public async Task SGIOC045_NonTransientDisposable_NoDiagnostic(string lifetime)
    {
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(ServiceLifetime.{{lifetime}})]
            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC045_TransientNotDisposable_NoDiagnostic()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public sealed class MyService;

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC045_DefaultSettingsLifetimeScoped_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped)]
            [IocRegister]
            public sealed class MyService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC045_ExplicitTransientOverridesDefaultSettings_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped)]
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class MyService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("Scoped", 0)]
    [Arguments("Transient", 1)]
    public async Task SGIOC045_MSBuildDefaultLifetime_AppliesToRegistrationWithoutLifetime(string defaultLifetime, int expected)
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source, new()
        {
            ["build_property.SourceGenIocDefaultLifetime"] = defaultLifetime
        });

        await Assert.That(sgioc045).Count().IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC045_DefaultSettingsImplementationTypes_ReportsOnDefaultsAttribute()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Transient, ImplementationTypes = [typeof(TestNamespace.MyService)])]

            namespace TestNamespace;

            public interface IMyService;

            public sealed class MyService : IMyService, IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
        await Assert.That(sgioc045[0].Location.SourceTree!.ToString().Substring(sgioc045[0].Location.SourceSpan.Start))
            .StartsWith("IocRegisterDefaults<");
    }

    [Test]
    [Arguments("IConnection", 1)]
    [Arguments("Connection", 1)]
    [Arguments("IService", 0)]
    public async Task SGIOC045_TransientFactory_ChecksFactoryReturnType(string returnType, int expected)
    {
        var source = $$"""
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService;
            public interface IConnection : IService, IDisposable;

            public sealed class Connection : IConnection
            {
                public void Dispose() { }
            }

            public static class Factories
            {
                public static {{returnType}} Create() => new Connection();
            }

            [IocRegisterFor<IService>(Factory = nameof(Factories.Create))]
            public sealed class Registrations;

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(expected);
        if(expected > 0)
            await Assert.That(sgioc045[0].GetMessage()).Contains($"'{returnType}'");
    }

    [Test]
    public async Task SGIOC045_DefaultSettingsFactory_ChecksFactoryReturnType()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConnection : IDisposable;

            public static class Factories
            {
                public static IConnection Create() => new Connection();
            }

            [IocRegisterDefaults<IConnection>(ServiceLifetime.Transient, Factory = nameof(Factories.Create))]
            [IocRegister]
            public sealed class Connection : IConnection
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
        await Assert.That(sgioc045[0].GetMessage()).Contains("'IConnection'");
    }

    [Test]
    public async Task SGIOC045_AsyncInitImplementation_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public sealed class MyService : IAsyncDisposable
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;

                public ValueTask DisposeAsync() => default;
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC045_AssemblyLevelRegistration_ReportsOnAttribute()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.MyService>]

            namespace TestNamespace;

            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
        await Assert.That(sgioc045[0].Location.SourceTree!.ToString().Substring(sgioc045[0].Location.SourceSpan.Start))
            .StartsWith("IocRegisterFor<");
    }

    [Test]
    public async Task SGIOC045_KeyedTransientDisposable_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Key = "a")]
            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC045_UnconstructibleImplementation_NoDiagnostic()
    {
        // The generator does not register an implementation it cannot construct (SGIOC001)
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConnection : IDisposable;

            [IocRegister]
            public abstract class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocRegisterFor<IConnection>]
            public sealed class Registrations;

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC045_TransientFactoryOfDisposableImplementation_UsesFactoryReturnType()
    {
        // The container creates the instance with the factory, not the constructor of the implementation type
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            [IocRegister<IMyService>(Factory = nameof(Create))]
            public sealed class MyService : IMyService, IDisposable
            {
                public static IMyService Create() => new MyService();
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    [Arguments("")]
    [Arguments("ServiceLifetime.Transient, ")]
    public async Task SGIOC045_Instance_NoDiagnostic(string lifetime)
    {
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister({{lifetime}}Instance = nameof(Default))]
            public sealed class MyService : IDisposable
            {
                public static readonly MyService Default = new();
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC045_OpenGenericTransientDisposable_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T>;

            [IocRegister(ServiceTypes = [typeof(IRepository<>)])]
            public sealed class Repository<T> : IRepository<T>, IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC045_SeveralContainers_ReportsOncePerContainer()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class FirstContainer;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class SecondContainer;

            [IocContainer(ExplicitOnly = true)]
            public partial class EmptyContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(2);
        await Assert.That(sgioc045.Select(static d => d.GetMessage()))
            .Contains(static m => m.Contains("'FirstContainer'"))
            .And.Contains(static m => m.Contains("'SecondContainer'"));
    }

    [Test]
    public async Task SGIOC045_IncludeTags_OnlyReportsIncludedRegistrations()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Tags = ["A"])]
            public sealed class TaggedService : IDisposable
            {
                public void Dispose() { }
            }

            [IocRegister(Tags = ["B"])]
            public sealed class OtherService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer(IncludeTags = ["A"])]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
        await Assert.That(sgioc045[0].GetMessage()).Contains("'TaggedService'");
    }

    [Test]
    public async Task SGIOC045_ImportedModule_ReportsForModuleOnly()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor<MyService>]
            public partial class Module;

            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer(ExplicitOnly = true)]
            [IocImportModule<Module>]
            public partial class AppContainer;
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(1);
        await Assert.That(sgioc045[0].GetMessage()).Contains("'Module'");
    }

    [Test]
    public async Task SGIOC045_ContainerFeatureDisabled_NoDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var sgioc045 = await RunAsync(source, new()
        {
            ["build_property.SourceGenIocFeatures"] = "Register"
        });

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC045_NoContainer_NoDiagnostic()
    {
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister]
            public sealed class MyService : IDisposable
            {
                public void Dispose() { }
            }
            """;

        var sgioc045 = await RunAsync(source);

        await Assert.That(sgioc045).Count().IsEqualTo(0);
    }
}
