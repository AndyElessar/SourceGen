using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC041: a Factory, Instance or C# Key given with nameof() is a member of an unbound generic type that the
/// implementation type does not declare, so the generator ignores the registration.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC041)]
public class SGIOC041Tests
{
    private static string CreateSource(string registration) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface IMyService;
        public interface INode<T>;

        public static class Factories<T>
        {
            public const string Name = "name";

            public static readonly MyService Default = new();

            public static MyService Create() => new();
        }

        public sealed class MyService : IMyService;

        {{registration}}
        """;

    [Test]
    [Arguments("Factory", "[IocRegisterFor(typeof(MyService), ServiceTypes = [typeof(IMyService)], Factory = nameof(Factories<>.Create))] public static class Config;", "nameof(Factories<>.Create)", "Create")]
    [Arguments("Instance", "[IocRegisterFor<MyService>(Lifetime = ServiceLifetime.Singleton, Instance = nameof(Factories<>.Default))] public static class Config;", "nameof(Factories<>.Default)", "Default")]
    [Arguments("Key", "[IocRegisterFor(typeof(MyService), Key = nameof(Factories<>.Name), KeyType = KeyType.Csharp)] public static class Config;", "nameof(Factories<>.Name)", "Name")]
    [Arguments("ClosedImplementationOwnType", "[IocRegisterFor(typeof(Node<int>), ServiceTypes = [typeof(INode<int>)], Factory = nameof(Node<>.Create))] public static class Config; public sealed class Node<T> : INode<T> { public static Node<T> Create() => new(); }", "nameof(Node<>.Create)", "Create")]
    [Arguments("GenericRegistration", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<>.Create))] public sealed class Node<T> : INode<T>;", "nameof(Factories<>.Create)", "Create")]
    public async Task SGIOC041_MemberOfUnboundGenericType_ReportsError(string scenario, string registration, string argument, string member)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(registration));
        var sgioc041 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC041).ToList();

        await Assert.That(sgioc041).Count().IsEqualTo(1);
        await Assert.That(sgioc041[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        var containingType = argument["nameof(".Length..argument.IndexOf('<')];
        await Assert.That(sgioc041[0].GetMessage()).Contains($"'{member}'").And.Contains($"'{containingType}<T>'");
        await Assert.That(sgioc041[0].Location.SourceTree!.GetText().ToString(sgioc041[0].Location.SourceSpan)).IsEqualTo(argument);
    }

    [Test]
    [Arguments("Assembly", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.Factories<>.Create))]")]
    [Arguments("TypeLevel", "[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))] public static class Config;")]
    [Arguments("Generic", "[IocRegisterDefaults<IMyService>(ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))] public static class Config;")]
    [Arguments("OpenGenericTarget", "[IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(NodeFactories<>.Create))] public static class Config; public static class NodeFactories<T> { public static INode<T> Create() => throw new NotImplementedException(); }")]
    [Arguments("TargetOwnType", "[IocRegisterDefaults(typeof(Node<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(Node<>)], Factory = nameof(Node<>.Create))] public static class Config; public sealed class Node<T> : INode<T> { public static Node<T> Create() => new(); }")]
    public async Task SGIOC041_DefaultSettings_ReportsError(string scenario, string defaults)
    {
        // Default settings apply to registrations of other types, so even the unbound target type is not closed
        var source = scenario == "Assembly"
            ? CreateSource("[IocRegister] public sealed class Registered : IMyService;").Replace("namespace TestNamespace;", defaults + "\nnamespace TestNamespace;")
            : CreateSource(defaults);
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc041 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC041).ToList();

        await Assert.That(sgioc041).Count().IsEqualTo(1);
        await Assert.That(sgioc041[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(sgioc041[0].GetMessage()).Contains("'Create'").And.Contains("default settings are ignored");
        var location = sgioc041[0].Location.SourceTree!.GetText().ToString(sgioc041[0].Location.SourceSpan);
        await Assert.That(location).StartsWith("nameof(").And.EndsWith("<>.Create)");
    }

    [Test]
    [Arguments("[IocRegisterDefaults(targetServiceType: typeof(IMyService), lifetime: ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))] public static class Config;")]
    [Arguments("[IocRegisterDefaults(lifetime: ServiceLifetime.Singleton, targetServiceType: typeof(IMyService), Factory = nameof(Factories<>.Create))] public static class Config;")]
    public async Task SGIOC041_DefaultSettings_NamedTargetArgument_ReportsError(string defaults)
    {
        // The target is read like the generator, whatever order the constructor arguments are named in
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(defaults));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC041)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC041_DefaultSettings_AreIgnoredByOtherAnalyses()
    {
        // Like the generator, the ignored default settings do not apply, so the later settings for the same target type
        // are not duplicated (SGIOC012)
        var source = CreateSource("""
            [IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))]
            [IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton)]
            public static class Config;

            [IocRegister] public sealed class Registered : IMyService;
            """);
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC041)).Count().IsEqualTo(1);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC012)).IsEmpty();
    }

    [Test]
    [Arguments("ClosedType", "[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Singleton, Factory = nameof(Factories<int>.Create))] public static class Config;")]
    [Arguments("OpenNestedTarget", "public static class Outer<T> { public interface INested; } [IocRegisterDefaults(typeof(Outer<>.INested), ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))] public static class Config;")]
    public async Task SGIOC041_DefaultSettings_ClosedTypeOrIgnoredSettings_NoDiagnostic(string scenario, string defaults)
    {
        // A closed type keeps its type arguments, and default settings for an open type nested in a generic type are
        // ignored as a whole (SGIOC038)
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(defaults));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC041)).IsEmpty();
    }

    [Test]
    [Arguments("ClosedType", "[IocRegisterFor(typeof(MyService), ServiceTypes = [typeof(IMyService)], Factory = nameof(Factories<int>.Create))] public static class Config;")]
    [Arguments("ValueKey", "[IocRegisterFor(typeof(MyService), Key = nameof(Factories<>.Name))] public static class Config;")]
    [Arguments("OwnUnboundType", "[IocRegisterFor(typeof(Node<>), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Instance = nameof(Node<>.Default))] public static class Config; public sealed class Node<T> : INode<T> { public static readonly Node<T> Default = new(); }")]
    public async Task SGIOC041_MemberOfClosedOrOwnType_NoDiagnostic(string scenario, string registration)
    {
        // A closed type keeps its type arguments, a value key is the member's name, and the implementation's own unbound
        // type is closed with its type arguments
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(registration));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC041)).IsEmpty();
    }
}
