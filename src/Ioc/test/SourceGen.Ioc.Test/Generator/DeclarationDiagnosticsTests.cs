using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// The checks of a single declaration (Diagnostics.spec, "Errors in a single declaration"): location, message, and that
/// the declaration is skipped while the others are still generated.
/// </summary>
public class DeclarationDiagnosticsTests
{
    private const string Header = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        public interface IFoo;
        public interface IHandler<T>;
        public delegate void Act();

        [IocRegister(ServiceLifetime.Singleton)]
        public sealed class Valid;

        """;

    private const string AssemblyHeader = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        [assembly: IocServiceCollectionExtensions(Name = "App")]

        """;

    private static readonly GeneratorRunOptions Options = new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } };

    private static async Task<GeneratorTestResult> VerifyAsync(string source, string assemblyAttributes = "")
    {
        (IReadOnlyList<string> sources, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(Header + source, AssemblyHeader + assemblyAttributes);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(sources, Options);

        await result.VerifyDiagnosticsAsync(expected);
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("global::App.Valid");
        return result;
    }

    private static string Message(GeneratorTestResult result, string id) => result.ReportedDiagnostics.First(d => d.Id == id).GetMessage(CultureInfo.InvariantCulture);

    [Test]
    [Arguments("public abstract class Bad;", "it is abstract")]
    [Arguments("public static class Bad;", "it is static")]
    public async Task SGIOC003_RegisterOnInvalidClass_ReportedOnAttribute(string declaration, string reason)
    {
        GeneratorTestResult result = await VerifyAsync($"[{{|SGIOC003:IocRegister(ServiceLifetime.Singleton)|}}]\n{declaration}");

        await Assert.That(Message(result, "SGIOC003")).IsEqualTo($"'App.Bad' cannot be an implementation or decorator type: {reason}");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).DoesNotContain("App.Bad");
    }

    [Test]
    public async Task SGIOC003_FileType_Reported()
    {
        GeneratorTestResult result = await VerifyAsync("[{|SGIOC003:IocRegister(ServiceLifetime.Singleton)|}] file sealed class Bad;");

        await Assert.That(Message(result, "SGIOC003")).IsEqualTo("'App.Bad' cannot be an implementation or decorator type: it uses a file type");
        await result.VerifyCompilableAsync();
    }

    [Test]
    [Arguments("private")]
    [Arguments("protected")]
    public async Task InaccessibleNestedType_CompilerReportsTheError(string accessibility)
    {
        // An error that the C# compiler reports is not a generator diagnostic (Constitution, "Diagnostic policy")
        GeneratorTestResult result = await VerifyAsync($"public class Outer {{ [IocRegister(ServiceLifetime.Singleton)] {accessibility} sealed class Bad; }}");

        await Assert.That(result.OutputCompilation.GetDiagnostics().Select(static d => d.Id)).Contains("CS0122");
    }

    [Test]
    public async Task InvalidDeclaration_SkipsTheCompleteInstanceDefinition()
    {
        GeneratorTestResult result = await VerifyAsync("""
            [IocRegister(ServiceLifetime.Scoped)]
            [IocRegister(ServiceLifetime.Scoped, ServiceTypes = [{|SGIOC004:typeof(int)|}])]
            public sealed class Two;
            """);

        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).DoesNotContain("App.Two");
    }

    [Test]
    [Arguments("IFoo", "it is not a class")]
    [Arguments("Act", "it is not a class")]
    public async Task SGIOC003_RegisterForInvalidType_ReportedOnTypeArgument(string type, string reason)
    {
        GeneratorTestResult result = await VerifyAsync(string.Empty, $"[assembly: IocRegisterFor({{|SGIOC003:typeof(App.{type})|}}, ServiceLifetime.Singleton)]");

        await Assert.That(Message(result, "SGIOC003")).EndsWith(reason);
    }

    [Test]
    public async Task SGIOC003_FileTypeInContainingTypeOrTypeArgument_Reported()
    {
        GeneratorTestResult result = await VerifyAsync(
            """
            file class FileOuter { [{|SGIOC003:IocRegister(ServiceLifetime.Singleton)|}] public sealed class Inner; }
            public sealed class Box<T>;
            file sealed class Hidden;
            [IocContainer]
            [IocRegisterFor({|SGIOC003:typeof(Box<Hidden>)|}, ServiceLifetime.Singleton)]
            public sealed partial class Host;
            """);

        await Assert.That(result.ReportedDiagnostics.All(static d => d.GetMessage(CultureInfo.InvariantCulture).EndsWith("it uses a file type", StringComparison.Ordinal))).IsTrue();
        await result.VerifyCompilableAsync();
    }

    [Test]
    public async Task SGIOC003_ArrayType_ReportedAsNotAClass()
    {
        GeneratorTestResult result = await VerifyAsync(string.Empty, "[assembly: IocRegisterFor({|SGIOC003:typeof(App.IFoo[])|}, ServiceLifetime.Singleton)]");

        await Assert.That(Message(result, "SGIOC003")).EndsWith("it is not a class");
    }

    [Test]
    public async Task SGIOC003_NamedArgumentsInOtherOrder_ReportedOnTypeArgument()
        => await VerifyAsync(string.Empty, "[assembly: IocRegisterFor(lifetime: ServiceLifetime.Singleton, implementationType: {|SGIOC003:typeof(App.IFoo)|})]");

    [Test]
    public async Task ImplementationType_ErrorType_NoGeneratorDiagnostic()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [Header, AssemblyHeader + "[assembly: IocRegisterFor(typeof(Missing), ServiceLifetime.Singleton)]"],
            Options with { AllowedInputErrors = new HashSet<string> { "CS0246" } });

        await Assert.That(result.ReportedDiagnostics.Where(static d => d.Id.StartsWith("SGIOC", StringComparison.Ordinal))).IsEmpty();
    }

    [Test]
    public async Task ServiceTypes_UnboundDefinitionOfTheImplementationItself_IsNotAnError()
    {
        GeneratorTestResult result = await VerifyAsync(string.Empty, "[assembly: IocRegisterFor(typeof(App.Box<int>), ServiceLifetime.Singleton, ServiceTypes = [typeof(App.Box<>)])]\nnamespace App { public sealed class Box<T>; }");

        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("AddKeyedSingleton<global::SourceGen.Ioc.IocHolder<global::App.Box<global::System.Int32>>>");
    }

    [Test]
    [Arguments("[IocInject] public Bad() { } [IocInject] public Bad(IFoo foo) { }", "multiple marked constructors")]
    [Arguments("public Bad() { } public Bad(IFoo foo) { }", "multiple public constructors and no primary constructor")]
    [Arguments("private Bad() { }", "no public constructor")]
    public async Task SGIOC002_NoSingleConstructor_ReportedOnAttribute(string constructors, string reason)
    {
        GeneratorTestResult result = await VerifyAsync($$"""
            [{|SGIOC002:IocRegister(ServiceLifetime.Transient)|}]
            public sealed class Bad
            {
                {{constructors}}
            }
            """);

        await Assert.That(Message(result, "SGIOC002")).IsEqualTo($"'App.Bad' has {reason}; mark exactly one constructor with [IocInject]");
    }

    [Test]
    [Arguments("int", "int", "it is a value type")]
    [Arguments("void", "void", "it is a pointer or void")]
    [Arguments("IServiceProvider", "System.IServiceProvider", "it is a built-in service")]
    [Arguments("IServiceScopeFactory", "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory", "it is a built-in service")]
    [Arguments("IHandler<>", "App.IHandler<>", "the implementation type has no base type or interface of this generic type")]
    public async Task SGIOC004_InvalidServiceType_ReportedOnTypeArgument(string serviceType, string name, string reason)
    {
        GeneratorTestResult result = await VerifyAsync($$"""
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo), {|SGIOC004:typeof({{serviceType}})|}])]
            public sealed class Bad : IFoo;
            """);

        await Assert.That(Message(result, "SGIOC004")).IsEqualTo($"'{name}' cannot be a service type of 'App.Bad': {reason}");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).DoesNotContain("App.Bad");
    }

    [Test]
    public async Task SGIOC004_ArrayCreationSyntax_ReportedOnElement()
        => await VerifyAsync("""
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = new[] { typeof(IFoo), {|SGIOC004:typeof(int)|} })]
            public sealed class Bad : IFoo;
            """);

    [Test]
    [Arguments("Lazy<IFoo>")]
    [Arguments("Func<IFoo>")]
    [Arguments("IEnumerable<IFoo>")]
    [Arguments("IFoo[]")]
    [Arguments("System.Threading.Tasks.Task<IFoo>")]
    [Arguments("KeyValuePair<string, IFoo>")]
    [Arguments("IReadOnlyDictionary<string, IFoo>")]
    public async Task SGIOC006_WrapperServiceType_Reported(string serviceType)
    {
        GeneratorTestResult result = await VerifyAsync($$"""
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [{|SGIOC006:typeof({{serviceType}})|}])]
            public sealed class Bad;
            """);

        await Assert.That(Message(result, "SGIOC006")).EndsWith("is generated automatically and cannot be registered; wrap it in a class or depend on it directly");
    }

    [Test]
    public async Task SGIOC006_UnboundWrapperFromInterfaces_Reported()
        => await VerifyAsync("""
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [{|SGIOC006:typeof(IEnumerable<>)|}])]
            public sealed class Bad : List<int>;
            """);

    [Test]
    public async Task SGIOC025_RegisterForOnPlainClass_ReportedAndNotRegistered()
    {
        GeneratorTestResult result = await VerifyAsync("""
            [{|SGIOC025:IocRegisterFor(typeof(Other), ServiceLifetime.Singleton)|}]
            public sealed class Plain;

            public sealed class Other;
            """);

        await Assert.That(Message(result, "SGIOC025")).IsEqualTo("[IocRegisterFor] on a class has effect only when the class has [IocModule] or [IocContainer]");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).DoesNotContain("App.Other");
    }

    [Test]
    [Arguments("IocContainer")]
    [Arguments("IocModule")]
    public async Task RegisterFor_OnContainerOrModuleClass_IsRegistered(string attribute)
    {
        GeneratorTestResult result = await VerifyAsync($$"""
            [{{attribute}}]
            [IocRegisterFor(typeof(Other), ServiceLifetime.Singleton)]
            public sealed partial class Host;

            public sealed class Other;
            """);

        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).Contains("global::App.Other");
    }

    [Test]
    public async Task SGIOC001_DifferentLifetimes_ReportedOnEachAttribute()
    {
        GeneratorTestResult result = await VerifyAsync(
            """
            [{|SGIOC001:IocRegister(ServiceLifetime.Scoped)|}]
            [{|SGIOC001:IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)])|}]
            public sealed class Bad : IFoo;
            """,
            "[assembly: {|SGIOC001:IocRegisterFor(typeof(App.Bad), ServiceLifetime.Scoped)|}]");

        await Assert.That(Message(result, "SGIOC001")).IsEqualTo("'App.Bad' is registered with different Lifetime; all attributes for the same implementation and key must agree");
        await Assert.That(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")).DoesNotContain("App.Bad");
    }

    [Test]
    public async Task ServiceTypes_UnboundGeneric_ExpandsToEachImplementedType()
    {
        GeneratorTestResult result = await VerifyAsync("""
            public class BaseHandler<T> : IHandler<T>;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>), typeof(BaseHandler<>)])]
            public sealed class Multi : BaseHandler<int>, IHandler<string>;
            """);

        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!;
        await result.VerifyCompilableAsync();
        await Assert.That(source).Contains("AddSingleton<global::App.IHandler<global::System.Int32>>(");
        await Assert.That(source).Contains("AddSingleton<global::App.IHandler<global::System.String>>(");
        await Assert.That(source).Contains("AddSingleton<global::App.BaseHandler<global::System.Int32>>(");
    }

    [Test]
    public async Task BuiltInService_ServiceProviderParameter_UsesSp()
    {
        GeneratorTestResult result = await VerifyAsync("""
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class UsesProvider(IServiceProvider provider, IServiceScopeFactory scopes);
            """);

        string source = result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs")!.Replace("\r\n", "\n");
        await result.VerifyCompilableAsync();
        await Assert.That(source).Contains("new global::App.UsesProvider(\n            sp,");
        await Assert.That(source).Contains("GetRequiredService<global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp)");
    }

    [Test]
    public async Task Diagnostics_CodeMoved_DoesNotRunAnalysisAgain()
    {
        const string Bad = "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(int)])] public sealed class Bad;";
        GeneratorTestResult first = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            [Header + Bad, AssemblyHeader],
            Options with { TrackSteps = true });

        GeneratorTestResult second = IncrementalTestHelper.RunAgainWithEdit(first, 0, "// moved\n" + Header + Bad);

        await IncrementalTestHelper.VerifyNotRunAgainAsync(second, TrackingNames.Analysis);
        await Assert.That(second.GetDiagnostic("SGIOC004").Location.GetLineSpan().StartLinePosition.Line)
            .IsEqualTo(first.GetDiagnostic("SGIOC004").Location.GetLineSpan().StartLinePosition.Line + 1);
    }
}
