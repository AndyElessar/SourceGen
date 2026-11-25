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

    private const string Handlers = Usings + """
        using System.Collections.Generic;

        public interface IHandler;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
        public sealed class FirstHandler : IHandler;

        [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler)])]
        public sealed class SecondHandler : IHandler;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "k")]
        public sealed class KeyedHandler : IHandler;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Consumer(
            IEnumerable<Lazy<IHandler>> lazy,
            Func<IHandler>[] funcs,
            IReadOnlyList<Task<IHandler>> tasks,
            [IocInject(Key = "k")] IEnumerable<Lazy<IHandler>> keyed,
            [IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] IEnumerable<Func<IHandler>> any);

        """;

    [Test]
    public async Task WrapperCollection_ContainerWithFallbackProvider_WrapsEachCandidateWithoutAMerge()
    {
        GeneratorTestResult result = await RunAsync(Handlers + """
            [IocContainer]
            public partial class AppContainer
            {
                public partial IEnumerable<Lazy<IHandler>> Handlers { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["Handlers", "__lookup", "__collections", "__BuildKeyedLookup"], "__Get_Consumer_", "__All_Lazy", "__All_Func", "__All_Task"));
    }

    [Test]
    public async Task WrapperCollection_TwoCandidatesWithOneKey_TheKeyedEntryOfTheWrapperThrows()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Collections.Generic;

            public interface IHandler;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "k")]
            public sealed class FirstHandler : IHandler;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "k")]
            public sealed class SecondHandler : IHandler;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "k")] IEnumerable<Lazy<IHandler>> handlers);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains(
            "table.TryAdd((typeof(global::System.Lazy<global::App.IHandler>), k0), static (c, _) => throw new global::System.InvalidOperationException(\"More than one registration provides 'System.Lazy<App.IHandler> (Key: \\\"k\\\")': App.FirstHandler (Key: \\\"k\\\"), App.SecondHandler (Key: \\\"k\\\").\"));");
    }

    [Test]
    public async Task Cycle_ThroughACollectionOfLazy_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Collections.Generic;

            public interface IFoo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)])]
            public sealed class Composite(IEnumerable<Lazy<IFoo>> all) : IFoo;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task Cycle_ThroughACollectionOfTask_ReportedByTheFullCheck()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Collections.Generic;

            public interface IFoo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)])]
            public sealed class Composite(IEnumerable<Task<IFoo>> all) : IFoo;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC301:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.Composite → App.Composite");
    }

    [Test]
    public async Task SGIOC502_CollectionOfFuncOfTransient_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Collections.Generic;

            public interface IJob;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IJob)])]
            public sealed class Job : IJob;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Scheduler(IEnumerable<Func<IJob>> jobs);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task Wrapper_ServiceCollectionExtension_RegistersTheWrappersOfEachCandidate()
    {
        GeneratorTestResult result = await RunAsync(
            Handlers + """
                public interface IMissing;

                [IocRegister(ServiceLifetime.Scoped)]
                public sealed class Session;

                [IocRegister(ServiceLifetime.Transient)]
                public sealed class Holder(
                    Lazy<IHandler> handler,
                    Func<Session> sessions,
                    Task<Session> session,
                    Lazy<Task<IHandler>> lazyTask,
                    [IocInject(Key = "k")] Func<IHandler> keyed,
                    Lazy<IMissing> missing,
                    IEnumerable<Task<IHandler>> tasks,
                    [IocInject(Key = "k")] IEnumerable<Task<IHandler>> keyedTasks,
                    [IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] IEnumerable<Task<IHandler>> anyTasks,
                    Func<IMissing>? optional = null,
                    Task<IMissing>? task = null)
                {
                    // A member that keeps its initializer: a wrapper that the site creates is always assigned
                    [IocInject]
                    public Lazy<IMissing> MissingMember { get; set; } = new(() => null!);

                    [IocInject]
                    public Lazy<IHandler> HandlerMember { get; set; } = new(() => null!);
                }
                """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["AddApp"], "__Create_Consumer_", "__Create_Holder_"));
    }

    [Test]
    public async Task WrapperCollection_ServiceCollectionExtensionWithAKeyEqualOnlyAtRunTime_RegistersTheWrapperOfThatKey()
    {
        // MS.DI compares the keys with Equals, so the wrapper of each key that can be equal is registered with its own key
        GeneratorTestResult result = await RunAsync(
            Usings + """
                using System.Collections.Generic;

                public static class Keys
                {
                    public static readonly string A = "a";
                    public static readonly int Number = 1;
                }

                public interface IStore;

                [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
                public sealed class Store : IStore;

                [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "b")]
                public sealed class OtherStore : IStore;

                [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.A))]
                public sealed class SymbolStore : IStore;

                [IocRegister(ServiceLifetime.Transient)]
                public sealed class Consumer(
                    [IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IEnumerable<Lazy<IStore>> stores,
                    [IocInject(Key = "c")] IEnumerable<Task<IStore>> values,
                    [IocInject(Key = "c")] IEnumerable<Lazy<Task<IStore>>> lazyValues,
                    [IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.Number))] IEnumerable<Func<IStore>> numbers);
                """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Assert.That(source).Contains("AddKeyedSingleton<global::System.Lazy<global::App.IStore>>(services, \"a\",");
        await Assert.That(source).Contains("AddKeyedSingleton<global::System.Lazy<global::App.IStore>>(services, \"b\",");

        // A value demand ("c") can be equal to a symbol registration, but not to another value
        await Assert.That(source).Contains("AddKeyedSingleton<global::System.Lazy<global::System.Threading.Tasks.Task<global::App.IStore>>>(services, global::App.Keys.A,");
        await Assert.That(source).DoesNotContain("AddKeyedSingleton<global::System.Lazy<global::System.Threading.Tasks.Task<global::App.IStore>>>(services, \"a\",");

        // An int key cannot be equal to a string key
        await Assert.That(source).DoesNotContain("global::System.Func<global::App.IStore>>(services");
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
