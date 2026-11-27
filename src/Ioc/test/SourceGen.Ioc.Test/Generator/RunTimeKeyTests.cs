using System.Globalization;
using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// SGIOC505: a key of a dependency site that has no registration of the same compile-time identity, but can be equal
/// to a registered key at run time (Semantics.spec, "Keyed services"; Diagnostics.spec, "Warning").
/// </summary>
public class RunTimeKeyTests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        public interface IStore;

        public static class Keys
        {
            public static readonly string A = "a";
            public static readonly string B = "b";
            public static readonly int Number = 1;
            public static readonly object Boxed = "a";
            public const string Constant = "a";
        }

        """;

    private const string CollectionSite = Usings + """
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
        public sealed class Store : IStore;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IEnumerable<IStore> stores);

        """;

    private static async Task<GeneratorTestResult> RunAsync(string source, IReadOnlyDictionary<string, ReportDiagnostic>? diagnosticOptions = null)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(source);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            parsed,
            new()
            {
                AllowedInputErrors = new HashSet<string> { "CS8795", "CS9248" },
                DiagnosticOptions = diagnosticOptions ?? new Dictionary<string, ReportDiagnostic>(),
            });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static IEnumerable<string> Messages(GeneratorTestResult result)
        => result.ReportedDiagnostics.Where(static d => d.Id == "SGIOC505").Select(static d => d.GetMessage(CultureInfo.InvariantCulture));

    [Test]
    public async Task SGIOC505_SymbolSiteKeyAndValueRegistration_ReportedWithTheMissingDependency()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class Store : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IStore store);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402,SGIOC505:AppContainer|};
            """);

        await Assert.That(Messages(result).Single()).IsEqualTo(
            "'App.Consumer → store' requires 'App.IStore (Key: App.Keys.A)', and no registration has this key at compile time; at run time it can be equal to the key of these registrations, which the container does not match: App.Store (Key: \"a\"); use the same key member or a const key");
        await Assert.That(result.ReportedDiagnostics.Single(static d => d.Id == "SGIOC505").Severity).IsEqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    [Test]
    public async Task SGIOC505_FuncWithArguments_ReportedWithTheInvalidFunc()
    {
        // T of the Func is not registered with this key, so SGIOC305 also comes
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class Store : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] Func<string, IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC305,SGIOC505:AppContainer|};
            """);

        await Assert.That(Messages(result).Single()).StartsWith(
            "'App.Consumer → stores' requires 'System.Func<System.String, App.IStore> (Key: App.Keys.A)', and no registration has this key at compile time;");
    }

    [Test]
    public async Task FuncWithArguments_SameKeyIdentity_NotReported()
    {
        // Near SGIOC505: the site key and the registered key are the same member
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.A))]
            public sealed class Store(string name) : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] Func<string, IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC505_ValueSiteKeyAndSymbolRegistration_ReportedWithTheFallback()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.A))]
            public sealed class Store : IStore;

            [IocContainer]
            public partial class {|SGIOC501,SGIOC505:AppContainer|}
            {
                [IocInject(Key = "a")]
                public partial IStore Store { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Messages(result).Single()).StartsWith("'App.AppContainer → Store' requires 'App.IStore (Key: \"a\")', and no registration has this key at compile time; at run time it can be equal to the key of these registrations, which the container does not match: App.Store (Key: App.Keys.A);");
    }

    [Test]
    public async Task SGIOC505_CollectionWithSymbolKey_ReportedAndGenerated()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class Store : IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.Boxed))]
            public sealed class BoxedStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IReadOnlyList<IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC505:AppContainer|};
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNotNull();
        await Assert.That(Messages(result).Single()).Contains(
            "'System.Collections.Generic.IReadOnlyList<App.IStore> (Key: App.Keys.A)', and no registration has this key at compile time; at run time it can be equal to the key of these registrations, which the container does not match: App.BoxedStore (Key: App.Keys.Boxed), App.Store (Key: \"a\");");
    }

    [Test]
    public async Task SGIOC505_SeverityOptions_SuppressedOrRaisedWithoutChangingTheOutput()
    {
        string source = CollectionSite + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """;

        GeneratorTestResult suppressed = await RunAsync(source, new Dictionary<string, ReportDiagnostic> { ["SGIOC505"] = ReportDiagnostic.Suppress });
        GeneratorTestResult raised = await RunAsync(source.Replace("class AppContainer", "class {|SGIOC505:AppContainer|}", StringComparison.Ordinal), new Dictionary<string, ReportDiagnostic> { ["SGIOC505"] = ReportDiagnostic.Error });

        await Assert.That(raised.ReportedDiagnostics.Single().Severity).IsEqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        await Assert.That(suppressed.GetGeneratedSource("AppContainer.g.cs")).IsNotNull();
        await Assert.That(raised.GetGeneratedSource("AppContainer.g.cs")).IsEqualTo(suppressed.GetGeneratedSource("AppContainer.g.cs"));
    }

    [Test]
    public async Task SGIOC505_ServiceCollectionExtension_NotReported()
    {
        // MS.DI compares the keys with Equals
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [CollectionSite, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]"]);

        await result.VerifyDiagnosticsAsync([]);
        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).IsNotNull();
    }

    [Test]
    public async Task SGIOC505_SiteKeyOfASkippedDeclaration_NotReported()
    {
        // The registration with the key of the site is skipped (SGIOC002): it is an error node with its own error
        await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class Store : IStore;

            [{|SGIOC002:IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.A))|}]
            public sealed class Broken : IStore
            {
                public Broken(int a) { }
                public Broken(string b) { }
            }

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(KeyType = KeyType.Csharp, Key = nameof(Keys.A))] IEnumerable<IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);
    }

    [Test]
    [Arguments("KeyType = KeyType.Csharp, Key = nameof(Keys.A)")]
    [Arguments("KeyType = KeyType.Csharp, Key = nameof(Keys.Constant)")]
    [Arguments("KeyType = KeyType.Csharp, Key = nameof(Keys.Number)")]
    public async Task SGIOC505_SameIdentityOrDifferentKeyType_NotReported(string siteKey)
    {
        // The same symbol and a const (normalized to its value) match by identity. An int cannot be equal to a string
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], KeyType = KeyType.Csharp, Key = nameof(Keys.A))]
            public sealed class SymbolStore : IStore;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class ValueStore : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject({{siteKey}})] IEnumerable<IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC505_TwoDifferentValues_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IStore)], Key = "a")]
            public sealed class Store : IStore;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([IocInject(Key = "b")] IEnumerable<IStore> stores);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC505_Pragma_Suppressed()
    {
        GeneratorTestResult result = await RunAsync(CollectionSite + """
            #pragma warning disable SGIOC505
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            #pragma warning restore SGIOC505
            """);

        await result.VerifyCompilableAsync();
    }
}
