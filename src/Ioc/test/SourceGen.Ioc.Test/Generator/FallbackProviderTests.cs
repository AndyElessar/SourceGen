using System.Globalization;
using System.Text.RegularExpressions;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// The container with a fallback provider (Container.Output.spec, "Constructor", "Fallback provider", "Integration with
/// IServiceCollection"; Analysis.Model.spec, "Single service").
/// </summary>
public partial class FallbackProviderTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Services = Usings + """
        public interface IClock;
        public interface ILogger;
        public interface IOptions;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        public sealed class SystemClock : IClock;

        [IocRegister(ServiceLifetime.Scoped)]
        public sealed class Consumer(IClock clock, ILogger logger, IOptions? options = null, int retries = 3, int count = 0) : IDisposable
        {
            public void Dispose() { }
        }

        """;

    private static async Task<GeneratorTestResult> RunAsync(string source, GeneratorRunOptions? options = null)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(source);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, options ?? new() { AllowedInputErrors = new HashSet<string> { "CS8795", "CS9248" } });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static IEnumerable<string> Messages(GeneratorTestResult result, string id)
        => result.ReportedDiagnostics.Where(d => d.Id == id).Select(static d => d.GetMessage(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"__Create_Consumer_[0-9a-f]+")]
    private static partial Regex ConsumerCreation { get; }

    [Test]
    public async Task Container_FallbackProvider_GeneratesTheFallbackPaths()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer]
            public partial class {|SGIOC501:AppContainer|}
            {
                public partial IOptions? Options { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Constructors(source) + Environment.NewLine + Environment.NewLine + SnapshotHelper.Members(
            source,
            "__Singletons",
            "__fallbackScope",
            ConsumerCreation.Match(source).Value,
            "Options",
            "__lookup",
            "GetService",
            "GetKeyedService",
            "IsService",
            "IsKeyedService",
            "__DisposeInstances",
            "__Fallback",
            "__CreateFallbackScope",
            "__Merge",
            "ServiceProviderFactory"));
    }

    [Test]
    public async Task SGIOC501_RequiredDependencyMissing_ReportedOnceForEachSite()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer]
            public partial class {|SGIOC501,SGIOC501:AppContainer|}
            {
                public partial ILogger Logger { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(Messages(result, "SGIOC501")).IsEquivalentTo(
        [
            "'App.Consumer → logger' requires 'App.ILogger', which is not registered in the container; it is resolved from the fallback service provider",
            "'App.AppContainer → Logger' requires 'App.ILogger', which is not registered in the container; it is resolved from the fallback service provider",
        ]);
        await Assert.That(result.ReportedDiagnostics.First(static d => d.Id == "SGIOC501").Severity).IsEqualTo(Microsoft.CodeAnalysis.DiagnosticSeverity.Info);
    }

    [Test]
    public async Task SGIOC501_OptionalDependencyMissing_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IOptions;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IOptions? options = null, int retries = 3);

            [IocContainer]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC501_Pragma_Suppressed()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            #pragma warning disable SGIOC501
            [IocContainer]
            public partial class AppContainer;
            #pragma warning restore SGIOC501
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC402_WrapperDependencyWithFallbackProvider_NotSentToTheFallbackYet()
    {
        // A collection of Func<A..., T> comes in a later slice. The container must create it, so until then it stays a
        // missing dependency and does not go to the fallback provider. A collection of the service is merged with it.
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Collections.Generic;

            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class SystemClock : IClock;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<IClock> clocks, IClock[] array, IEnumerable<Func<string, IClock>>? factories = null);

            [IocContainer]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC401_AmbiguousWithFallbackProvider_StillAnError()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class ClockA : IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class ClockB : IClock;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IClock clock);

            [IocContainer]
            public partial class {|SGIOC401:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task ServiceProviderFactory_WithoutDependencyInjectionPackage_NotGenerated()
    {
        GeneratorTestResult result = await RunAsync(
            Services + """
                [IocContainer]
                public partial class {|SGIOC501:AppContainer|};
                """,
            new() { BaseReferences = SourceGeneratorTestHelper.BaseReferencesWithoutDependencyInjection });

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("ServiceProviderFactory");
    }

    [Test]
    public async Task Container_WithoutFallbackProvider_HasNoFallbackMembers()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Service;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source).DoesNotContain("__Fallback");
        await Assert.That(source).DoesNotContain("ServiceProviderFactory");
        await Assert.That(source).Contains("public AppContainer()");
    }
}
