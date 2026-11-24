using System.Globalization;
using System.Text.RegularExpressions;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Keys at dependency sites (Annotations.spec, "Key specification" and "Partial accessor"; Semantics.spec, "Keyed
/// services"; Analysis.Model.spec, "Single service").
/// </summary>
public partial class KeyedDependencyTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Services = Usings + """
        public interface IRepo;

        public static class Keys
        {
            public static readonly object Backup = new();
        }

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "a")]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = 1L)]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], KeyType = KeyType.Csharp, Key = nameof(Keys.Backup))]
        public sealed class Repo([ServiceKey] object key) : IRepo;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
        public sealed class DefaultRepo : IRepo;

        [IocRegister(ServiceLifetime.Transient, Key = "a")]
        public sealed class Consumer(
            [IocInject(Key = "a")] IRepo a,
            [FromKeyedServices(1L)] IRepo b,
            [IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.Backup))] IRepo c,
            [IocInject(LookupMode = ServiceKeyLookupMode.InheritKey)] IRepo inherited,
            [FromKeyedServices] IRepo inheritedToo,
            [IocInject(LookupMode = ServiceKeyLookupMode.NullKey)] IRepo plain,
            [FromKeyedServices(null)] IRepo plainToo,
            [IocInject] IRepo marked,
            [ServiceKey] string key);

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

    [GeneratedRegex(@"__Get_Consumer_[0-9a-f]+")]
    private static partial Regex ConsumerResolver { get; }

    [Test]
    public async Task KeyedDependency_Container_ResolvesByTheKeyOfTheSite()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Members(source, ConsumerResolver.Match(source).Value));
    }

    [Test]
    public async Task KeyedDependency_ServiceCollectionExtension_UsesTheKeyedMethods()
    {
        GeneratorTestResult result = await RunAsync(Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Verify(SnapshotHelper.Members(source, [.. Regex.Matches(source, @"__Create_(Consumer|Repo)_[0-9a-f]+\(").Select(static m => m.Value.TrimEnd('(')).Distinct()]));
    }

    [Test]
    public async Task KeyedDependency_FallbackProvider_ResolvesTheKeyFromIt()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface ILogger;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "x")] ILogger logger, [FromKeyedServices("y")] ILogger? optional = null, [FromKeyedServices("z")] int count = 3);

            [IocContainer]
            public partial class {|SGIOC501:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Messages(result, "SGIOC501")).IsEquivalentTo(
            ["'App.Consumer → logger' requires 'App.ILogger (Key: \"x\")', which is not registered in the container; it is resolved from the fallback service provider"]);
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Members(source, ConsumerResolver.Match(source).Value));
    }

    [Test]
    public async Task KeyedDependency_PartialAccessor_ResolvesByItsKey()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                [IocInject(Key = "a")]
                public partial IRepo Main { get; }

                [IocInject(LookupMode = ServiceKeyLookupMode.InheritKey)]
                public partial IRepo Default { get; }

                [IocInject(Key = "missing")]
                public partial IRepo? Missing { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        await Verify(SnapshotHelper.Members(result.GetGeneratedSource("AppContainer.g.cs")!, "Main", "Default", "Missing"));
    }

    [Test]
    public async Task SGIOC402_KeyedDependencyMissing_NamesTheKey()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "zzz")] IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Messages(result, "SGIOC402").Single()).IsEqualTo("'App.Consumer → repo' requires 'App.IRepo (Key: \"zzz\")', which is not registered");
    }

    [Test]
    public async Task SGIOC402_InheritKeyFindsNoRegistrationForTheKey_DoesNotUseNoKey()
    {
        // InheritKey does not go back to the registration without a key (Semantics.spec, "Keyed services")
        await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient, Key = "a")]
            public sealed class Consumer([FromKeyedServices] IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);
    }

    [Test]
    public async Task SGIOC402_KeyedSiteNextToAnErrorNodeWithAnotherKey_Reported()
    {
        // The error node is (IRepo, "a"). A site that asks for "b" is still a missing dependency
        await RunAsync(Usings + """
            public interface IRepo;

            [{|SGIOC002:IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "a")|}]
            public sealed class Repo : IRepo
            {
                public Repo() { }
                public Repo(int value) { }
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "b")] IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);
    }

    [Test]
    public async Task SGIOC302_CaptiveThroughAKeyedEdge_Reported()
    {
        await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped, Key = "a")]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC302:[IocInject(Key = "a")] Session session|});
            """);
    }

    [Test]
    public async Task SGIOC017_LookupModeWithKey_ReportedOnce()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient, Key = "a")]
            [IocRegister(ServiceLifetime.Transient, Key = "b")]
            public sealed class Consumer([{|SGIOC017:IocInject(LookupMode = ServiceKeyLookupMode.NullKey, Key = "a")|}] IRepo repo);
            """);

        await Assert.That(Messages(result, "SGIOC017").Single()).IsEqualTo("LookupMode 'NullKey' cannot be combined with Key");
    }

    [Test]
    [Arguments("[{|SGIOC018:IocInject|}, FromKeyedServices(\"a\")] object repo", "'repo' has both [IocInject] and [FromKeyedServices]")]
    [Arguments("[{|SGIOC018:IocInject(Key = \"b\")|}, ServiceKey] object repo", "'repo' has both [IocInject] and [ServiceKey]")]
    [Arguments("[FromKeyedServices(\"b\"), {|SGIOC018:ServiceKey|}] object repo", "'repo' has both [FromKeyedServices] and [ServiceKey]")]
    public async Task SGIOC018_TwoKeyAnnotations_Reported(string parameter, string message)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Transient, Key = "a")]
            public sealed class Consumer({{parameter}});
            """);

        await Assert.That(Messages(result, "SGIOC018").Single()).IsEqualTo(message);
    }

    [Test]
    public async Task SGIOC007_AnyKeyForOneService_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, {|SGIOC007:Key = nameof(KeyedService.AnyKey)|})] IRepo repo);
            """);

        await Assert.That(Messages(result, "SGIOC007").Single()).IsEqualTo("KeyedService.AnyKey cannot be used to resolve a single service");
    }

    [Test]
    [Arguments("System.Collections.Generic.IEnumerable<IRepo>")]
    [Arguments("IRepo[]")]
    [Arguments("System.Collections.Generic.IReadOnlyDictionary<string, IRepo>")]
    public async Task SGIOC007_AnyKeyForACollection_NotReported(string type)
    {
        await RunAsync(Usings + $$"""
            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] {{type}} repos);
            """);
    }

    [Test]
    [Arguments("IocInject(Key = \"a\")")]
    [Arguments("IocInject(LookupMode = ServiceKeyLookupMode.InheritKey)")]
    public async Task SGIOC019_KeyOnConstructor_Reported(string attribute)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [{|SGIOC019:{{attribute}}|}]
                public Consumer(IRepo repo) { }
            }
            """);

        await Assert.That(Messages(result, "SGIOC019").Single()).IsEqualTo("[IocInject] on a constructor cannot specify Key or LookupMode; annotate the parameters instead");
    }

    [Test]
    public async Task ServiceKey_TypeNotCompatibleWithTheKey_CompilerReportsIt()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient, Key = 1L)]
            public sealed class Service([ServiceKey] string key);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyHasCompilationErrorsAsync();
    }

    [Test]
    public async Task SGIOC023_ServiceKeyWithoutKey_ReportedAndOptionalGetsTheDefault()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [{|SGIOC023:IocRegister(ServiceLifetime.Transient)|}]
            public sealed class Required([ServiceKey] string key);

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Optional([ServiceKey] string? key = null, [ServiceKey] int number = 5);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(Messages(result, "SGIOC023").Single()).IsEqualTo("Parameter 'key' has [ServiceKey] but 'App.Required' is registered without a key");
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).Contains("(global::System.Int32)(5)");
        await Assert.That(source).DoesNotContain("new global::App.Required(");
    }

    [Test]
    public async Task SGIOC023_KeyedRegistrationWithRequiredServiceKey_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient, Key = "a")]
            public sealed class Service([ServiceKey] string key);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    [Arguments("[{|SGIOC017:IocInject(LookupMode = ServiceKeyLookupMode.NullKey, Key = \"a\")|}] IRepo repo", "")]
    [Arguments("[{|SGIOC018:IocInject|}, FromKeyedServices(\"a\")] IRepo repo", "")]
    [Arguments("[IocInject(KeyType = KeyType.Csharp, {|SGIOC011:Key = \"literal\"|})] IRepo repo", "")]
    [Arguments("[FromKeyedServices({|SGIOC022:typeof(int)|})] IRepo repo", "")]
    [Arguments("[IocInject(KeyType = KeyType.Csharp, {|SGIOC007:Key = nameof(KeyedService.AnyKey)|})] IRepo repo", "")]
    [Arguments("[ServiceKey] string key", "SGIOC023")]
    [Arguments("[ServiceKey] string? key", "SGIOC023")]
    public async Task SiteKeyError_DeclarationSkipped_NoOtherDiagnosticAndNoContainer(string parameter, string registrationError)
    {
        // The skipped declaration is an error node: the dependent User reports no SGIOC402, and the container is not
        // generated (Diagnostics.spec, "Consequences")
        string registration = "IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IConsumer)])";
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public interface IRepo;
            public interface IConsumer;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [{{(registrationError.Length == 0 ? registration : $"{{|{registrationError}:{registration}|}}")}}]
            public sealed class Consumer({{parameter}}) : IConsumer;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class User(IConsumer consumer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC019_KeyOnConstructor_DeclarationSkipped()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [{|SGIOC019:IocInject(Key = "a")|}]
                public Consumer(IRepo repo) { }
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class User(Consumer consumer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SiteKey_CloseButValid_NotReported()
    {
        // ExplicitKey with Key, and [IocInject] without arguments on a constructor, are valid
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "a")]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer
            {
                [IocInject]
                public Consumer([IocInject(LookupMode = ServiceKeyLookupMode.ExplicitKey, Key = "a")] IRepo repo) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC017_OnPartialAccessor_Reported()
    {
        await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
            public sealed class Repo : IRepo;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                [{|SGIOC017:IocInject(LookupMode = ServiceKeyLookupMode.InheritKey, Key = "a")|}]
                public partial IRepo Repo { get; }
            }
            """);
    }
}
