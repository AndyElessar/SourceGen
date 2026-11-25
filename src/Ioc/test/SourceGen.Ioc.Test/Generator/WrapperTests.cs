using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// <c>Lazy&lt;T&gt;</c>, <c>Func&lt;T&gt;</c> and <c>Task&lt;T&gt;</c> of a single service (Analysis.Model.spec, "Grammar of
/// wrapper types", "Circular dependencies", "Lifetime checks"; Container.Output.spec, "Wrapper types", "Behavior for each
/// request").
/// </summary>
public class WrapperTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Services = Usings + """
        public interface IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        public sealed class SystemClock : IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)], Key = "utc")]
        public sealed class UtcClock : IClock;

        [IocRegister(ServiceLifetime.Scoped)]
        public sealed class Session;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Job;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Consumer(
            Lazy<IClock> clock,
            Func<Session> session,
            Task<Job> job,
            Func<Job> jobs,
            Lazy<Task<Job>> lazyJob,
            Func<Task<IClock>> clockTask,
            [IocInject(Key = "utc")] Lazy<IClock> utc,
            Lazy<IServiceProvider> provider);

        """;

    private static async Task<GeneratorTestResult> RunAsync(params string[] sources)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(sources);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            parsed,
            new()
            {
                GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" },
                AllowedInputErrors = new HashSet<string> { "CS8795", "CS9248" },
            });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static string Message(GeneratorTestResult result, string id)
        => result.ReportedDiagnostics.First(d => d.Id == id).GetMessage(CultureInfo.InvariantCulture);

    [Test]
    public async Task Wrapper_Container_CachedByTheLifetimeOfTheService()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial Func<Job> Jobs { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["__Singletons", "Jobs", "__lookup", "__BuildKeyedLookup"], "__Get_Consumer_", "__w_", "__Wrap_", "__CreateWrap_"));
    }

    [Test]
    public async Task Wrapper_ServiceOnlyInTheFallbackProvider_TheWrapperResolvesItFromTheFallback()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface ILogger;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Lazy<ILogger> logger, Func<ILogger>? optional = null, Task<ILogger>? task = null);

            [IocContainer]
            public partial class {|SGIOC501:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Message(result, "SGIOC501")).IsEqualTo(
            "'App.Consumer → logger' requires 'System.Lazy<App.ILogger>', which is not registered in the container; it is resolved from the fallback service provider");
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, [], "__Get_Consumer_", "__w_", "__Wrap_", "__CreateWrap_"));
    }

    [Test]
    public async Task Wrapper_MissingWithoutFallback_RequiredReportedAndOptionalUsesTheDefault()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface ILogger;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Lazy<ILogger> logger, Func<ILogger>? optional = null);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC402")).IsEqualTo("'App.Consumer → logger' requires 'System.Lazy<App.ILogger>', which is not registered");
    }

    [Test]
    public async Task Wrapper_OptionalMissingWithoutFallback_DefaultValue()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface ILogger;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Func<ILogger>? optional = null);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("(global::System.Func<global::App.ILogger>)(default!)");
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("__Wrap_");
    }

    [Test]
    public async Task Cycle_ThroughLazyOrFunc_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class A(Lazy<B> b);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class B(Func<A> a);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task Cycle_ThroughTask_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class A({|SGIOC301:Task<B> b|});

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class B(A a);
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.A → App.B → App.A");
    }

    [Test]
    [Arguments("Lazy<Session>")]
    [Arguments("Func<Session>")]
    [Arguments("Task<Session>")]
    public async Task Captive_SingletonOnAWrapperOfScoped_ReportedAtTheSite(string wrapper)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC302:{{wrapper}} session|});
            """);

        await Assert.That(Message(result, "SGIOC302")).IsEqualTo("Singleton 'App.Cache' depends on scoped 'App.Session'");
    }

    [Test]
    public async Task Captive_SingletonOnFuncOfTransientOnScoped_ReportedThroughTheTransient()
    {
        // A Transient that a Singleton reaches through Func joins the singleton context (Analysis.Model.spec, "Lifetime checks")
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(Session session);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC302:Func<Job> jobs|});
            """);

        await Assert.That(Message(result, "SGIOC302")).IsEqualTo("Singleton 'App.Cache' depends on scoped 'App.Session' through App.Cache → App.Job → App.Session");
    }

    [Test]
    public async Task SGIOC502_SingletonHoldsTransient_ReportedForLazyAndTaskButNotForFunc()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC502:Lazy<Job> lazy|}, {|SGIOC502:Task<Job> task|}, Func<Job> jobs);
            """);

        await Assert.That(Message(result, "SGIOC502"))
            .IsEqualTo("Singleton 'App.Cache' holds transient 'App.Job' for the lifetime of the container; depend on Func<App.Job> to create instances on demand");
    }

    [Test]
    public async Task SGIOC505_WrapperWithASymbolKey_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public static class Keys
            {
                public static readonly string Utc = "utc";
            }

            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)], Key = "utc")]
            public sealed class UtcClock : IClock;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.Utc))] Lazy<IClock> clock);

            [IocContainer]
            public partial class {|SGIOC501,SGIOC505:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC505")).StartsWith("'App.Consumer → clock' requires 'System.Lazy<App.IClock> (Key: App.Keys.Utc)'");
    }

    [Test]
    public async Task Wrapper_ContainerAndServiceCollectionExtension_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [
                Services + """
                    [IocContainer]
                    public partial class AppContainer;
                    """,
            ],
            new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } });
}
