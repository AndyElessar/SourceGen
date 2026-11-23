namespace SourceGen.Ioc.Test.Generator;

/// <summary>The <c>Add{Name}</c> method (Register.Output.spec): conditions, name, namespace, and registrations.</summary>
public class AddMethodTests
{
    private const string Services = """
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        public interface IRepo;
        public interface IClock;

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)])]
        internal sealed class Repo : IRepo;

        [IocRegister(ServiceLifetime.Scoped)]
        internal sealed class Service(IRepo repo, Clock clock);

        [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IClock)])]
        internal sealed class Clock : IClock
        {
            public Clock(IRepo repo) { }
        }
        """;

    private static GeneratorRunOptions RootNamespace(string value) => new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = value } };

    [Test]
    public async Task AddMethod_NoAttribute_GeneratesNothing()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(Services);

        await Assert.That(result.GeneratedSources).IsEmpty();
        await Assert.That(result.ReportedDiagnostics).IsEmpty();
    }

    [Test]
    public async Task AddMethod_NoRegistration_GeneratesOnlyTheGuard()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]",
            RootNamespace("App"));

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task AddMethod_ThreeLifetimes_RegistersHoldersAndServiceTypes()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]"],
            RootNamespace("App"));

        await result.VerifyCompilableAsync();
        await Assert.That(result.ReportedDiagnostics).IsEmpty();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task AddMethod_RegisterForOnAssembly_RegistersTheType()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocServiceCollectionExtensions(Name = "App")]
            [assembly: IocRegisterFor(typeof(App.External), ServiceLifetime.Singleton, ServiceTypes = [typeof(System.IDisposable)])]

            namespace App;

            public sealed class External : System.IDisposable
            {
                public void Dispose() { }
            }
            """,
            RootNamespace("App"));

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task AddMethod_TwoAttributesOnOneType_MergesServiceTypes()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocServiceCollectionExtensions(Name = "App")]

            namespace App;

            public interface IA;
            public interface IB;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IA)])]
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IB), typeof(IA)])]
            internal sealed class Both : IA, IB;
            """,
            RootNamespace("App"));

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await Assert.That(CountOf(source, "IocHolder.Own(")).IsEqualTo(1);
        await Assert.That(CountOf(source, "AddSingleton<global::App.IA>(")).IsEqualTo(1);
        await Assert.That(CountOf(source, "AddSingleton<global::App.IB>(")).IsEqualTo(1);
        await Assert.That(CountOf(source, "AddSingleton<global::App.Both>(")).IsEqualTo(1);
    }

    [Test]
    [Arguments("App", null, "App")]
    [Arguments("My.App-2", null, "MyApp2")]
    [Arguments(null, "Contoso.Shop", "ContosoShop")]
    [Arguments("_x", null, "_x")]
    public async Task AddMethod_Name_RemovesNonIdentifierCharacters(string? name, string? assemblyName, string expected)
    {
        string attribute = name is null ? "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions]" : $"[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"{name}\")]";
        GeneratorRunOptions options = RootNamespace("Root") with { AssemblyName = assemblyName ?? "TestAssembly" };

        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(attribute, options);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource($"{expected}ServiceCollectionExtensions.g.cs")!;
        await Assert.That(source).Contains($"public static class {expected}ServiceCollectionExtensions");
        await Assert.That(source).Contains($" Add{expected}(this ");
    }

    [Test]
    [Arguments("123", "the name cannot start an identifier")]
    [Arguments("...", "no identifier characters remain")]
    [Arguments("", "no identifier characters remain")]
    public async Task AddMethod_InvalidName_ReportsSGIOC108AndGeneratesNothing(string name, string reason)
    {
        (IReadOnlyList<string> sources, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(
            $"[assembly: {{|SGIOC108:SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"{name}\")|}}]");

        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(sources, RootNamespace("App"));

        await result.VerifyDiagnosticsAsync(expected);
        await Assert.That(result.GetDiagnostic("SGIOC108").GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo($"'{name}' does not give a valid name for the Add method: {reason}");
        await Assert.That(result.GeneratedSources).IsEmpty();
    }

    [Test]
    public async Task AddMethod_InvalidAssemblyName_ReportsSGIOC108WithAssemblyName()
    {
        (IReadOnlyList<string> sources, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(
            "[assembly: {|SGIOC108:SourceGen.Ioc.IocServiceCollectionExtensions|}]");

        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(sources, RootNamespace("App") with { AssemblyName = "1st" });

        await result.VerifyDiagnosticsAsync(expected);
        await Assert.That(result.GetDiagnostic("SGIOC108").GetMessage(System.Globalization.CultureInfo.InvariantCulture)).StartsWith("'1st' ");
    }

    [Test]
    public async Task AddMethod_Namespace_RootNamespaceOrAssemblyName()
    {
        const string Attribute = "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]";

        GeneratorTestResult withRoot = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(Attribute, RootNamespace("Company.Product"));
        GeneratorTestResult withoutRoot = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(Attribute, new() { AssemblyName = "Company.Assembly" });

        await Assert.That(withRoot.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("namespace Company.Product;");
        await Assert.That(withRoot.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("\"Company.Product.AppServiceCollectionExtensions.AddApp\"");
        await Assert.That(withoutRoot.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("namespace Company.Assembly;");
    }

    [Test]
    public async Task AddMethod_SourcesInAnyOrder_GeneratesSameOutput()
        => await SourceGeneratorTestHelper.VerifyDeterministicAsync<IocSourceGenerator>(
            [Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]", "namespace App; [SourceGen.Ioc.IocRegister(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)] internal sealed class Another;"],
            RootNamespace("App"));

    [Test]
    public async Task AddMethod_UnrelatedEdit_DoesNotRunDeclarationsAgain()
    {
        GeneratorTestResult first = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]", "namespace App; public class Unrelated;"],
            RootNamespace("App") with { TrackSteps = true });

        GeneratorTestResult second = IncrementalTestHelper.RunAgainWithEdit(first, 2, "namespace App; public class Unrelated { public int X; }");

        await IncrementalTestHelper.VerifyNotRunAgainAsync(second, TrackingNames.Declarations);
        await Assert.That(IncrementalTestHelper.FindForbiddenValues(first, TrackingNames.Declarations)).IsEmpty();
    }

    private const string Usings = """
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        [assembly: IocServiceCollectionExtensions(Name = "App")]

        namespace App;

        public interface IBar;

        """;

    private static async Task<string> GenerateAsync(string source, GeneratorRunOptions? options = null, string assemblyAttributes = "")
    {
        string attributes = "using Microsoft.Extensions.DependencyInjection;\nusing SourceGen.Ioc;\n" + assemblyAttributes;
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>([Usings + source, attributes], options ?? RootNamespace("App"));
        await result.VerifyCompilableAsync();
        return result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
    }

    [Test]
    public async Task AddMethod_PositionalRecord_UsesPrimaryConstructorNotCopyConstructor()
    {
        string source = await GenerateAsync("""
            [IocRegister(ServiceLifetime.Singleton)] public sealed record Foo(IBar Bar);
            [IocRegister(ServiceLifetime.Singleton)] public record Plain { }
            [IocRegister(ServiceLifetime.Singleton)] public sealed class Bar : IBar;
            """);

        await Assert.That(source).Contains("new global::App.Foo(");
        await Assert.That(source).Contains("GetRequiredService<global::App.IBar>(sp)");
        await Assert.That(source).Contains("new global::App.Plain();");
    }

    [Test]
    public async Task AddMethod_JaggedArrayParameter_KeepsRankOrder()
    {
        string source = await GenerateAsync("""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class J(int[][,] a, int[,][] b);
            """);

        await Assert.That(source).Contains("GetRequiredService<global::System.Int32[][,]>(sp)");
        await Assert.That(source).Contains("GetRequiredService<global::System.Int32[,][]>(sp)");
    }

    [Test]
    [Arguments("[IocInject] internal Marked(IBar bar) { } public Marked() { }", "GetRequiredService<global::App.IBar>")]
    [Arguments("[ActivatorUtilitiesConstructor] public Marked(IBar bar) { } public Marked() { }", "GetRequiredService<global::App.IBar>")]
    [Arguments("public Marked(IBar bar) { }", "GetRequiredService<global::App.IBar>")]
    public async Task AddMethod_ConstructorSelection_UsesMarkedOrSinglePublic(string constructors, string expected)
    {
        string source = await GenerateAsync($$"""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Marked
            {
                {{constructors}}
            }
            """);

        await Assert.That(source).Contains("new global::App.Marked(");
        await Assert.That(source).Contains(expected);
    }

    [Test]
    public async Task AddMethod_PrimaryConstructorAndOtherPublicConstructor_UsesPrimary()
    {
        string source = await GenerateAsync("""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class P(IBar bar)
            {
                public P() : this(null!) { }
            }
            """);

        await Assert.That(source).Contains("GetRequiredService<global::App.IBar>(sp)");
    }

    [Test]
    [Arguments("[IocInject] public Bad(IBar bar) { } [IocInject] public Bad() { }")]
    [Arguments("public Bad(IBar bar) { } public Bad() { }")]
    [Arguments("private Bad() { }")]
    public async Task AddMethod_NoSingleConstructor_IsNotRegistered(string constructors)
    {
        string source = await GenerateAsync($$"""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Bad
            {
                {{constructors}}
            }
            """);

        await Assert.That(source).DoesNotContain("global::App.Bad");
    }

    [Test]
    [Arguments("[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(int)])] public sealed class Bad;")]
    [Arguments("[IocRegister(ServiceLifetime.Transient)] public sealed class Bad { public Bad(ref int value) { } }")]
    [Arguments("[IocRegister(ServiceLifetime.Transient)] public sealed class Bad { public Bad(out int value) { value = 0; } }")]
    [Arguments("[IocRegister(ServiceLifetime.Transient)] public sealed class Bad { public Bad(ref readonly int value) { } }")]
    public async Task AddMethod_UnsupportedDeclaration_IsSkippedAndOutputCompiles(string declaration)
    {
        string source = await GenerateAsync(declaration);

        await Assert.That(source).DoesNotContain("global::App.Bad");
    }

    [Test]
    public async Task AddMethod_RegisterForTypeOfOtherAssembly_DoesNotUsePrimaryConstructor()
    {
        GeneratorRunOptions libraryOptions = new() { AssemblyName = "Lib" };
        ReferencedAssembly library = SourceGeneratorTestHelper.CompileReference<IocSourceGenerator>(
            """
            namespace Lib;

            public interface IDep;

            // Two public constructors in metadata: the primary constructor is not known outside this compilation
            public sealed class TwoConstructors(IDep dep)
            {
                public TwoConstructors() : this(null!) { }
            }

            public sealed class OnePrimary(IDep dep);
            """,
            libraryOptions);

        string source = await GenerateAsync(
            string.Empty,
            RootNamespace("App") with { References = [library.Image] },
            """
            [assembly: IocRegisterFor(typeof(Lib.TwoConstructors), ServiceLifetime.Transient)]
            [assembly: IocRegisterFor(typeof(Lib.OnePrimary), ServiceLifetime.Transient)]
            """);

        await Assert.That(source).DoesNotContain("global::Lib.TwoConstructors");
        await Assert.That(source).Contains("new global::Lib.OnePrimary(");
    }

    [Test]
    public async Task AddMethod_RegisterAndRegisterForOnSameType_MergesIntoOneDefinition()
    {
        string source = await GenerateAsync(
            """
            [IocRegister(ServiceLifetime.Scoped)]
            public sealed class Both : IBar;
            """,
            assemblyAttributes: "[assembly: IocRegisterFor(typeof(App.Both), ServiceLifetime.Scoped, ServiceTypes = [typeof(App.IBar)])]");

        await Assert.That(CountOf(source, "IocHolder.Own(")).IsEqualTo(1);
        await Assert.That(source).Contains("AddScoped<global::App.IBar>(");
    }

    [Test]
    public async Task AddMethod_DifferentLifetimesOnSameType_SkipsTheDefinition()
    {
        string source = await GenerateAsync("""
            [IocRegister(ServiceLifetime.Scoped)]
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IBar)])]
            public sealed class Both : IBar;
            """);

        await Assert.That(source).DoesNotContain("global::App.Both");
    }

    [Test]
    public async Task AddMethod_Ordering_FollowsSortNameNotDeclarationOrder()
    {
        string source = await GenerateAsync("""
            [IocRegister(ServiceLifetime.Transient)] public sealed class Zeta;
            [IocRegister(ServiceLifetime.Transient)] public sealed class Alpha;
            """);

        await Assert.That(source.IndexOf("AddTransient<global::App.Alpha>", StringComparison.Ordinal))
            .IsLessThan(source.IndexOf("AddTransient<global::App.Zeta>", StringComparison.Ordinal));
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for(int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
