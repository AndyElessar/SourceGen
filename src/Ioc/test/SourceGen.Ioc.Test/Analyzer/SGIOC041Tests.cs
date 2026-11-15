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
