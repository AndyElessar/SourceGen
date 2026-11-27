using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Factory registrations (Annotations.spec, "Factory registration", "Service types of a factory", "Checks of a factory or
/// instance member"; Analysis.Model.spec, "Identity", "Disposal kind"; Container.Output.spec, "Resolution of a closed
/// definition"; ServiceCollection.Output.spec, "Creation method").
/// </summary>
public class FactoryTests
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

    private const string Services = Usings + """
        public interface IClock;
        public interface IRepo;
        public interface IHandle : IDisposable;
        public sealed class SystemClock : IClock;
        public sealed class Repo : IRepo;
        public sealed class Handle : IHandle { public void Dispose() { } }

        [IocRegister(ServiceLifetime.Singleton)]
        public sealed class Settings;

        public static class Factories
        {
            [IocFactory(ServiceLifetime.Singleton)]
            public static IClock CreateClock(Settings settings) => new SystemClock();

            [IocFactory(ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepo)], Key = "main")]
            public static Repo CreateRepo([ServiceKey] string key, IClock clock) => new Repo();

            [IocFactory(ServiceLifetime.Singleton)]
            public static IHandle CreateHandle() => new Handle();
        }

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Consumer(IClock clock, [FromKeyedServices("main")] IRepo repo, IHandle handle);

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
    public async Task Factory_Container_CallsTheMethodAndChecksNull()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Verify(SnapshotHelper.MembersByPrefix(result.GetGeneratedSource("AppContainer.g.cs")!, ["__DisposeInstances"], "__Create_Factories_", "__Get_Factories_"));
    }

    [Test]
    public async Task Factory_ServiceCollectionExtension_CallsTheMethodAndChecksNull()
    {
        GeneratorTestResult result = await RunAsync(Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task Factory_DisposableReturnTypeNotKnown_DisposalTestsTheTypeAtRunTime()
    {
        // IClock is an interface: the object can be disposable, so the container tests its type when it disposes it
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).Contains("is global::System.IDisposable");
        await Assert.That(source).Contains("is global::System.IAsyncDisposable");
    }

    [Test]
    public async Task Factory_FactoryFor_ReadsTheMethodFromNameof()
    {
        GeneratorTestResult result = await RunAsync(Header + """
            [assembly: IocFactoryFor(nameof(App.Clocks.Create), ServiceLifetime.Singleton)]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks
            {
                public static IClock Create() => new SystemClock();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("global::App.Clocks.Create()");
    }

    [Test]
    public async Task Factory_ClosedGenericContainingType_IsValid()
    {
        GeneratorTestResult result = await RunAsync(Header + """
            [assembly: IocFactoryFor(nameof(App.Clocks<int>.Create), ServiceLifetime.Singleton)]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Clocks<T>
            {
                public static IClock Create() => new SystemClock();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("global::App.Clocks<global::System.Int32>.Create()");
    }

    [Test]
    public async Task SGIOC402_MissingFactoryParameter_NamesTheFactory()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public interface IRepo;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                [IocFactory(ServiceLifetime.Singleton)]
                public static IClock Create(IRepo repo) => new SystemClock();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC402")).IsEqualTo("'App.Factories.Create(App.IRepo) → repo' requires 'App.IRepo', which is not registered");
    }

    [Test]
    public async Task SGIOC403_TransientFactoryOfDisposableType_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public sealed class Handle : IDisposable { public void Dispose() { } }
            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                [IocFactory(ServiceLifetime.Transient)]
                public static Handle CreateHandle() => new Handle();

                // The disposal kind is "run-time check": not reported, and the container does not track the object
                [IocFactory(ServiceLifetime.Transient)]
                public static IClock CreateClock() => new SystemClock();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC403:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC403")).IsEqualTo("Transient 'App.Factories.CreateHandle()' is disposable (IDisposable); the container does not track transient services");
    }

    [Test]
    public async Task Factory_TwoAttributesOnOneMethod_MergeIntoOneDefinition()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public interface ITimer;
            public sealed class SystemClock : IClock, ITimer;

            public static class Factories
            {
                [IocFactory(ServiceLifetime.Singleton)]
                [IocFactory(ServiceLifetime.Singleton, ServiceTypes = [typeof(ITimer)])]
                public static SystemClock Create() => new SystemClock();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source.Split("global::App.Factories.Create()").Length - 1).IsEqualTo(1);
        await Assert.That(source).Contains("typeof(global::App.ITimer)");
    }

    [Test]
    public async Task SGIOC001_TwoAttributesWithDifferentLifetimes_ReportedOnEach()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                [{|SGIOC001:IocFactory(ServiceLifetime.Singleton)|}]
                [{|SGIOC001:IocFactory(ServiceLifetime.Scoped)|}]
                public static IClock Create() => new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC001")).StartsWith("'App.Factories.Create()' is registered with different Lifetime");
    }

    [Test]
    [Arguments("void", "it is a pointer or void")]
    [Arguments("Task", "it is a Task without a result")]
    [Arguments("ValueTask", "ValueTask is not supported")]
    [Arguments("ValueTask<IClock>", "ValueTask is not supported")]
    [Arguments("int", "it is a value type")]
    [Arguments("KeyValuePair<string, IClock>", "it is a value type")]
    [Arguments("AppContainer", "it is a container class")]
    public async Task SGIOC004_InvalidReturnType_ReportedOnAttribute(string returnType, string reason)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IClock;

            public static class Factories
            {
                [{|SGIOC004:IocFactory(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])|}]
                public static {{returnType}} Create() => throw new NotSupportedException();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC004")).EndsWith($"' cannot be a service type of 'App.Factories.Create()': {reason}");
    }

    [Test]
    [Arguments("[{|SGIOC026:IocFactory(ServiceLifetime.Singleton)|}] public IClock Create() => new SystemClock();")]
    [Arguments("[{|SGIOC004:IocFactory(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])|}] public static void Create() { }")]
    [Arguments("[IocFactory(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock), {|SGIOC006:typeof(Lazy<IClock>)|}])] public static IClock Create() => new SystemClock();")]
    [Arguments("[{|SGIOC023:IocFactory(ServiceLifetime.Singleton)|}] public static IClock Create([ServiceKey] string key) => new SystemClock();")]
    public async Task InvalidFactory_SkippedAsAnErrorNode_DependencyIsNotReportedAgain(string factory)
    {
        // The skipped instance definition stays as an error node: a dependency on it is not a missing dependency
        // (Constitution, "Diagnostic policy"; Diagnostics.spec, "Consequences"). Only the marked diagnostic is reported
        await RunAsync(Usings + $$"""
            public interface IClock;
            public sealed class SystemClock : IClock;

            public class Factories
            {
                {{factory}}
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(IClock clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);
    }

    [Test]
    [Arguments("IServiceProvider", "SGIOC004")]
    [Arguments("Lazy<IClock>", "SGIOC006")]
    public async Task InvalidServiceType_ReturnTypeWithoutServiceTypes_Reported(string returnType, string id)
    {
        await RunAsync(Usings + $$"""
            public interface IClock;

            public static class Factories
            {
                [{|{{id}}:IocFactory(ServiceLifetime.Singleton)|}]
                public static {{returnType}} Create() => throw new NotSupportedException();
            }
            """);
    }

    [Test]
    public async Task SGIOC004_UnboundGenericServiceType_ReportedOnElement()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler<T>;
            public sealed class Handler : IHandler<int>;

            public static class Factories
            {
                [IocFactory(ServiceLifetime.Singleton, ServiceTypes = [{|SGIOC004:typeof(IHandler<>)|}])]
                public static Handler Create() => new Handler();
            }
            """);

        await Assert.That(Message(result, "SGIOC004")).EndsWith("it is an unbound generic type");
    }

    [Test]
    public async Task SGIOC009_GenericFactoryWithServiceTypes_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo<T>;
            public sealed class Repo<T> : IRepo<T>;

            public static class Factories
            {
                [IocFactory(ServiceLifetime.Singleton, {|SGIOC009:ServiceTypes = [typeof(IRepo<int>)]|})]
                public static IRepo<T> Create<T>() => new Repo<T>();
            }
            """);

        await Assert.That(Message(result, "SGIOC009")).IsEqualTo("Generic factory 'App.Factories.Create`1()' always registers its return type; remove ServiceTypes");
    }

    [Test]
    [Arguments("public IClock Create() => new SystemClock();", "it is not static")]
    public async Task SGIOC026_InstanceMethod_Reported(string method, string reason)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IClock;
            public sealed class SystemClock : IClock;

            public class Factories
            {
                [{|SGIOC026:IocFactory(ServiceLifetime.Singleton)|}]
                {{method}}
            }
            """);

        await Assert.That(Message(result, "SGIOC026")).IsEqualTo($"'App.Factories.Create()' cannot be a factory: {reason}");
    }

    [Test]
    [Arguments("file static class Factories { [{|SGIOC026:IocFactory(ServiceLifetime.Singleton)|}] public static IClock Create() => new SystemClock(); }")]
    [Arguments("file static class Outer { public static class Factories { [{|SGIOC026:IocFactory(ServiceLifetime.Singleton)|}] public static IClock Create() => new SystemClock(); } }")]
    public async Task SGIOC026_FileType_Reported(string declarations)
    {
        // The generated file cannot name a file type (Annotations.spec, "Checks of a factory or instance member"). A
        // signature can use a file type only in a file type (CS9051), so the containing type covers the return type
        // and the parameter types too
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IClock;
            public sealed class SystemClock : IClock;
            {{declarations}}

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC026")).EndsWith("cannot be a factory: it uses a file type");
    }

    [Test]
    public async Task SGIOC026_FileTypeArgumentOfTheContainingType_Reported()
    {
        // CS9051 does not apply: Clocks<T> is not a file type, but its closed form names one
        GeneratorTestResult result = await RunAsync(Header + """
            [assembly: {|SGIOC026:IocFactoryFor(nameof(App.Clocks<App.Local>.Create), ServiceLifetime.Singleton, ServiceTypes = [typeof(App.IClock)])|}]
            namespace App;

            public interface IClock;
            file sealed class Local : IClock;

            public static class Clocks<T> where T : new()
            {
                public static T Create() => new T();
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Message(result, "SGIOC026")).EndsWith("cannot be a factory: it uses a file type");
    }

    [Test]
    public async Task SGIOC026_OpenGenericContainingType_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler<T>;
            public sealed class Handler<T> : IHandler<T>;

            public static class Factories<T>
            {
                [{|SGIOC026:IocFactory(ServiceLifetime.Singleton)|}]
                public static IHandler<T> Create() => new Handler<T>();
            }
            """);

        await Assert.That(Message(result, "SGIOC026")).IsEqualTo("'App.Factories<T>.Create()' cannot be a factory: its containing type is an open generic");
    }

    [Test]
    public async Task SGIOC026_FactoryForOpenGenericContainingType_Reported()
    {
        GeneratorTestResult result = await RunAsync(Header + """
            [assembly: {|SGIOC026:IocFactoryFor(nameof(App.Factories<>.Create), ServiceLifetime.Singleton)|}]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories<T>
            {
                public static IClock Create() => new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC026")).EndsWith("its containing type is an open generic");
    }

    [Test]
    public async Task SGIOC010_OverloadedFactory_ReportedOnNameof()
    {
        await RunAsync(Header + """
            [assembly: IocFactoryFor({|SGIOC010:nameof(App.Factories.Create)|}, ServiceLifetime.Singleton)]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                public static IClock Create() => new SystemClock();
                public static IClock Create(int seed) => new SystemClock();
            }
            """);
    }

    [Test]
    [Arguments("\"Create\"")]
    [Arguments("nameof(App.Factories.Field)")]
    public async Task SGIOC011_FactoryIsNotNameofOfMethod_ReportedOnArgument(string argument)
    {
        GeneratorTestResult result = await RunAsync(Header + $$"""
            [assembly: IocFactoryFor({|SGIOC011:{{argument}}|}, ServiceLifetime.Singleton)]
            namespace App;

            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                public static readonly IClock Field = new SystemClock();
                public static IClock Create() => new SystemClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC011")).IsEqualTo("Factory must be a nameof() expression that refers to a method");
    }

    [Test]
    public async Task SGIOC023_ServiceKeyWithoutKey_Reported()
    {
        await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                [{|SGIOC023:IocFactory(ServiceLifetime.Singleton)|}]
                public static IClock Create([ServiceKey] string key) => new SystemClock();
            }
            """);
    }

    [Test]
    public async Task SGIOC025_FactoryForOnPlainClass_Reported()
    {
        await RunAsync(Usings + """
            public interface IClock;
            public sealed class SystemClock : IClock;

            [{|SGIOC025:IocFactoryFor(nameof(Create), ServiceLifetime.Singleton)|}]
            public static class Factories
            {
                public static IClock Create() => new SystemClock();
            }
            """);
    }

    [Test]
    public async Task Factory_GenericOrAsynchronous_SkippedUntilLaterSlices()
    {
        // A generic factory comes with open generics, and Task<T> with asynchronous services. Until then they are skipped
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo<T>;
            public sealed class Repo<T> : IRepo<T>;
            public interface IClock;
            public sealed class SystemClock : IClock;

            public static class Factories
            {
                [IocFactory(ServiceLifetime.Singleton)]
                public static IRepo<T> Create<T>() => new Repo<T>();

                [IocFactory(ServiceLifetime.Singleton)]
                public static Task<IClock> CreateClock() => Task.FromResult<IClock>(new SystemClock());
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("Factories");
    }

    [Test]
    public async Task Factory_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [DiagnosticMarkup.Parse(Services + """
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
                """).Sources[0]]);
}
