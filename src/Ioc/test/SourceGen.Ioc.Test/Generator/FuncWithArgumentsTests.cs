using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// <c>Func&lt;A1, ..., An, T&gt;</c> of a single service (Analysis.Model.spec, "Mapping of <c>Func&lt;..., T&gt;</c>";
/// Container.Output.spec, "Wrapper types"; ServiceCollection.Output.spec, "Wrapper types and collections").
/// </summary>
public class FuncWithArgumentsTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    /// <summary><c>Job(string name, Clock clock)</c>: <c>name</c> has no registration, so only a caller can supply it.</summary>
    private const string Jobs = Usings + """
        [IocRegister(ServiceLifetime.Singleton)]
        public sealed class Clock;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Job(string name, Clock clock);

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
    public async Task Func_Container_TheCallerSuppliesTheMappedParameter()
    {
        GeneratorTestResult result = await RunAsync(Jobs + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<string, Job> jobs, Func<string, Task<Job>> tasks);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial Func<string, Job> Jobs { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["Jobs", "__ThrowUnavailable"], "__Get_Job_", "__Create_Consumer_", "__w_", "__Wrap_", "__CreateWrap_", "__CreateWith_"));
    }

    [Test]
    public async Task Func_ServiceCollectionExtension_OneTransientRegistrationForEachCandidate()
    {
        GeneratorTestResult result = await RunAsync(
            Jobs + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<string, Job> jobs, Func<string, Task<Job>> tasks);
            """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["AddApp"], "__CreateWith_"));
    }

    [Test]
    public async Task Func_KeyedMethodAccessorAndMemberSite_Compile()
    {
        GeneratorTestResult result = await RunAsync(
            Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Clock;

            [IocRegister(ServiceLifetime.Transient, Key = "k")]
            public sealed class Job(string name, Clock clock);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer
            {
                [IocInject(Key = "k")] public Func<string, Task<Job>> Jobs { get; set; } = null!;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                [IocInject(Key = "k")]
                public partial Func<string, Job> GetJobs();
            }
            """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("__CreateWith_");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("AddKeyedTransient<global::System.Func<global::System.String, global::System.Threading.Tasks.Task<global::App.Job>>>");
    }

    [Test]
    public async Task SGIOC305_InvalidAccessor_ReportedOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(string name = "");

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC305:AppContainer|}
            {
                public partial Func<int, Job> Jobs { get; }
            }
            """);

        await Assert.That(Message(result, "SGIOC305")).IsEqualTo("'System.Func<System.Int32, App.Job>' cannot be generated: the argument type 'System.Int32' matches no constructor parameter of 'App.Job'");
    }

    [Test]
    public async Task SGIOC305_SameInvalidFuncAtTwoSites_ReportedOneTimeOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IMissing;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Func<string, IMissing> factory)
            {
                [IocInject] public Func<string, IMissing> Other { get; set; } = null!;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC305:AppContainer|}
            {
                public partial Func<string, IMissing> Missing { get; }
            }
            """);

        await Assert.That(Message(result, "SGIOC305")).IsEqualTo("'System.Func<System.String, App.IMissing>' cannot be generated: 'App.IMissing' is not registered");
    }

    [Test]
    public async Task SGIOC305_ArgumentTypeTwice_ReportedAtTheSiteAlsoWithoutCandidate()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IMissing;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer({|SGIOC305:Func<string, string, IMissing> factory|});
            """);

        await Assert.That(Message(result, "SGIOC305"))
            .IsEqualTo("'System.Func<System.String, System.String, App.IMissing>' cannot be generated: the argument type 'System.String' occurs more than once");
    }

    [Test]
    public async Task SGIOC305_TargetIsNotTransient_ReportedAtTheSite()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Job(string name = "");

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer({|SGIOC305:Func<string, Job> factory|});

            [IocContainer]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC305")).IsEqualTo("'System.Func<System.String, App.Job>' cannot be generated: 'App.Job' is Singleton; it must be Transient");
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    [Arguments("Func<int, Job>", "Job(string name = \"\")", "'System.Func<System.Int32, App.Job>' cannot be generated: the argument type 'System.Int32' matches no constructor parameter of 'App.Job (Key: \"k\")'")]
    [Arguments("Func<string, Job>", "Job(string first = \"\", string second = \"\")", "'System.Func<System.String, App.Job>' cannot be generated: the argument type 'System.String' matches more than one constructor parameter of 'App.Job (Key: \"k\")'")]
    [Arguments("Func<object, Job>", "Job([ServiceKey] object key)", "'System.Func<System.Object, App.Job>' cannot be generated: the argument type 'System.Object' matches no constructor parameter of 'App.Job (Key: \"k\")'")]
    public async Task SGIOC305_ArgumentDoesNotMapToOneParameter_ReportedAtTheSite(string func, string job, string message)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Transient, Key = "k")]
            public sealed class {{job}};

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer({|SGIOC305:[IocInject(Key = "k")] {{func}} factory|});
            """);

        await Assert.That(Message(result, "SGIOC305")).IsEqualTo(message);
    }

    [Test]
    [Arguments(false, "'App.IMissing' is not registered")]
    [Arguments(true, "'App.IMissing' is not registered in the container")]
    public async Task SGIOC305_TargetMissing_ReportedOnTheContainerAlsoWithFallback(bool fallback, string reason)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IMissing;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Func<string, IMissing>? factory = null);

            [IocContainer(IntegrateServiceProvider = {{(fallback ? "true" : "false")}})]
            public partial class {|SGIOC305:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC305")).IsEqualTo($"'System.Func<System.String, App.IMissing>' cannot be generated: {reason}");
    }

    [Test]
    public async Task SGIOC305_TargetAmbiguous_ReportedOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler)])]
            public sealed class FirstHandler(string name = "") : IHandler;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler)])]
            public sealed class SecondHandler(string name = "") : IHandler;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Func<string, IHandler> factory);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC305:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC305"))
            .IsEqualTo("'System.Func<System.String, App.IHandler>' cannot be generated: 'App.IHandler' is ambiguous: App.FirstHandler, App.SecondHandler");
    }

    [Test]
    public async Task SGIOC305_TargetIsABuiltInService_ReportedOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Func<string, IServiceProvider> factory);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC305:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC305"))
            .IsEqualTo("'System.Func<System.String, System.IServiceProvider>' cannot be generated: 'System.IServiceProvider' is a built-in service");
    }

    [Test]
    public async Task Func_MappedParameterWithRegistration_TheTargetResolvesItAsUsual()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Clock;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(Clock clock);

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Func<Clock, Job> factory, Job job);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("__ThrowUnavailable");
    }

    [Test]
    public async Task Func_TwoFuncsMapDifferentParameters_EachResolvesTheOtherParameterAsUsual()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(string name, int count);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Host(Func<string, Job> byName, Func<int, Job> byCount);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402,SGIOC402:AppContainer|};
            """);

        string[] messages = [.. result.ReportedDiagnostics.Where(static d => d.Id == "SGIOC402").Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)];
        await Assert.That(string.Join(" | ", messages)).IsEqualTo(
            "'App.Job → count' requires 'System.Int32', which is not registered | 'App.Job → name' requires 'System.String', which is not registered");
    }

    [Test]
    public async Task Func_TwoFuncsMapDifferentParameters_TheOtherParameterFromTheFallback()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(string name, int count);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Host(Func<string, Job> byName, Func<int, Job> byCount);

            [IocContainer]
            public partial class {|SGIOC501,SGIOC501:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, [], "__Get_Job_", "__CreateWith_"));
    }

    [Test]
    public async Task SGIOC302_ScopedParameterThatTheFuncMaps_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(Session session);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Host(Func<Session, Job> jobs);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC302_ScopedParameterThatTheFuncDoesNotMap_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(string name, Session session);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Host({|SGIOC302:Func<string, Job> jobs|});

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC302")).IsEqualTo("Singleton 'App.Host' depends on scoped 'App.Session' through App.Host → App.Job → App.Session");
    }

    [Test]
    public async Task SGIOC302_TargetAlsoReachedDirectly_ReportedAtTheDirectSite()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Job(Session session);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Host(Func<Session, Job> jobs, {|SGIOC302,SGIOC502:Job job|});

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC302")).IsEqualTo("Singleton 'App.Host' depends on scoped 'App.Session' through App.Host → App.Job → App.Session");
    }

    [Test]
    public async Task Func_MappedParameterWithFallback_TheTargetResolvesItFromTheFallbackWithoutInfo()
    {
        GeneratorTestResult result = await RunAsync(Jobs + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<string, Job> jobs);

            [IocContainer]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).DoesNotContain("__ThrowUnavailable");
        await Assert.That(source).Contains("GetRequiredService(c.__Fallback(), typeof(global::System.String))");
    }

    [Test]
    public async Task Func_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>([Jobs + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<string, Job> jobs, Func<string, Task<Job>> tasks);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]"]);
}
