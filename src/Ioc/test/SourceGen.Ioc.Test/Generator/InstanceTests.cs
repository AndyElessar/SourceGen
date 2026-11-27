using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Instance registrations (Annotations.spec, "Instance registration", "Service types of an instance", "Checks of a
/// factory or instance member"; Container.Output.spec, "Resolution of a closed definition"; ServiceCollection.Output.spec,
/// "Registration for each lifetime").
/// </summary>
public class InstanceTests
{
    /// <summary>The using directives. An assembly attribute follows them, before the namespace.</summary>
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        """;

    private const string Usings = Header + """
        namespace App;

        """;

    private const string Services = Header + """
        [assembly: IocInstanceFor(nameof(App.Clocks.Backup), Key = "backup")]
        namespace App;

        public interface IClock;
        public sealed class SystemClock : IClock, IDisposable { public void Dispose() { } }

        public static class Clocks
        {
            [IocInstance(ServiceTypes = [typeof(IClock)])]
            public static readonly SystemClock Utc = new();

            [IocInstance]
            public static IClock Local { get; } = new SystemClock();

            public static readonly IClock Backup = new SystemClock();
        }

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Consumer(IClock clock, [FromKeyedServices("backup")] IClock backup, Lazy<IClock> lazy);

        """;

    private static readonly GeneratorRunOptions Options = new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } };

    private static async Task<GeneratorTestResult> RunAsync(params string[] sources)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(sources);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, Options);
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static string Message(GeneratorTestResult result, string id) => result.ReportedDiagnostics.First(d => d.Id == id).GetMessage(CultureInfo.InvariantCulture);

    [Test]
    public async Task Instance_Container_ReadsTheMemberOnEachRequestAndDoesNotDisposeIt()
    {
        // Utc registers only SystemClock here, so that IClock without a key has one candidate (Local). Nothing stores an
        // instance, so nothing disposes it
        GeneratorTestResult result = await RunAsync(Services.Replace("[IocInstance(ServiceTypes = [typeof(IClock)])]", "[IocInstance]") + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).DoesNotContain("__f_Clocks_");
        await Verify(SnapshotHelper.MembersByPrefix(source, [], "__Get_Clocks_"));
    }

    [Test]
    public async Task Instance_ServiceCollectionExtension_PassesTheInstanceDirectly()
    {
        GeneratorTestResult result = await RunAsync(Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task Instance_OneMemberWithTwoKeys_TwoDefinitionsOfTheSameObject()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks
            {
                [IocInstance(Key = "a")]
                [IocInstance(Key = "b")]
                public static readonly IClock Shared = new SystemClock();
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([FromKeyedServices("a")] IClock a, [FromKeyedServices("b")] IClock b);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        // Two keys give two closed definitions; each one reads the same member
        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source.Split("instance = global::App.Clocks.Shared").Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task Instance_ScopedServiceDependsOnIt_NoCaptiveDependency()
    {
        // An instance is a Singleton without dependencies (Analysis.Model.spec, "Lifetime checks")
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks
            {
                [IocInstance]
                public static readonly IClock Utc = new SystemClock();
            }

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session(IClock clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    [Arguments("public static readonly int Count = 1;", "it is a value type")]
    [Arguments("public static readonly AppContainer Container = null!;", "it is a container class")]
    public async Task SGIOC004_InvalidMemberType_ReportedOnAttribute(string member, string reason)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public static class Values
            {
                [{|SGIOC004:IocInstance(ServiceTypes = [typeof(object)])|}]
                {{member}}
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC004")).EndsWith($"' cannot be a service type of 'App.Values.{member.Split(' ')[4]}': {reason}");
    }

    [Test]
    public async Task SGIOC006_WrapperMemberTypeWithoutServiceTypes_Reported()
    {
        await RunAsync(Usings + """
            public interface IClock;

            public static class Values
            {
                [{|SGIOC006:IocInstance|}]
                public static readonly Lazy<IClock> Clock = null!;
            }
            """);
    }

    [Test]
    public async Task SGIOC026_InstanceMember_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            public class Clocks
            {
                [{|SGIOC026:IocInstance|}]
                public IClock Utc { get; } = new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC026")).IsEqualTo("'App.Clocks.Utc' cannot be an instance: it is not static");
    }

    [Test]
    public async Task SGIOC026_OpenGenericContainingType_Reported()
    {
        GeneratorTestResult result = await RunAsync(Header + """
            [assembly: {|SGIOC026:IocInstanceFor(nameof(App.Clocks<>.Utc))|}]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks<T>
            {
                public static readonly IClock Utc = new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC026")).EndsWith("cannot be an instance: its containing type is an open generic");
    }

    [Test]
    public async Task SGIOC026_FileType_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            file static class Clocks
            {
                [{|SGIOC026:IocInstance|}]
                public static readonly IClock Utc = new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC026")).EndsWith("cannot be an instance: it uses a file type");
    }

    [Test]
    [Arguments("\"Utc\"")]
    [Arguments("nameof(App.Clocks.Create)")]
    public async Task SGIOC011_InstanceIsNotNameofOfFieldOrProperty_ReportedOnArgument(string argument)
    {
        GeneratorTestResult result = await RunAsync(Header + $$"""
            [assembly: IocInstanceFor({|SGIOC011:{{argument}}|})]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks
            {
                public static readonly IClock Utc = new SystemClock();
                public static IClock Create() => new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC011")).IsEqualTo("Instance must be a nameof() expression that refers to a field or property");
    }

    [Test]
    public async Task SGIOC025_InstanceForOnPlainClass_Reported()
    {
        await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            [{|SGIOC025:IocInstanceFor(nameof(Utc))|}]
            public static class Clocks
            {
                public static readonly IClock Utc = new SystemClock();
            }
            """);
    }

    [Test]
    [Arguments("[{|SGIOC026:IocInstance|}] public IClock Utc { get; } = new SystemClock();")]
    [Arguments("[{|SGIOC004:IocInstance(ServiceTypes = [typeof(IClock)])|}] public static readonly int Count = 1;")]
    public async Task InvalidInstance_SkippedAsAnErrorNode_DependencyIsNotReportedAgain(string member)
    {
        // Only the marked diagnostic is reported (Diagnostics.spec, "Consequences")
        await RunAsync(Usings + $$"""
            public interface IClock;
            public sealed class SystemClock : IClock;

            public class Clocks
            {
                {{member}}
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(IClock clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);
    }

    [Test]
    public async Task SGIOC004_ValueTypeWithoutServiceTypes_Reported()
    {
        await RunAsync(Usings + """
            public static class Values
            {
                [{|SGIOC004:IocInstance|}]
                public static readonly int Count = 1;
            }
            """);
    }

    [Test]
    public async Task Instance_ClosedGenericContainingType_IsValid()
    {
        // Near SGIOC026: a closed containing type can be named
        GeneratorTestResult result = await RunAsync(Header + """
            [assembly: IocInstanceFor(nameof(App.Clocks<int>.Utc))]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks<T>
            {
                public static readonly IClock Utc = new SystemClock();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("global::App.Clocks<global::System.Int32>.Utc");
    }

    [Test]
    public async Task Instance_InstanceForOnContainerClass_IsValid()
    {
        // Near SGIOC025: the attribute is on a container class
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IClock clock);

            [IocContainer(IntegrateServiceProvider = false)]
            [IocInstanceFor(nameof(Utc))]
            public partial class AppContainer
            {
                public static readonly IClock Utc = new SystemClock();
            }
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task Instance_UnrelatedEdit_DoesNotRunDeclarationsAgain()
    {
        GeneratorTestResult first = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]", "namespace App; public class Unrelated;"],
            Options with { TrackSteps = true });

        GeneratorTestResult second = IncrementalTestHelper.RunAgainWithEdit(first, 2, "namespace App; public class Unrelated { public int X; }");

        await IncrementalTestHelper.VerifyNotRunAgainAsync(second, TrackingNames.Declarations);
        await Assert.That(IncrementalTestHelper.FindForbiddenValues(first, TrackingNames.Declarations)).IsEmpty();
    }

    [Test]
    public async Task Instance_AttributeMoves_DeclarationsAreUnchanged()
    {
        // A change of position alone stops at the declarations (Diagnostics.spec, "Two pipelines")
        GeneratorTestResult first = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]"],
            Options with { TrackSteps = true });

        GeneratorTestResult second = IncrementalTestHelper.RunAgainWithEdit(first, 0, Services.Replace("    [IocInstance]", "\n    [IocInstance]"));

        await IncrementalTestHelper.VerifyNotRunAgainAsync(second, TrackingNames.Declarations);
    }

    [Test]
    public async Task Instance_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]"]);
}
