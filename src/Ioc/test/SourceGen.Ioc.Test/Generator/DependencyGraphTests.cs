using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// The checks of the dependency graph (Analysis.Model.spec, "Two levels of checks", "Circular dependencies",
/// "Lifetime checks", "Warning for a held Transient", "Disposable Transient").
/// </summary>
public class DependencyGraphTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        """;

    private const string Container = """

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

    private static bool HasSource(GeneratorTestResult result, string name) => result.GeneratedSources.Any(s => s.HintName.Contains(name, StringComparison.Ordinal));

    [Test]
    public async Task Cycle_TwoServices_ReportedOnceAtTheSiteOfTheFirstDefinition()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class A({|SGIOC301:B b|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class B(A a);
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.A → App.B → App.A");
    }

    [Test]
    public async Task Cycle_SelfDependency_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            public interface IFoo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)])]
            public sealed class Composite({|SGIOC301:IFoo inner|}) : IFoo;
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.Composite → App.Composite");
    }

    [Test]
    public async Task Cycle_WithContainer_NotReportedAgainAndNotGenerated()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class A({|SGIOC301:B b|});

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class B(A a);
            """ + Container);

        await Assert.That(HasSource(result, "AppContainer")).IsFalse();
    }

    [Test]
    public async Task Cycle_ServiceCollectionExtension_NotGenerated()
    {
        GeneratorTestResult result = await RunAsync(
            Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class A({|SGIOC301:B b|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class B(A a);
            """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await Assert.That(HasSource(result, "ServiceCollectionExtensions")).IsFalse();
    }

    [Test]
    public async Task Captive_SingletonOnScoped_ReportedAtTheSite()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC302:Session session|});

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;
            """ + Container);

        await Assert.That(Message(result, "SGIOC302")).IsEqualTo("Singleton 'App.Cache' depends on scoped 'App.Session'");
        await Assert.That(HasSource(result, "AppContainer")).IsFalse();
    }

    [Test]
    public async Task Captive_ThroughTransient_ReportsThePathAndTheHeldTransient()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC302,SGIOC502:Reader reader|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Reader(Session session);

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;
            """);

        await Assert.That(Message(result, "SGIOC302"))
            .IsEqualTo("Singleton 'App.Cache' depends on scoped 'App.Session' through App.Cache → App.Reader → App.Session");
    }

    [Test]
    public async Task HeldTransient_SingletonAndScoped_WarnAndStillGenerate()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            public interface IFormatter;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IFormatter)])]
            public sealed class Formatter : IFormatter;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Printer({|SGIOC502:IFormatter formatter|});

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Report({|SGIOC503:Formatter formatter|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Draft(Formatter formatter);
            """ + Container);

        await Assert.That(Message(result, "SGIOC502"))
            .IsEqualTo("Singleton 'App.Printer' holds transient 'App.Formatter' for the lifetime of the container; depend on Func<App.IFormatter> to create instances on demand");
        await Assert.That(Message(result, "SGIOC503"))
            .IsEqualTo("Scoped 'App.Report' holds transient 'App.Formatter' for the lifetime of the scope; depend on Func<App.Formatter> to create instances on demand");
        await Assert.That(HasSource(result, "AppContainer")).IsTrue();
    }

    [Test]
    [Arguments("IDisposable", "public void Dispose() { }", "IDisposable")]
    [Arguments("IAsyncDisposable", "public System.Threading.Tasks.ValueTask DisposeAsync() => default;", "IAsyncDisposable")]
    [Arguments("IDisposable, IAsyncDisposable", "public void Dispose() { } public System.Threading.Tasks.ValueTask DisposeAsync() => default;", "IDisposable, IAsyncDisposable")]
    public async Task DisposableTransient_Container_ReportedOnContainerAndNotGenerated(string interfaces, string members, string kind)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            namespace App;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Connection : {{interfaces}} { {{members}} }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC403:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC403"))
            .IsEqualTo($"Transient 'App.Connection' is disposable ({kind}); the container does not track transient services");
        await Assert.That(HasSource(result, "AppContainer")).IsFalse();
    }

    [Test]
    public async Task DisposableTransient_NoContainer_NotReported()
    {
        GeneratorTestResult result = await RunAsync(
            Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Connection : IDisposable { public void Dispose() { } }
            """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await Assert.That(HasSource(result, "ServiceCollectionExtensions")).IsTrue();
    }

    /// <summary>Runs with a library whose types have no source: <c>Lib.External(Lib.Helper helper)</c>.</summary>
    private static async Task<GeneratorTestResult> RunWithLibraryAsync(string source)
    {
        ReferencedAssembly library = SourceGeneratorTestHelper.CompileReference<IocSourceGenerator>(
            "namespace Lib; public sealed class Helper; public sealed class External(Helper helper);",
            new() { AssemblyName = "Lib" });
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(source);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, new() { References = [library.Image] });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    [Test]
    public async Task Captive_SiteInMetadata_ReportedAtTheAttribute()
    {
        GeneratorTestResult result = await RunWithLibraryAsync(Usings + """
            [assembly: {|SGIOC302:IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)|}]
            [assembly: IocRegisterFor(typeof(Lib.Helper), ServiceLifetime.Scoped)]
            [assembly: IocServiceCollectionExtensions(Name = "App")]
            """);

        await Assert.That(HasSource(result, "ServiceCollectionExtensions")).IsFalse();
    }

    [Test]
    public async Task HeldTransient_SiteInMetadataWithContainer_ReportedOnlyAtTheAttribute()
    {
        GeneratorTestResult result = await RunWithLibraryAsync(Usings + """
            [assembly: {|SGIOC502:IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)|}]
            [assembly: IocRegisterFor(typeof(Lib.Helper), ServiceLifetime.Transient)]

            namespace App;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(HasSource(result, "AppContainer")).IsTrue();
    }

    [Test]
    [Arguments("Singleton", "SGIOC502")]
    [Arguments("Scoped", "SGIOC503")]
    public async Task HeldTransient_SiteInMetadataWithoutContainer_ReportedAtTheAttribute(string lifetime, string id)
    {
        GeneratorTestResult result = await RunWithLibraryAsync(Usings + $$"""
            [assembly: {|{{id}}:IocRegisterFor(typeof(Lib.External), ServiceLifetime.{{lifetime}})|}]
            [assembly: IocRegisterFor(typeof(Lib.Helper), ServiceLifetime.Transient)]
            [assembly: IocServiceCollectionExtensions(Name = "App")]
            """);

        await Assert.That(HasSource(result, "ServiceCollectionExtensions")).IsTrue();
    }

    [Test]
    [Arguments("Transient")]
    [Arguments("Scoped")]
    public async Task ContainerClass_AsDependency_IsNotANode(string lifetime)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Holder(AppContainer container);

            [IocRegister(ServiceLifetime.{{lifetime}})]
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(HasSource(result, "AppContainer.g.cs")).IsTrue();
    }

    [Test]
    [Arguments("Scoped", "SGIOC302")]
    [Arguments("Transient", "SGIOC502")]
    public async Task ContainerClass_RegisteredInAnotherContainer_ReportedOnThatContainer(string lifetime, string id)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Holder(FirstContainer container);

            [IocRegister(ServiceLifetime.{{lifetime}})]
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class FirstContainer;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|{{id}}:SecondContainer|};
            """);

        await Assert.That(HasSource(result, "FirstContainer.g.cs")).IsTrue();
    }

    [Test]
    public async Task ContainerClass_SecondCaptiveAtTheSameSite_ReportedOnThatContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Holder({|SGIOC302,SGIOC502:Reader reader|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Reader(Session session, FirstContainer container);

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Scoped)]
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class FirstContainer;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC302:SecondContainer|};
            """);

        string[] messages = [.. result.ReportedDiagnostics.Where(static d => d.Id == "SGIOC302").Select(static d => d.GetMessage(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal)];
        await Assert.That(messages).IsEquivalentTo([
            "Singleton 'App.Holder' depends on scoped 'App.FirstContainer' through App.Holder → App.Reader → App.FirstContainer",
            "Singleton 'App.Holder' depends on scoped 'App.Session' through App.Holder → App.Reader → App.Session"]);
    }

    [Test]
    public async Task Cycle_ThroughAmbiguousDependency_NotAnEdge()
        => await RunAsync(Usings + """
            namespace App;

            public interface IFoo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class A(IFoo foo);

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IFoo)])]
            public sealed class B(A a) : IFoo;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IFoo)])]
            public sealed class C : IFoo;
            """);

    [Test]
    public async Task Cycle_OfTransientsHeldBySingleton_ReportedOnceWithoutHang()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Root({|SGIOC502:First first|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class First({|SGIOC301:Second second|});

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Second(First first);
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.First → App.Second → App.First");
    }

    [Test]
    public async Task Cycle_TwoComponents_ReportedInOrder()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Transient)] public sealed class D(C c);
            [IocRegister(ServiceLifetime.Transient)] public sealed class C({|SGIOC301:D d|});
            [IocRegister(ServiceLifetime.Transient)] public sealed class A({|SGIOC301:B b|});
            [IocRegister(ServiceLifetime.Transient)] public sealed class B(A a);
            """);

        string[] messages = [.. result.ReportedDiagnostics.Where(static d => d.Id == "SGIOC301").Select(static d => d.GetMessage(CultureInfo.InvariantCulture))];
        await Assert.That(messages).IsEquivalentTo(["Circular dependency: App.A → App.B → App.A", "Circular dependency: App.C → App.D → App.C"]);
    }

    [Test]
    public async Task Captive_ThroughSingleton_ReportedOnlyForTheSecondSingleton()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Outer(Inner inner);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Inner({|SGIOC302:Session session|});

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;
            """);

        await Assert.That(Message(result, "SGIOC302")).IsEqualTo("Singleton 'App.Inner' depends on scoped 'App.Session'");
    }

    [Test]
    public async Task Graph_Valid_NoDiagnostic()
        => await RunAsync(Usings + """
            namespace App;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Clock;

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session(Clock clock);

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Handler(Session session, Clock clock);
            """ + Container);
}
