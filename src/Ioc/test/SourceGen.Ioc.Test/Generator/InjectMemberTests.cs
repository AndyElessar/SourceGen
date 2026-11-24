namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// <c>IocInjectMemberAttribute</c> (Annotations.spec, "Types that the user does not own" and "Member lookup by name";
/// Semantics.spec, "Registration scope").
/// </summary>
public class InjectMemberTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        """;

    /// <summary>A library whose types the user does not own.</summary>
    private const string Library = """
        namespace Lib;
        public interface IClock;
        public interface ILog;
        public sealed class NullLog : ILog;
        public class ExternalBase
        {
            public IClock BaseClock { get; set; } = null!;
            public IClock Hidden { get; set; } = null!;
        }
        public class External : ExternalBase
        {
            public ILog? Log { get; set; } = new NullLog();
            public required IClock Required { get; init; }
            public IClock Field = null!;
            public new IClock Hidden { get; set; } = null!;
            public void Initialize(IClock clock) { }
            public void Overloaded() { }
            public void Overloaded(IClock clock) { }
            public void Generic<T>() { }
            public event System.EventHandler? Changed;
            public static IClock Static { get; set; } = null!;
            public class Nested;
        }
        public class HidesWithType : ExternalBase
        {
            public class BaseClock;
            public static new IClock Hidden { get; set; } = null!;
        }
        public abstract class Abstract
        {
            public IClock Clock { get; set; } = null!;
        }
        public class WithServiceKey
        {
            public IClock Clock { get; set; } = null!;
            public void Init([Microsoft.Extensions.DependencyInjection.ServiceKey] object key) { }
        }
        public class Marked
        {
            [SourceGen.Ioc.IocInject] public IClock Clock { get; set; } = null!;
        }
        """;

    private const string Services = """
        [assembly: IocRegisterFor(typeof(App.SystemClock), ServiceLifetime.Singleton, ServiceTypes = [typeof(Lib.IClock)])]
        [assembly: IocRegisterFor(typeof(App.KeyedLog), ServiceLifetime.Singleton, ServiceTypes = [typeof(Lib.ILog)], Key = "main")]

        """;

    private const string Types = """
        namespace App
        {
            public sealed class SystemClock : Lib.IClock;
            public sealed class KeyedLog : Lib.ILog;
        }

        """;

    private static async Task<GeneratorTestResult> RunAsync(string source, bool projectReference = false)
    {
        ReferencedAssembly library = SourceGeneratorTestHelper.CompileReference<IocSourceGenerator>(Library, new() { AssemblyName = "Lib" });
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(source);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            parsed,
            new()
            {
                References = [projectReference ? library.CompilationReference : library.Image],
                GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" },
            });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    [Test]
    public async Task InjectMember_FieldPropertyAndMethod_InjectedInBothOutputs()
    {
        GeneratorTestResult result = await RunAsync(Usings + Services + """
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize))]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Log), Key = "main")]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Required))]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Field))]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.BaseClock))]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Hidden))]
            [assembly: IocServiceCollectionExtensions(Name = "App")]

            """ + Types + """
            namespace App
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task InjectMember_InitializerAndNullable_StillRequired()
    {
        // A member of a type that the user does not own is always required (Semantics.spec, "Dependency resolution")
        GeneratorTestResult result = await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Log))]

            namespace App
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class {|SGIOC402:AppContainer|};
            }
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task InjectMember_TypeWithSeveralKeys_InjectsEachDefinition()
    {
        GeneratorTestResult result = await RunAsync(Usings + Services + """
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton, Key = "a")]
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton, Key = "b")]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize))]
            [assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Required))]

            """ + Types + """
            namespace App
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source.Split("instance.Initialize(").Length - 1).IsEqualTo(2);
    }

    [Test]
    public async Task InjectMember_OrderedWithInjectMembers_BaseFirst()
    {
        // Members of IocInjectMember are sorted with [IocInject] members (Annotations.spec, "Order")
        GeneratorTestResult result = await RunAsync(Usings + Services + """
            [assembly: IocInjectMember(typeof(App.Derived), nameof(App.Derived.BaseClock))]
            [assembly: IocInjectMember(typeof(App.Derived), nameof(App.Derived.Last))]

            """ + Types + """
            namespace App
            {
                [IocRegister(ServiceLifetime.Singleton)]
                public sealed class Derived : Lib.ExternalBase
                {
                    [IocInject] public Lib.IClock First { get; set; } = null!;
                    public Lib.IClock Last { get; set; } = null!;
                }

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        int baseClock = source.IndexOf("instance.BaseClock =", StringComparison.Ordinal);
        int first = source.IndexOf("instance.First =", StringComparison.Ordinal);
        int last = source.IndexOf("instance.Last =", StringComparison.Ordinal);
        await Assert.That(baseClock).IsGreaterThan(-1);
        await Assert.That(baseClock < first && first < last).IsTrue();
    }

    [Test]
    [Arguments("{|SGIOC011:\"Missing\"|}")]
    [Arguments("{|SGIOC011:nameof(Lib.External.Changed)|}")]
    [Arguments("{|SGIOC011:nameof(Lib.External.Nested)|}")]
    [Arguments("{|SGIOC011:nameof(Lib.External.Static)|}")]
    [Arguments("{|SGIOC010:nameof(Lib.External.Overloaded)|}")]
    public async Task InjectMember_MemberNotFoundOrOverloaded_DeclarationSkipped(string member)
    {
        await RunLookupAsync("Lib.External", member);
    }

    [Test]
    [Arguments("{|SGIOC011:\"BaseClock\"|}")]
    [Arguments("{|SGIOC011:\"Hidden\"|}")]
    public async Task InjectMember_NestedTypeOrStaticMemberHidesTheBaseMember_NotFound(string member)
    {
        // C# member lookup stops at the first level that declares the name (Annotations.spec, "Member lookup by name")
        await RunLookupAsync("Lib.HidesWithType", member);
    }

    private static async Task RunLookupAsync(string type, string member)
    {
        // The skipped declaration is an error node: the dependent User reports no SGIOC402, and the container is not
        // generated (Diagnostics.spec, "Consequences")
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [assembly: IocRegisterFor(typeof({{type}}), ServiceLifetime.Singleton)]
            [assembly: IocInjectMember(typeof({{type}}), {{member}})]

            namespace App
            {
                [IocRegister(ServiceLifetime.Transient)]
                public sealed class User({{type}} external);

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    [Arguments("[assembly: {|SGIOC020:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize), Key = \"a\")|}]")]
    [Arguments("[assembly: {|SGIOC016:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Generic))|}]")]
    [Arguments("[assembly: IocInjectMember(typeof(Lib.External), nameof(Lib.External.Log), KeyType = KeyType.Csharp, {|SGIOC011:Key = \"literal\"|})]")]
    public async Task InjectMember_InvalidSetting_DeclarationSkipped(string attribute)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            {{attribute}}

            namespace App
            {
                [IocRegister(ServiceLifetime.Transient)]
                public sealed class User(Lib.External external);

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC024_MemberAlreadyMarked_DeclarationSkipped()
    {
        GeneratorTestResult result = await RunAsync(Usings + Services + """
            [assembly: IocRegisterFor(typeof(Lib.Marked), ServiceLifetime.Singleton)]
            [assembly: {|SGIOC024:IocInjectMember(typeof(Lib.Marked), nameof(Lib.Marked.Clock))|}]

            """ + Types + """
            namespace App
            {
                [IocRegister(ServiceLifetime.Transient)]
                public sealed class User(Lib.Marked marked);

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task SGIOC021_NoRegistrationOfTheType_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [assembly: {|SGIOC021:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize))|}]
            """);

        await Assert.That(result.Diagnostics.Single().GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo("'Lib.External' has no registration attribute in the same assembly and scope as this [IocInjectMember]");
    }

    [Test]
    public async Task SGIOC021_RegistrationSkippedForAnotherReason_NotReported()
    {
        // A registration attribute that gives no declaration (SGIOC003, or a shape of a later slice) still counts
        await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(Lib.Abstract), ServiceLifetime.Singleton)]
            [assembly: IocInjectMember(typeof(Lib.Abstract), nameof(Lib.Abstract.Clock))]
            [assembly: IocInjectMember(typeof(App.Svc), nameof(App.Svc.Clock))]

            namespace App
            {
                [IocRegister(ServiceLifetime.Singleton)]
                public sealed class Svc
                {
                    public Lib.IClock Clock { get; set; } = null!;

                    [IocInject]
                    public Task InitAsync() => Task.CompletedTask;
                }
            }
            """.Replace("typeof(Lib.Abstract), ServiceLifetime", "{|SGIOC003:typeof(Lib.Abstract)|}, ServiceLifetime", StringComparison.Ordinal));
    }

    [Test]
    public async Task SGIOC023_ServiceKeyParameterOnRegistrationWithoutKey_KeyedDefinitionStillGenerated()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(Lib.WithServiceKey), ServiceLifetime.Singleton)]
            [assembly: IocRegisterFor(typeof(Lib.WithServiceKey), ServiceLifetime.Singleton, Key = "k")]
            [assembly: {|SGIOC023:IocInjectMember(typeof(Lib.WithServiceKey), nameof(Lib.WithServiceKey.Init))|}]

            namespace App
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        // Only the definition without a key is skipped
        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source.Split("instance.Init(").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC023_EveryRegistrationHasAKey_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(Lib.WithServiceKey), ServiceLifetime.Singleton, Key = "k")]
            [assembly: IocInjectMember(typeof(Lib.WithServiceKey), nameof(Lib.WithServiceKey.Init))]

            namespace App
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("instance.Init(");
    }

    [Test]
    public async Task InjectMember_SameMemberTwice_InjectedOnce()
    {
        GeneratorTestResult result = await RunAsync(Usings + Services + """
            [assembly: IocRegisterFor(typeof(Lib.WithServiceKey), ServiceLifetime.Singleton, Key = "k")]
            [assembly: IocInjectMember(typeof(Lib.WithServiceKey), nameof(Lib.WithServiceKey.Clock))]
            [assembly: IocInjectMember(typeof(Lib.WithServiceKey), nameof(Lib.WithServiceKey.Clock))]

            """ + Types + """
            namespace App
            {
                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await result.VerifyCompilableAsync();
        string source = result.GetGeneratedSource("AppContainer.g.cs")!;
        await Assert.That(source.Split("instance.Clock =").Length - 1).IsEqualTo(1);
    }

    [Test]
    [Arguments("member: {|SGIOC011:\"Missing\"|}, implementationType: typeof(Lib.External)")]
    [Arguments("implementationType: typeof(Lib.External), {|SGIOC011:\"Missing\"|}")]
    public async Task SGIOC011_NamedArguments_ReportedAtTheMemberArgument(string arguments)
    {
        await RunAsync(Usings + $$"""
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            [assembly: IocInjectMember({{arguments}})]
            """);
    }

    [Test]
    public async Task SGIOC024_SameMemberWithAnotherKey_ReportedAtEachAttribute()
    {
        // One member MUST NOT have two settings (Annotations.spec, "Types that the user does not own")
        GeneratorTestResult result = await RunAsync(Usings + Services + """
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            [assembly: {|SGIOC024:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Log), Key = "main")|}]
            [assembly: {|SGIOC024:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Log))|}]

            """ + Types + """
            namespace App
            {
                [IocRegister(ServiceLifetime.Transient)]
                public sealed class User(Lib.External external);

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class AppContainer;
            }
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
        await Assert.That(result.Diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .IsEqualTo("Member 'Lib.External.Log' already has another injection setting: another [IocInjectMember]");
    }

    [Test]
    public async Task SGIOC025_OnPlainClass_Reported()
    {
        await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]

            namespace App
            {
                [{|SGIOC025:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize))|}]
                public sealed class Plain;
            }
            """);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SGIOC302_CaptiveThroughInjectedMember_ReportedAtTheAttribute(bool projectReference)
    {
        // The member is in another assembly, so the site is the IocInjectMember attribute in both builds
        await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(App.SystemClock), ServiceLifetime.Scoped, ServiceTypes = [typeof(Lib.IClock)])]
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            [assembly: {|SGIOC302:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize))|}]

            namespace App
            {
                public sealed class SystemClock : Lib.IClock;
            }
            """, projectReference);
    }

    [Test]
    public async Task InjectMember_Message_NamesTheMemberAndTheKey()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [assembly: IocRegisterFor(typeof(Lib.External), ServiceLifetime.Singleton)]
            [assembly: {|SGIOC020:IocInjectMember(typeof(Lib.External), nameof(Lib.External.Initialize), Key = 1)|}]
            [assembly: IocRegisterFor(typeof(Lib.Marked), ServiceLifetime.Singleton)]
            [assembly: {|SGIOC024:IocInjectMember(typeof(Lib.Marked), nameof(Lib.Marked.Clock))|}]
            [assembly: IocInjectMember(typeof(Lib.External), {|SGIOC010:nameof(Lib.External.Overloaded)|})]
            [assembly: IocInjectMember(typeof(Lib.External), {|SGIOC011:"Missing"|})]
            """);

        string[] messages = [.. result.Diagnostics.OrderBy(static d => d.Id).Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))];
        await Assert.That(messages[0]).IsEqualTo("'Lib.External.Overloaded' has multiple overloads; nameof cannot choose one");
        await Assert.That(messages[1]).IsEqualTo("Member must be a nameof() expression that refers to a field, property, or method of 'Lib.External'");
        await Assert.That(messages[2]).IsEqualTo("Key cannot be specified for method 'Lib.External.Initialize'; use a factory registration");
        await Assert.That(messages[3]).IsEqualTo("Member 'Lib.Marked.Clock' already has another injection setting: [IocInject]");
    }
}
