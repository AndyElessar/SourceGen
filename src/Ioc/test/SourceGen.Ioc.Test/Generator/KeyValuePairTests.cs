using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// <c>KeyValuePair&lt;TKey, T&gt;</c> of a single service in the container (Analysis.Model.spec, "Grammar of wrapper
/// types"; Container.Output.spec, "Wrapper types", "Behavior for each request").
/// </summary>
public class KeyValuePairTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Clocks = Usings + """
        public interface IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        public sealed class SystemClock : IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)], Key = "utc")]
        public sealed class UtcClock : IClock;

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
    public async Task KeyValuePair_Container_PairsTheKeyOfTheSiteWithTheService()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(
                [IocInject(Key = "utc")] KeyValuePair<string, IClock> clock,
                [IocInject(Key = "utc")] KeyValuePair<object, IClock> any,
                [IocInject(Key = "utc")] KeyValuePair<string, Lazy<IClock>> lazy);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                [IocInject(Key = "utc")]
                public partial KeyValuePair<string, IClock> Utc { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["Utc", "__BuildKeyedLookup"], "__Get_Consumer_", "__Pair_", "__Wrap_"));
    }

    [Test]
    public async Task KeyValuePair_KeyTypeIsNotTKey_Missing()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "utc")] KeyValuePair<int, IClock> clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC402"))
            .IsEqualTo("'App.Consumer → clock' requires 'System.Collections.Generic.KeyValuePair<System.Int32, App.IClock> (Key: \"utc\")', which is not registered");
    }

    [Test]
    public async Task KeyValuePair_SiteWithoutKey_MissingAlsoWhenARegistrationWithoutKeyExists()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(KeyValuePair<object, IClock> clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC402"))
            .IsEqualTo("'App.Consumer → clock' requires 'System.Collections.Generic.KeyValuePair<System.Object, App.IClock>', which is not registered");
    }

    [Test]
    public async Task KeyValuePair_OptionalMissingWithoutFallback_DefaultValue()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "other")] KeyValuePair<string, IClock> clock = default);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("__Pair_");
    }

    [Test]
    public async Task KeyValuePair_MissingWithFallback_AsksForThePairUnchangedWithoutKey()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(
                [IocInject(Key = "other")] KeyValuePair<string, IClock> clock,
                [IocInject(Key = "utc")] KeyValuePair<int, IClock> wrongType = default);

            [IocContainer]
            public partial class {|SGIOC501:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Message(result, "SGIOC501")).IsEqualTo(
            "'App.Consumer → clock' requires 'System.Collections.Generic.KeyValuePair<System.String, App.IClock> (Key: \"other\")', which is not registered in the container; it is resolved from the fallback service provider");
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, [], "__Get_Consumer_"));
    }

    [Test]
    public async Task KeyValuePair_TwoRegistrationsWithTheKey_Ambiguous()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)], Key = "utc")]
            public sealed class OtherUtcClock : IClock;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "utc")] KeyValuePair<string, IClock> clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC401:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC401")).IsEqualTo(
            "'App.Consumer → clock' requires a single 'System.Collections.Generic.KeyValuePair<System.String, App.IClock> (Key: \"utc\")' but 2 registrations provide it: App.OtherUtcClock (Key: \"utc\"), App.UtcClock (Key: \"utc\"); use a key or depend on a collection");
    }

    [Test]
    public async Task SGIOC301_CycleThroughKeyValuePair_ReportedByTheFullCheck()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class First([IocInject(Key = "b")] KeyValuePair<string, Second> second);

            [IocRegister(ServiceLifetime.Transient, Key = "b")]
            public sealed class Second(First first);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC301:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.First → App.Second (Key: \"b\") → App.First");
    }

    [Test]
    public async Task SGIOC505_KeyValuePairWithSymbolKeyEqualAtRunTime_Warns()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            public static class Keys
            {
                public static readonly string Utc = "utc";
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.Utc))] KeyValuePair<string, IClock> clock = default);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC505:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC505_KeyTypeOfTheRegistrationIsNotTKey_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Clocks + """
            public static class Keys
            {
                public static readonly string Utc = "utc";
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.Utc))] KeyValuePair<int, IClock> clock = default);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC502_SingletonHoldsTransientThroughAPair_ReportedButNotThroughFunc()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IClock)], Key = "t")]
            public sealed class TransientClock : IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Holder([IocInject(Key = "t")] KeyValuePair<string, IClock> clock);

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class FactoryHolder([IocInject(Key = "t")] KeyValuePair<string, Func<IClock>> clocks);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC502:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC502")).IsEqualTo(
            "Singleton 'App.Holder' holds transient 'App.TransientClock (Key: \"t\")' for the lifetime of the container; depend on Func<App.IClock> to create instances on demand");
    }

    /// <summary>Stores with the keys "a" and "b" (string), 1L (long) and <c>Keys.C</c> (a string symbol).</summary>
    private const string Stores = Usings + """
        public interface IStore;

        public static class Keys
        {
            public static readonly string C = "c";
        }

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)])]
        public sealed class DefaultStore : IStore;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "b")]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = 1L)]
        [IocRegister(ServiceLifetime.Scoped, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.C))]
        public sealed class Store : IStore;

        """;

    [Test]
    public async Task PairCollection_Container_KeyedRegistrationsOfTheKeyTypeSelectedByTheKeyOfTheSite()
    {
        GeneratorTestResult result = await RunAsync(Stores + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Consumer(
                IEnumerable<KeyValuePair<string, IStore>> strings,
                IReadOnlyList<KeyValuePair<object, Lazy<IStore>>> all,
                [IocInject(Key = "a")] KeyValuePair<string, IStore>[] onlyA,
                [IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] IEnumerable<KeyValuePair<long, IStore>> longs);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["__lookup", "__BuildKeyedLookup"], "__Get_Consumer_", "__Keyed_", "__CreateKeyed_"));
    }

    [Test]
    public async Task Dictionary_Container_OneEntryForEachKeyOfTheKeyType()
    {
        GeneratorTestResult result = await RunAsync(Stores + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Consumer(
                IReadOnlyDictionary<string, IStore> strings,
                Dictionary<long, Func<IStore>> longs,
                [IocInject(KeyType = KeyType.Csharp, Key = nameof(KeyedService.AnyKey))] IDictionary<object, IStore> all,
                [IocInject(Key = "b")] IDictionary<string, IStore> onlyB);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IReadOnlyDictionary<string, IStore> Strings { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.MembersByPrefix(source, ["Strings", "__lookup", "__BuildKeyedLookup"], "__Get_Consumer_", "__Keyed_", "__CreateKeyed_"));
    }

    [Test]
    public async Task PairCollectionAndDictionary_NoCandidateWithFallback_EmptyAndNotFromTheFallback()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)])]
            public sealed class DefaultStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IEnumerable<KeyValuePair<string, IStore>> pairs, IDictionary<string, IStore> dictionary);

            [IocContainer]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).Contains("global::System.Array.Empty<global::System.Collections.Generic.KeyValuePair<global::System.String, global::App.IStore>>()");
        await Assert.That(source).Contains("new global::System.Collections.Generic.Dictionary<global::System.String, global::App.IStore>()");
    }

    [Test]
    public async Task SGIOC404_TwoRegistrationsWithTheSameKey_ReportedAndNotGenerated()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class FirstStore : IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class SecondStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IReadOnlyDictionary<string, IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC404:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC404")).IsEqualTo(
            "Dictionary of 'App.IStore' has more than one registration for key '\"a\"': App.FirstStore (Key: \"a\"), App.SecondStore (Key: \"a\")");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC404_SeveralDictionariesWithTheSameDuplicateKey_ReportedOnce()
        => await RunAsync(Usings + """
            public interface IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class FirstStore : IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class SecondStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(
                IReadOnlyDictionary<string, IStore> stores,
                IDictionary<string, Lazy<IStore>> lazy,
                [IocInject(Key = "a")] IDictionary<string, IStore> onlyA,
                IDictionary<object, IStore> all);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC404:AppContainer|};
            """);

    [Test]
    public async Task SGIOC404_ServiceCollectionExtension_StillGenerated()
    {
        GeneratorTestResult result = await RunAsync(
            Usings + """
            public interface IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class FirstStore : IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class SecondStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IReadOnlyDictionary<string, IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC404:AppContainer|};
            """,
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        // Only the container checks duplicate keys: the service collection extension throws when it is called
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).IsNotNull();
    }

    [Test]
    public async Task Dictionary_CandidateIsAnErrorNode_OnlyTheOriginalError()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], {|SGIOC022:Key = typeof(string)|})]
            public sealed class BrokenStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IReadOnlyDictionary<string, IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC505_DictionaryWithSymbolKeyEqualAtRunTime_WarnsOnlyForTheKeyType()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IStore;

            public static class Keys
            {
                public static readonly string A = "a";
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class Store : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(
                [IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IDictionary<string, IStore> strings,
                [IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IEnumerable<KeyValuePair<long, IStore>> longs);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC505:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Message(result, "SGIOC505")).Contains("App.Consumer → strings");
    }

    [Test]
    public async Task SGIOC404_SameKeysInACollectionOfPairs_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class FirstStore : IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class SecondStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IEnumerable<KeyValuePair<string, IStore>> stores, IReadOnlyDictionary<long, IStore> others);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC301_CycleThroughDictionary_ReportedByTheFullCheck()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "composite")]
            public sealed class Composite(IReadOnlyDictionary<string, IHandler> handlers) : IHandler;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC301:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC301")).IsEqualTo("Circular dependency: App.Composite (Key: \"composite\") → App.Composite (Key: \"composite\")");
    }

    [Test]
    public async Task SGIOC502_SingletonHoldsTransientThroughACollectionOfPairs_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IHandler;

            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler)], Key = "t")]
            public sealed class Handler : IHandler;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Holder(IEnumerable<KeyValuePair<string, IHandler>> handlers, IDictionary<string, Func<IHandler>> factories);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC502:AppContainer|};
            """);

        await Assert.That(Message(result, "SGIOC502")).IsEqualTo(
            "Singleton 'App.Holder' holds transient 'App.Handler (Key: \"t\")' for the lifetime of the container; depend on Func<App.IHandler> to create instances on demand");
    }

    [Test]
    public async Task PairCollectionAndDictionary_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>([Stores + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Consumer(
                IReadOnlyList<KeyValuePair<object, Lazy<IStore>>> all,
                [IocInject(Key = "a")] KeyValuePair<string, IStore>[] onlyA,
                IReadOnlyDictionary<string, IStore> strings,
                Dictionary<long, Func<IStore>> longs);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """]);

    [Test]
    public async Task KeyValuePair_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>([Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "utc")] KeyValuePair<string, Lazy<IClock>> clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """]);
}
