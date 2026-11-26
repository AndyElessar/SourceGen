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

    [Test]
    public async Task KeyValuePair_Deterministic()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>([Clocks + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "utc")] KeyValuePair<string, Lazy<IClock>> clock);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """]);
}
