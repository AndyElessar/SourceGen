using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Collections whose element is the service itself, and the grammar of wrapper types (Analysis.Model.spec, "Grammar of
/// wrapper types", "Multiple services", "Circular dependencies"; Container.Output.spec, "Collections and dictionaries";
/// ServiceCollection.Output.spec, "Wrapper types and collections").
/// </summary>
public class CollectionTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Handlers = Usings + """
        public interface IHandler;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
        public sealed class FirstHandler : IHandler;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
        public sealed class SecondHandler : IHandler;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "k")]
        public sealed class KeyedHandler : IHandler;

        [IocRegister(ServiceLifetime.Scoped)]
        public sealed class Consumer(
            IEnumerable<IHandler> all,
            IReadOnlyList<IHandler> list,
            IHandler[] array,
            [IocInject(Key = "k")] ICollection<IHandler> keyed,
            [IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] IList<IHandler> anyKey,
            IEnumerable<IUnknown>? unknown = null);

        public interface IUnknown;

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

    private static IEnumerable<string> Messages(GeneratorTestResult result, string id)
        => result.ReportedDiagnostics.Where(d => d.Id == id).Select(static d => d.GetMessage(CultureInfo.InvariantCulture));

    [Test]
    public async Task Collection_Container_ResolvesEachCollectionTypeAsAnArray()
    {
        GeneratorTestResult result = await RunAsync(Handlers + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IReadOnlyCollection<IHandler> Handlers { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["__Singletons", "Handlers", "__lookup", "__collections", "__BuildKeyedLookup"], "__Create_Consumer_", "__All_", "__CreateAll_"));
    }

    [Test]
    public async Task Collection_ContainerWithFallbackProvider_MergesEachCollection()
    {
        GeneratorTestResult result = await RunAsync(Handlers + """
            [IocContainer]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, [], "__Create_Consumer_", "__All_"));
    }

    [Test]
    public async Task Collection_ContainerAndServiceCollectionExtension_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [
                Handlers + """
                    [IocContainer]
                    public partial class AppContainer;
                    """,
                "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]",
            ],
            new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } });

    [Test]
    public async Task Collection_TransientOrScopedElement_CachedByLifetime()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IStep;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStep)])]
            public sealed class SingletonStep : IStep;

            [IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IStep)], Key = "scoped")]
            public sealed class ScopedStep : IStep;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IStep)], Key = "transient")]
            public sealed class TransientStep : IStep;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                [IocInject(Key = "scoped")]
                public partial IEnumerable<IStep> Scoped { get; }

                [IocInject(Key = "transient")]
                public partial IEnumerable<IStep> Transient { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["__Singletons"], "__all_", "__All_", "__CreateAll_"));
    }

    [Test]
    public async Task Collection_ServiceCollectionExtension_UsesTheProviderAndRegistersOtherCollectionTypes()
    {
        GeneratorTestResult result = await RunAsync(Handlers, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["AddApp"], "__Create_Consumer_"));
    }

    [Test]
    public async Task Collection_OptionalAndKeptInitializer_AlwaysAssigned()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [IocInject]
                public IEnumerable<IHandler> Handlers { get; set; } = [];
            }

            [IocContainer]
            public partial class AppContainer;
            """, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("instance.Handlers = (global::System.Collections.Generic.IEnumerable<global::App.IHandler>)c.__All_IHandler_");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"))
            .Contains("instance.Handlers = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<global::App.IHandler>(sp);");
    }

    [Test]
    public async Task KeyedCollection_OnlyASiteAsksForTheKey_AddedWithoutTheEqualKeyCheck()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            public static class Keys
            {
                public static readonly string A = "a";
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "a")]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IEnumerable<IRepo> repos);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC505:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Members(source, "__BuildKeyedLookup", "__TryAddKeyed"));
    }

    [Test]
    public async Task SGIOC301_CompositeDependsOnItsOwnCollection_ReportedByTheFullCheck()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
            public sealed class Composite(IEnumerable<IHandler> handlers) : IHandler;

            [IocContainer]
            public partial class {|SGIOC301:AppContainer|};
            """, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await Assert.That(Messages(result, "SGIOC301").Single()).IsEqualTo("Circular dependency: App.Composite → App.Composite");
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();

        // A collection is not a definite edge: the service collection extension is generated
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).IsNotNull();
    }

    [Test]
    public async Task SGIOC302_SingletonDependsOnCollectionWithScopedElement_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)])]
            public sealed class ScopedHandler : IHandler;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(IHandler[] handlers);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC302:AppContainer|};
            """);

        await Assert.That(Messages(result, "SGIOC302").Single()).IsEqualTo("Singleton 'App.Consumer' depends on scoped 'App.ScopedHandler'");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC502_CollectionWithTwoTransientElements_ReportedOnceWithTheFirst()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler)])]
            public sealed class FirstHandler : IHandler;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler)])]
            public sealed class SecondHandler : IHandler;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<IHandler> handlers);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC502:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Messages(result, "SGIOC502").Single())
            .IsEqualTo("Singleton 'App.Consumer' holds transient 'App.FirstHandler' for the lifetime of the container; depend on Func<App.IHandler> to create instances on demand");
    }

    [Test]
    public async Task Collection_ElementIsAnErrorNode_NoMissingDependencyAndNoContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [{|SGIOC002:IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])|}]
            public sealed class Handler : IHandler
            {
                public Handler(int a) { }
                public Handler(string b) { }
            }

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<IHandler> handlers);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC304_InvalidCombination_ReportedAtTheSiteOnce()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IFoo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(
                {|SGIOC304:Lazy<Lazy<IFoo>> a|},
                {|SGIOC304:IEnumerable<IEnumerable<IFoo>> b|},
                {|SGIOC304:Lazy<IEnumerable<IFoo>> c|},
                {|SGIOC304:Task<Func<IFoo>> d|},
                {|SGIOC304:IFoo[][] e|},
                {|SGIOC304:Dictionary<string, KeyValuePair<string, IFoo>> f|});

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await Assert.That(Messages(result, "SGIOC304")).IsEquivalentTo(
        [
            "'System.Lazy<System.Lazy<App.IFoo>>' is not a supported combination of wrapper types",
            "'System.Collections.Generic.IEnumerable<System.Collections.Generic.IEnumerable<App.IFoo>>' is not a supported combination of wrapper types",
            "'System.Lazy<System.Collections.Generic.IEnumerable<App.IFoo>>' is not a supported combination of wrapper types",
            "'System.Threading.Tasks.Task<System.Func<App.IFoo>>' is not a supported combination of wrapper types",
            "'App.IFoo[][]' is not a supported combination of wrapper types",
            "'System.Collections.Generic.Dictionary<System.String, System.Collections.Generic.KeyValuePair<System.String, App.IFoo>>' is not a supported combination of wrapper types",
        ]);

        // An Error of the definite check: neither the container nor the service collection extension is generated
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    [Arguments("List<IFoo>")]
    [Arguments("ValueTask<IFoo>")]
    [Arguments("Lazy<IFoo, string>")]
    public async Task SGIOC304_TypeOutsideTheGrammar_IsAnOrdinaryServiceType(string type)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IFoo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer({{type}} value);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Messages(result, "SGIOC402").Single()).Contains("App.Consumer → value");
    }

    [Test]
    [Arguments("Func<Task<IFoo>>")]
    [Arguments("Lazy<Task<IFoo>>")]
    [Arguments("Func<int, string, IFoo>")]
    [Arguments("IEnumerable<KeyValuePair<string, Func<IFoo>>>")]
    [Arguments("IReadOnlyDictionary<string, Lazy<Task<IFoo>>>")]
    public async Task SGIOC304_ValidCombination_NotReported(string type)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IFoo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer({{type}} value);
            """, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await Assert.That(Messages(result, "SGIOC304")).IsEmpty();
    }

    [Test]
    public async Task SGIOC304_PartialAccessor_ReportedOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IFoo;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC304:AppContainer|}
            {
                public partial Lazy<Lazy<IFoo>> Foo { get; }
            }
            """);

        await Assert.That(Messages(result, "SGIOC304").Single()).IsEqualTo("'System.Lazy<System.Lazy<App.IFoo>>' is not a supported combination of wrapper types");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }
}
