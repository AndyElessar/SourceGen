using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>The partial accessors of a container (Container.Output.spec, "Partial accessors"; Annotations.spec, "Partial accessor").</summary>
public class PartialAccessorTests
{
    private const string Services = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        public interface IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
        public sealed class SystemClock : IClock;

        [IocRegister(ServiceLifetime.Scoped)]
        public sealed class Session;

        [IocRegister(ServiceLifetime.Transient)]
        public sealed class Formatter;

        """;

    private static async Task<GeneratorTestResult> RunAsync(string source)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(source);
        // The input has no implementation parts: CS8795 for a method, CS9248 for a property
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(parsed, new() { AllowedInputErrors = new HashSet<string> { "CS8795", "CS9248" } });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static string Message(GeneratorTestResult result, string id) => result.ReportedDiagnostics.First(d => d.Id == id).GetMessage(CultureInfo.InvariantCulture);

    [Test]
    public async Task PartialAccessor_PropertiesAndMethods_ImplementedWithTheResolvers()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IClock Clock { get; }

                internal partial Session GetSession();

                [IocInject]
                protected partial Formatter Formatter { get; }

                public partial IServiceProvider Provider { get; }

                public partial IServiceScopeFactory GetScopeFactory();

                public partial AppContainer Self { get; }
            }
            """);

        await result.VerifyCompilableAsync();
        await Verify(SnapshotHelper.Members(result.GetGeneratedSource("AppContainer.g.cs")!, "Clock", "GetSession", "Formatter", "Provider", "GetScopeFactory", "Self"));
    }

    [Test]
    public async Task PartialAccessor_ModifiersAndNullableAnnotation_KeptAsDeclared()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            public class Base
            {
                public virtual IClock? Clock => null;
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer : Base
            {
                public override partial IClock? Clock { get; }

                public new partial Session GetHashCode();
            }
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task PartialAccessor_KeywordName_Escaped()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IClock @class { get; }

                public partial Session @event();
            }
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task PartialAccessor_NullableObliviousContext_Compiles()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            #nullable disable
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IClock Clock { get; }
            }
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task PartialAccessor_DeclaredInAnotherFile_Implemented()
    {
        (IReadOnlyList<string> parsed, _) = DiagnosticMarkup.Parse(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [parsed[0], "namespace App; public partial class AppContainer { public partial IClock Clock { get; } }"],
            new() { AllowedInputErrors = new HashSet<string> { "CS9248" } });

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task PartialAccessor_GeneratedCodeOnTheDefinition_NotRepeated()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                [System.CodeDom.Compiler.GeneratedCode("User", "1.0.0")]
                public partial IClock Clock { get; }
            }
            """);

        // The rule "each generated member has [GeneratedCode]" does not apply: the attribute of the user covers the member
        await Assert.That(result.OutputCompilation.GetDiagnostics().Where(static d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)).IsEmpty();
    }

    [Test]
    [Arguments("public partial void Clock();")]
    [Arguments("public partial ref readonly IClock Clock();")]
    [Arguments("public partial ref readonly IClock Clock { get; }")]
    [Arguments("public static partial IClock Clock();")]
    [Arguments("public partial T Get<T>();")]
    [Arguments("public partial IClock Get(int index);")]
    [Arguments("public partial IClock Clock { get; set; }")]
    [Arguments("public partial IClock Clock { get; init; }")]
    [Arguments("public partial IClock this[int index] { get; }")]
    [Arguments("public partial ref IClock Clock();")]
    public async Task PartialAccessor_OtherShape_NotImplemented(string member)
    {
        GeneratorTestResult result = await RunAsync(Services + $$"""
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                {{member}}
            }
            """);

        await result.VerifyHasCompilationErrorsAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain(" partial global::App.IClock");
    }

    [Test]
    public async Task PartialAccessor_ImplementedByTheUser_NotImplementedAgain()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer
            {
                public partial IClock Clock { get; }

                public partial IClock Clock => new SystemClock();
            }
            """);

        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC402_PartialAccessorMissing_ReportedOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            public interface IRepo;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|}
            {
                public partial IRepo Repo { get; }
            }
            """);

        await Assert.That(Message(result, "SGIOC402")).IsEqualTo("'App.AppContainer → Repo' requires 'App.IRepo', which is not registered");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task SGIOC401_PartialAccessorAmbiguous_ReportedOnTheContainer()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IClock)])]
            public sealed class OtherClock : IClock;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC401:AppContainer|}
            {
                public partial IClock GetClock();
            }
            """);

        await Assert.That(Message(result, "SGIOC401")).StartsWith("'App.AppContainer → GetClock' requires a single 'App.IClock'");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }
}
