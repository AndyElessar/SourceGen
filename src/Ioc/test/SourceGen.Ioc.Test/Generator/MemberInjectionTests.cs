using System.Text.RegularExpressions;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Member injection with <c>[IocInject]</c> (Semantics.spec, "Member injection"; Annotations.spec, "Injected members",
/// "Checks of an injection method", "Order"; Container.Output.spec and ServiceCollection.Output.spec, "Failure during
/// creation").
/// </summary>
public partial class MemberInjectionTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Services = """
        public interface IClock;
        public interface IRepo;
        public interface ILog;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        public sealed class SystemClock : IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "main")]
        public sealed class MainRepo : IRepo;

        public sealed class NullLog : ILog;

        """;

    private const string Service = Usings + Services + """
        public abstract class ServiceBase
        {
            [IocInject] public IClock BaseClock { get; set; }
            [IocInject] public virtual IClock? Virtual { get; set; }
            [IocInject] public IClock Hidden { get; set; } = null!;
            [IocInject] public void InitBase(IClock clock) { }
        }

        [IocRegister(ServiceLifetime.Singleton, Key = "svc")]
        public sealed class Service(IClock clock) : ServiceBase, IDisposable
        {
            [IocInject] public IClock Field;
            [IocInject] public required IClock Required { get; set; }
            [IocInject] public IClock Init { get; init; } = null!;
            [IocInject(Key = "main")] public IRepo Repo { get; set; }
            [IocInject] public IRepo? Nullable { get; set; } = null;
            [IocInject] public ILog Log { get; set; } = new NullLog();
            public override IClock? Virtual { get; set; }
            public new IClock Hidden { get; set; } = null!;
            [IocInject] public void Initialize(IClock clock, [ServiceKey] string key, IRepo? repo = null) { }
            [IocInject] public void @event() { }
            public void Dispose() { }
        }
        """;

    private static async Task<GeneratorTestResult> RunAsync(string source, GeneratorRunOptions? options = null, string? extra = null)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = extra is null ? DiagnosticMarkup.Parse(source) : DiagnosticMarkup.Parse(source, extra);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, options);
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static readonly GeneratorRunOptions ExtensionOptions = new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } };

    private const string ExtensionAttribute = "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]";

    [GeneratedRegex(@"__Create_Service_[0-9a-f]+")]
    private static partial Regex ServiceCreator { get; }

    [Test]
    public async Task MemberInjection_Container_InjectsInTheOrderOfTheSteps()
    {
        GeneratorTestResult result = await RunAsync(Service + """

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Members(source, ServiceCreator.Match(source).Value));
    }

    [Test]
    public async Task MemberInjection_ContainerWithFallbackProvider_KeepsTheInitializerWhenMissing()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public interface ILog;
            public sealed class NullLog : ILog;

            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Service
            {
                [IocInject] public ILog Log { get; set; } = new NullLog();
                [IocInject] public int Retries { get; set; } = 3;
                [IocInject(Key = "a")] public ILog Keyed { get; set; } = new NullLog();
                [IocInject] public IClock Clock { get; set; }
            }

            [IocContainer]
            public partial class {|SGIOC501:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Members(source, ServiceCreator.Match(source).Value));
    }

    [Test]
    public async Task MemberInjection_ServiceCollectionExtension_InjectsInTheOrderOfTheSteps()
    {
        GeneratorTestResult result = await RunAsync(Service, ExtensionOptions, ExtensionAttribute);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Verify(SnapshotHelper.Members(source, ServiceCreator.Match(source).Value, "__DisposeLost"));
    }

    [Test]
    public async Task MemberInjection_NotDisposable_NoTryCatch()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class SystemClock : IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public void Initialize(IClock clock) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """, ExtensionOptions, ExtensionAttribute);

        await result.VerifyCompilableAsync();
        foreach(string file in (string[])["AppContainer.g.cs", "AppServiceCollectionExtensions.g.cs"])
        {
            string source = result.GetGeneratedSource(file)!;
            string creator = SnapshotHelper.Members(source, ServiceCreator.Match(source).Value);
            await Assert.That(creator).Contains("Initialize(").And.DoesNotContain("catch");
        }
    }

    [Test]
    public async Task MemberInjection_TypeFromMetadata_InitializerIsNotKept()
    {
        // An initializer counts only for a member declared in the current compilation (Constitution, "Explicitness")
        ReferencedAssembly library = SourceGeneratorTestHelper.CompileReference<IocSourceGenerator>(
            """
            namespace Lib;
            public interface ILog;
            public sealed class NullLog : ILog;
            public sealed class External
            {
                [SourceGen.Ioc.IocInject] public ILog Log { get; set; } = new NullLog();
            }
            """,
            new() { AssemblyName = "Lib" });
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse("""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]

            namespace App;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, new() { References = [library.Image] });
        await result.VerifyDiagnosticsAsync(expected);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    private const string LibraryBase = """
        namespace Lib;
        public interface ILog;
        public sealed class NullLog : ILog;
        public abstract class LibBase
        {
            [SourceGen.Ioc.IocInject] public ILog Log { get; set; } = new NullLog();
            [SourceGen.Ioc.IocInject] public int Init() => 0;
        }
        """;

    /// <summary>Runs <paramref name="source"/> with <c>Lib</c> as a DLL (command line) and as a project reference (IDE).</summary>
    private static (GeneratorTestResult Image, GeneratorTestResult Project) RunWithLibrary(string library, string source)
    {
        ReferencedAssembly reference = SourceGeneratorTestHelper.CompileReference<IocSourceGenerator>(library, new() { AssemblyName = "Lib" });
        return (
            SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>([source], new() { References = [reference.Image] }),
            SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>([source], new() { References = [reference.CompilationReference] }));
    }

    [Test]
    public async Task MemberInjection_BaseClassFromAnotherProject_InitializerIsNotKeptInBothBuilds()
    {
        (GeneratorTestResult image, GeneratorTestResult project) = RunWithLibrary(LibraryBase.Replace("[SourceGen.Ioc.IocInject] public int Init() => 0;", string.Empty), """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            namespace App;
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Impl : Lib.LibBase;
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        foreach(GeneratorTestResult result in (GeneratorTestResult[])[image, project])
        {
            await Assert.That(result.Diagnostics.Select(static d => d.Id)).IsEquivalentTo(["SGIOC402"]);
            await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
        }
    }

    [Test]
    public async Task MemberInjection_BaseClassInThisCompilation_InitializerIsKept()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface ILog;
            public sealed class NullLog : ILog;
            public abstract class LocalBase
            {
                [IocInject] public ILog Log { get; set; } = new NullLog();
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Impl : LocalBase;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("instance.Log");
    }

    [Test]
    public async Task SGIOC016_MethodOfAnotherProject_ReportedAtTheAttributeInBothBuilds()
    {
        const string Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            namespace App;
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Impl : Lib.LibBase;
            """;
        (GeneratorTestResult image, GeneratorTestResult project) = RunWithLibrary(LibraryBase, Source);

        int start = Source.IndexOf("IocRegister(", StringComparison.Ordinal);
        int end = Source.IndexOf(")]", start, StringComparison.Ordinal) + 1;
        foreach(GeneratorTestResult result in (GeneratorTestResult[])[image, project])
        {
            Microsoft.CodeAnalysis.Diagnostic diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("SGIOC016");
            await Assert.That(diagnostic.Location.SourceSpan).IsEqualTo(new Microsoft.CodeAnalysis.Text.TextSpan(start, end - start));
        }
    }

    [Test]
    [Arguments("= null!;")]
    [Arguments("= default!;")]
    [Arguments("= default(IClock)!;")]
    [Arguments("= (null!);")]
    [Arguments("= (null)!;")]
    [Arguments("= ((default))!;")]
    public async Task SGIOC402_NullForgivingInitializer_MemberIsRequired(string initializer)
    {
        // Such an initializer only suppresses the nullable warning (ADR 2026112503)
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public IClock Property { get; set; } {{initializer}}
                [IocInject] public IClock Field {{initializer}}
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402,SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC402_NullableMemberWithoutInitializer_Required()
    {
        // A nullable annotation does not make a member optional (ADR 2026112504)
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public IClock? Property { get; set; }
                [IocInject] public IClock? Field;
                [IocInject] public IClock? Kept { get; set; } = null;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402,SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task NullForgivingInitializer_FallbackAndServiceCollectionExtension_ResolveAsRequired()
    {
        const string Source = Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public IClock Clock { get; set; } = null!;
            }

            [IocContainer]
            public partial class {|SGIOC501:AppContainer|};
            """;
        GeneratorTestResult result = await RunAsync(Source, ExtensionOptions, ExtensionAttribute);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs"))
            .Contains("instance.Clock = (global::App.IClock)global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService(c.__Fallback(), typeof(global::App.IClock));");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"))
            .Contains("instance.Clock = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.IClock>(sp);");
    }

    [Test]
    [Arguments("= new SystemClock();")]
    [Arguments("= SystemClock.Default!;")]
    public async Task MemberInjection_RealInitializer_KeepsTheInitializer(string initializer)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IClock;
            public sealed class SystemClock : IClock { public static SystemClock? Default; }

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public IClock Property { get; set; } {{initializer}}
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNotNull();
    }

    [Test]
    public async Task SGIOC302_MemberSiteOfAnotherProject_ReportedAtTheAttributeInBothBuilds()
    {
        const string Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            [assembly: IocRegisterFor(typeof(Lib.Helper), ServiceLifetime.Scoped)]
            namespace App;
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Impl : Lib.LibBase;
            """;
        (GeneratorTestResult image, GeneratorTestResult project) = RunWithLibrary(
            "namespace Lib; public sealed class Helper; public abstract class LibBase { [SourceGen.Ioc.IocInject] public void Init(Helper helper) { } }",
            Source);

        int start = Source.IndexOf("IocRegister(ServiceLifetime.Singleton)", StringComparison.Ordinal);
        foreach(GeneratorTestResult result in (GeneratorTestResult[])[image, project])
        {
            Microsoft.CodeAnalysis.Diagnostic diagnostic = result.Diagnostics.Single();
            await Assert.That(diagnostic.Id).IsEqualTo("SGIOC302");
            await Assert.That(diagnostic.Location.SourceSpan.Start).IsEqualTo(start);
        }
    }

    [Test]
    public async Task SGIOC402_RequiredMemberMissing_NamesTheMember()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public IClock Clock { get; set; }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(result.Diagnostics.Single().GetMessage(System.Globalization.CultureInfo.InvariantCulture)).Contains("App.Service → Clock");
    }

    [Test]
    public async Task SGIOC302_CaptiveThroughMember_ReportedAtTheMember()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service
            {
                [IocInject] public void Initialize({|SGIOC302:Session session|}) { }
            }
            """, ExtensionOptions, ExtensionAttribute);

        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC301_CycleThroughProperty_ReportedAtTheProperty()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class A
            {
                {|SGIOC301:[IocInject] public B B { get; set; }|}
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class B(A a);
            """, ExtensionOptions, ExtensionAttribute);

        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).IsNull();
    }

    [Test]
    [Arguments("public void {|SGIOC016:Init|}<T>() { }")]
    [Arguments("public void {|SGIOC016:Init|}(ref int value) { }")]
    [Arguments("public void {|SGIOC016:Init|}(out int value) { value = 0; }")]
    [Arguments("public void {|SGIOC016:Init|}(in int value) { }")]
    [Arguments("public int {|SGIOC016:Init|}() => 0;")]
    [Arguments("public Task<int> {|SGIOC016:Init|}() => Task.FromResult(0);")]
    [Arguments("public ValueTask {|SGIOC016:Init|}() => default;")]
    [Arguments("public async void {|SGIOC016:Init|}() { await Task.Yield(); }")]
    public async Task SGIOC016_InvalidInjectionMethod_DeclarationSkipped(string method)
    {
        // The skipped declaration is an error node: the dependent User reports no SGIOC402, and the container is not
        // generated (Diagnostics.spec, "Consequences")
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [IocInject] {{method}}
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class User(Consumer consumer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC016_Message_NamesTheMethodAndTheReason()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [IocInject] public int {|SGIOC016:Init|}() => 0;
            }
            """);

        await Assert.That(result.Diagnostics.Single().GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo("Method 'App.Consumer.Init' cannot be injected: the return type must be void or Task");
    }

    [Test]
    public async Task SGIOC016_MethodNotMarked_NotChecked()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                public int Init<T>(ref int value) => 0;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNotNull();
    }

    [Test]
    [Arguments("{|SGIOC019:IocInject(Key = \"a\")|}")]
    [Arguments("{|SGIOC019:IocInject(LookupMode = ServiceKeyLookupMode.InheritKey)|}")]
    public async Task SGIOC019_KeyOnInjectionMethod_DeclarationSkipped(string attribute)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [{{attribute}}] public void Init() { }
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class User(Consumer consumer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    [Arguments("[{|SGIOC017:IocInject(LookupMode = ServiceKeyLookupMode.NullKey, Key = \"a\")|}] public IRepo Repo { get; set; } = null!;", "")]
    [Arguments("[IocInject] public void Init([{|SGIOC018:IocInject|}, FromKeyedServices(\"a\")] IRepo repo) { }", "")]
    [Arguments("[IocInject] public void Init([ServiceKey] string key) { }", "SGIOC023")]
    public async Task MemberKeyError_DeclarationSkipped(string member, string registrationError)
    {
        string registration = "IocRegister(ServiceLifetime.Transient)";
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [{{(registrationError.Length == 0 ? registration : $"{{|{registrationError}:{registration}|}}")}}]
            public sealed class Consumer
            {
                {{member}}
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class User(Consumer consumer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task MemberInjection_Deterministic()
    {
        await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [Service + """

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
                """],
            ExtensionOptions);
    }
}
