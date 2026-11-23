using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// The container without a fallback provider (Container.Output.spec): class, root and scope, resolvers, lookup table,
/// disposal, and the full check of single dependencies.
/// </summary>
public class ContainerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        """;

    private const string Services = Usings + """
        namespace App;

        public interface IClock;
        public interface IOrderService;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        internal sealed class SystemClock : IClock, IDisposable
        {
            public void Dispose() { }
        }

        [IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IOrderService)])]
        internal sealed class OrderService(IClock clock, Formatter formatter) : IOrderService, IAsyncDisposable
        {
            public ValueTask DisposeAsync() => default;
        }

        [IocRegister(ServiceLifetime.Transient)]
        internal sealed class Formatter(IServiceProvider provider, AppContainer container);

        [IocContainer(IntegrateServiceProvider = false)]
        public partial class AppContainer;
        """;

    private static async Task<GeneratorTestResult> RunAsync(params string[] sources)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(sources);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed);
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static string Message(GeneratorTestResult result, string id) => result.ReportedDiagnostics.First(d => d.Id == id).GetMessage(CultureInfo.InvariantCulture);

    [Test]
    public async Task Container_ThreeLifetimes_GeneratesResolversLookupAndDisposal()
    {
        GeneratorTestResult result = await RunAsync(Services);

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppContainer.g.cs"));
    }

    [Test]
    public async Task Container_NestedInNonGenericTypeAndGlobalNamespace_Compiles()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service;

            public static partial class Outer
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public sealed partial class Inner;
            }
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("Outer.Inner.g.cs")).Contains("partial class Inner");
    }

    [Test]
    [Arguments("public partial class Bad<T>;", "it is generic")]
    [Arguments("public partial class Outer<T> { [IocContainer(IntegrateServiceProvider = false)] public partial class Bad; }", "it is nested in a generic type")]
    public async Task SGIOC107_GenericContainer_ReportedAndNotGenerated(string declaration, string reason)
    {
        string source = declaration.Contains("Outer", StringComparison.Ordinal)
            ? declaration.Replace("[IocContainer(", "[{|SGIOC107:IocContainer(").Replace("= false)]", "= false)|}]")
            : "[{|SGIOC107:IocContainer(IntegrateServiceProvider = false)|}]\n" + declaration;

        GeneratorTestResult result = await RunAsync(Usings + "namespace App;\n" + source);

        await Assert.That(Message(result, "SGIOC107")).EndsWith($"cannot be a container: {reason}");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC402_MissingDependency_ReportedOnContainerAndNotGenerated()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Service(IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC402")).IsEqualTo("'App.Service → repo' requires 'App.IRepo', which is not registered");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC401_AmbiguousDependency_ReportedWithCandidates()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class RepoB : IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class RepoA : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Service(IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC401:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC401"))
            .IsEqualTo("'App.Service → repo' requires a single 'App.IRepo' but 2 registrations provide it: App.RepoA, App.RepoB; use a key or depend on a collection");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task Container_AmbiguousServiceTypeWithoutDependency_CompilesAndThrowsOnLookup()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class RepoA : IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class RepoB : IRepo;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).Contains("typeof(global::App.IRepo)");
        await Assert.That(source).Contains("typeof(global::System.Collections.Generic.IEnumerable<global::App.IRepo>)");
    }

    [Test]
    public async Task Container_ExplicitInterfaceDisposal_Compiles()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class SyncService : IDisposable { void IDisposable.Dispose() { } }

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class AsyncService : IAsyncDisposable { ValueTask IAsyncDisposable.DisposeAsync() => default; }

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class BothService : IDisposable, IAsyncDisposable
            {
                void IDisposable.Dispose() { }
                ValueTask IAsyncDisposable.DisposeAsync() => default;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).Contains("((global::System.IDisposable)");
        await Assert.That(source).Contains("await ((global::System.IAsyncDisposable)");
    }

    [Test]
    [Arguments("IRepo")]
    [Arguments("Repo")]
    public async Task Container_DependencyOnSkippedDefinition_NoOtherDiagnostic(string dependency)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            namespace App;

            public interface IRepo;

            [{|SGIOC002:IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])|}]
            public sealed class Repo : IRepo { public Repo(int a) { } public Repo(string b) { } }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Service({{dependency}} repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GeneratedSources.Any(static s => s.HintName.Contains("AppContainer", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task Container_DependencyOnServiceTypeOfDefinitionWithInvalidServiceType_NoOtherDiagnostic()
        => await RunAsync(Usings + """
            namespace App;

            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo), {|SGIOC004:typeof(int)|}])]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Service(IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

    [Test]
    public async Task Container_BuiltInServicesAsParameters_ResolveSelfAndRoot()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class UsesBuiltIns(
                IServiceScopeFactory scopes,
                IServiceProviderIsService isService,
                in IServiceProvider provider,
                ref readonly AppContainer container);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).Contains("(global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)c.__singletons.Root");
        await Assert.That(source).Contains("global::System.IServiceProvider arg2 = (global::System.IServiceProvider)c;");
        await Assert.That(source).Contains("in arg3);");
    }

    [Test]
    public async Task Container_AndAddMethod_BothGenerated()
    {
        GeneratorTestResult result = await RunAsync(Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Assert.That(result.GeneratedSources.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Container_SourcesInAnyOrder_GeneratesSameOutput()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [Services, Usings + "namespace App; [IocRegister(ServiceLifetime.Scoped)] internal sealed class Another : IDisposable { public void Dispose() { } }"]);

    [Test]
    public async Task Container_UnrelatedEdit_DoesNotRunAnalysisAgain()
    {
        GeneratorTestResult first = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>([Services, "namespace App; public class Unrelated;"], new() { TrackSteps = true });

        GeneratorTestResult second = IncrementalTestHelper.RunAgainWithEdit(first, 1, "namespace App; public class Unrelated { public int X; }");

        await IncrementalTestHelper.VerifyNotRunAgainAsync(second, TrackingNames.Analysis, TrackingNames.Views);
        await Assert.That(IncrementalTestHelper.FindForbiddenValues(first, TrackingNames.Views)).IsEmpty();
    }
}
