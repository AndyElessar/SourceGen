using System.Globalization;

namespace SourceGen.Ioc.Test.Generator;

/// <summary>
/// Keys on registrations (Annotations.spec, "Keys"; Analysis.Model.spec, "Identity" and "Ordering"; Container.Output.spec,
/// "Lookup by Type"; ServiceCollection.Output.spec, "Registration for each lifetime").
/// </summary>
public class KeyedRegistrationTests
{
    private const string Usings = """
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace App;

        """;

    private const string Services = Usings + """
        public interface IRepo;
        public enum Color { Red, Green, Blue }

        public static class Keys
        {
            public const string Main = "main";
            public static readonly object Backup = new();
        }

        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "a")]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = 1L)]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = Color.Blue)]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], KeyType = KeyType.Csharp, Key = nameof(Keys.Main))]
        [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], KeyType = KeyType.Csharp, Key = nameof(Keys.Backup))]
        public sealed class Repo : IRepo;

        [IocRegister(ServiceLifetime.Transient, ServiceTypes = [typeof(IRepo)])]
        public sealed class DefaultRepo : IRepo;

        """;

    private static async Task<GeneratorTestResult> RunAsync(params string[] sources)
    {
        (IReadOnlyList<string> parsed, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(sources);
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            parsed,
            new() { GlobalOptions = new Dictionary<string, string> { ["build_property.RootNamespace"] = "App" } });
        await result.VerifyDiagnosticsAsync(expected);
        return result;
    }

    private static string Message(GeneratorTestResult result, string id) => result.ReportedDiagnostics.First(d => d.Id == id).GetMessage(CultureInfo.InvariantCulture);

    [Test]
    public async Task KeyedRegistration_Container_GeneratesTheKeyedLookup()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Verify(SnapshotHelper.Members(result.GetGeneratedSource("AppContainer.g.cs")!, "__lookup", "__keyedLookup", "__collections", "__BuildKeyedLookup", "GetKeyedService", "IsKeyedService"));
    }

    [Test]
    public async Task KeyedRegistration_ContainerWithFallbackProvider_MergesKeyedCollections()
    {
        GeneratorTestResult result = await RunAsync(Services + """
            [IocContainer]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Verify(SnapshotHelper.Members(result.GetGeneratedSource("AppContainer.g.cs")!, "__collections", "__BuildKeyedLookup", "GetKeyedService", "__MergeKeyed"));
    }

    [Test]
    public async Task KeyedRegistration_ServiceCollectionExtension_UsesTheKeyedMethods()
    {
        GeneratorTestResult result = await RunAsync(Services, "[assembly: SourceGen.Ioc.IocServiceCollectionExtensions(Name = \"App\")]");

        await result.VerifyCompilableAsync();
        await Verify(result.GetGeneratedSource("AppServiceCollectionExtensions.g.cs"));
    }

    [Test]
    public async Task KeyedRegistration_ConstNullKey_SameAsNoKey()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public static class Keys
            {
                public const string? None = null;
            }

            [IocRegister(ServiceLifetime.Singleton, Key = null)]
            [IocRegister(ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(Keys.None))]
            public sealed class Service;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Service service);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        // One instance definition without a key: Consumer resolves it without SGIOC401, and the keyed table is empty
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).DoesNotContain("__AddKeyed(");
    }

    [Test]
    public async Task SGIOC402_UnkeyedDependencyOnlyKeyedRegistration_Missing()
    {
        await RunAsync(Usings + """
            public interface IRepo;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRepo)], Key = "a")]
            public sealed class Repo : IRepo;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IRepo repo);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class {|SGIOC402:AppContainer|};
            """);
    }

    [Test]
    public async Task SGIOC001_SameKeyDifferentLifetime_ReportedAndDifferentKeysMayDiffer()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [{|SGIOC001:IocRegister(ServiceLifetime.Singleton, Key = "a")|}]
            [{|SGIOC001:IocRegister(ServiceLifetime.Scoped, Key = "a")|}]
            [IocRegister(ServiceLifetime.Transient, Key = "b")]
            public sealed class Service;
            """);

        await Assert.That(Message(result, "SGIOC001")).IsEqualTo("'App.Service (Key: \"a\")' is registered with different Lifetime; all attributes for the same implementation and key must agree");
    }

    [Test]
    public async Task SGIOC007_AnyKey_Reported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton, KeyType = KeyType.Csharp, {|SGIOC007:Key = nameof(KeyedService.AnyKey)|})]
            public sealed class Service;
            """);

        await Assert.That(Message(result, "SGIOC007")).IsEqualTo("KeyedService.AnyKey cannot be used as a registration key");
    }

    [Test]
    public async Task SGIOC007_MemberNamedAnyKeyOfTheUser_NotReported()
    {
        GeneratorTestResult result = await RunAsync(Usings + """
            public static class MyKeys
            {
                public static readonly object AnyKey = new();
            }

            [IocRegister(ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(MyKeys.AnyKey))]
            public sealed class Service;

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await result.VerifyCompilableAsync();
        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("object k0 = global::App.MyKeys.AnyKey;");
    }

    [Test]
    [Arguments("KeyType = KeyType.Csharp, {|SGIOC007:Key = nameof(KeyedService.AnyKey)|}")]
    [Arguments("KeyType = KeyType.Csharp, {|SGIOC011:Key = \"literal\"|}")]
    [Arguments("{|SGIOC022:Key = typeof(string)|}")]
    public async Task KeyError_DeclarationSkipped_NoOtherDiagnosticAndNoContainer(string key)
    {
        // The skipped instance definition is an error node: a dependency on it causes no SGIOC402, and the container
        // is not generated (Diagnostics.spec, "Consequences")
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Singleton, {{key}})]
            public sealed class Service;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Service service);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).IsNull();
    }

    [Test]
    public async Task KeyError_ValidDeclarationWithoutKey_NotSkipped()
    {
        // The key of the invalid declaration is unknown, so it belongs to no instance definition that another
        // declaration has. The declaration without a key stays valid.
        GeneratorTestResult result = await RunAsync(Usings + """
            [IocRegister(ServiceLifetime.Singleton)]
            [IocRegister(ServiceLifetime.Singleton, {|SGIOC022:Key = typeof(string)|})]
            public sealed class Service;

            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(Service service);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class AppContainer;
            """);

        await Assert.That(result.GetGeneratedSource("AppContainer.g.cs")).Contains("new global::App.Consumer(");
    }

    [Test]
    [Arguments("Key = \"literal\"")]
    [Arguments("Key = nameof(Keys.Instance)")]
    [Arguments("Key = nameof(Keys.Method)")]
    [Arguments("Key = nameof(Keys)")]
    public async Task SGIOC011_CsharpKeyNotAStaticMember_Reported(string key)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            public class Keys
            {
                public string Instance = "x";
                public static string Method() => "x";
            }

            [IocRegister(ServiceLifetime.Singleton, KeyType = KeyType.Csharp, {|SGIOC011:{{key}}|})]
            public sealed class Service;
            """);

        await Assert.That(Message(result, "SGIOC011")).IsEqualTo("Key must be a nameof() expression that refers to a static field, property, or constant");
    }

    [Test]
    [Arguments("Key = typeof(string)", "System.Type")]
    [Arguments("Key = new[] { 1, 2 }", "int[]")]
    public async Task SGIOC022_UnsupportedKeyValue_Reported(string key, string type)
    {
        GeneratorTestResult result = await RunAsync(Usings + $$"""
            [IocRegister(ServiceLifetime.Singleton, {|SGIOC022:{{key}}|})]
            public sealed class Service;
            """);

        await Assert.That(Message(result, "SGIOC022")).IsEqualTo($"A key of type '{type}' is not supported");
    }
}
