using System.Text.RegularExpressions;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Optional dependencies (Semantics.spec, "Dependency resolution"; Analysis.Model.spec, "Single service";
/// ServiceCollection.Output.spec, "Dependency resolution").
/// </summary>
public partial class OptionalDependencyTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Consumer = Usings + """
        public interface IClock;
        public interface IRepo;
        public enum Mode { A, B }
        public struct Options;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        public sealed class SystemClock : IClock;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Consumer(
            IClock? clock = null,
            IRepo? repo = null,
            IRepo? other = null,
            IRepo withDefault = null!,
            int retries = 3,
            long offset = -1,
            uint count = 7,
            Mode mode = Mode.B,
            Mode? maybe = null,
            string name = "a\"b",
            char separator = '\n',
            bool enabled = true,
            double ratio = double.NaN,
            float scale = 0.1f,
            decimal price = 1.5m,
            int? limit = null,
            short small = -2,
            in Options options = default);
        """;

    private static async Task<GeneratorTestResult> RunAsync(string source, GeneratorRunOptions? options = null, string? extra = null)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = extra is null ? DiagnosticMarkup.Parse(source) : DiagnosticMarkup.Parse(source, extra);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, options);
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    [GeneratedRegex(@"__Get_Consumer_[0-9a-f]+")]
    private static partial Regex ConsumerResolver { get; }

    [Test]
    public async Task OptionalDependency_Container_ResolvesOrUsesTheDefault()
    {
        GeneratorTestResult result = await RunAsync(Consumer + """

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Verify(SnapshotHelper.Members(source, ConsumerResolver.Match(source).Value));
    }

    [Test]
    public async Task OptionalDependency_ServiceCollectionExtension_UsesGetService()
    {
        GeneratorTestResult result = await RunAsync(
            Consumer,
            new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } },
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task OptionalDependency_DateTimeConstant_CreatesTheDateTime()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Dated([Optional, DateTimeConstant(638000000000000000)] DateTime since);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("(global::System.DateTime)(new global::System.DateTime(638000000000000000L))");
    }

    [Test]
    public async Task SGIOC402_NullableReferenceWithoutDefaultValue_Required()
    {
        // Only a default value makes a parameter optional, as in MS.DI (ADR IocRewrite/0020)
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IRepo? repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task NullableReferenceWithoutDefaultValue_ServiceCollectionExtension_UsesGetRequiredService()
    {
        GeneratorTestResult result = await RunAsync(
            Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IRepo? repo);
            """,
            new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } },
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("GetRequiredService<global::App.IRepo>(sp)");
    }

    [Test]
    public async Task SGIOC402_NullableObliviousContext_Required()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IRepo;

            #nullable disable
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IRepo repo);
            #nullable restore

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC402_PartialAccessorNullableValueType_Required()
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(Usings + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|}
            {
                public partial int? Count { get; }
            }
            """);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, new() { AllowedInputErrors = new HashSet<string> { "CS9248" } });
        await result.VerifyDiagnosticsAsync(expected);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC402_OptionalAttributeWithoutDefault_Required()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            using System.Runtime.InteropServices;

            public interface IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer([Optional] IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC402_NullableValueTypeWithoutDefault_Required()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(int? limit);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC401_OptionalAmbiguous_StillReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class ClockA : IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class ClockB : IClock;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IClock? clock = null);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC401:AppContainer|};
            """);

        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC302_OptionalScopedInSingleton_StillCaptive()
    {
        await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Session;

            [IocRegister(ServiceLifetime.Singleton)]
            public sealed class Cache({|SGIOC302:Session? session|});
            """);
    }

    [Test]
    public async Task OptionalDependency_PartialAccessorNullable_ReturnsNullWhenMissing()
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(Usings + """
            public interface IRepo;
            public interface IClock;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class SystemClock : IClock;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IRepo? Repo { get; }

                public partial IClock? GetClock();
            }
            """);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, new() { AllowedInputErrors = new HashSet<string> { "CS8795", "CS9248" } });
        await result.VerifyDiagnosticsAsync(expected);

        await result.VerifyCompilableAsync();
        await Verify(SnapshotHelper.Members(result.GetGeneratedSource("AppContainer.g.cs")!, "Repo", "GetClock"));
    }
}
