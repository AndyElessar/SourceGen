using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC040: the Key of an open generic registration uses the implementation's type parameters, so the generator
/// does not register the open registration, only the closed types discovered at compile time.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC040)]
public class SGIOC040Tests
{
    private static string CreateSource(string registration) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface INode<T>;

        public static class Keys
        {
            public const string Name = "node";
        }

        public static class Keys<T>
        {
            public const string Name = "node";
        }

        {{registration}}
        """;

    [Test]
    [Arguments("ImplementationMember", "[IocRegister(ServiceTypes = [typeof(INode<>)], Key = nameof(Name), KeyType = KeyType.Csharp)]", "nameof(Name)")]
    [Arguments("OtherGenericType", "[IocRegister(ServiceTypes = [typeof(INode<>)], Key = nameof(Keys<T>.Name), KeyType = KeyType.Csharp)]", "nameof(Keys<T>.Name)")]
    [Arguments("UnboundImplementation", "[IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], Key = nameof(Node<>.Name), KeyType = KeyType.Csharp)]", "nameof(Node<>.Name)")]
    [Arguments("CsharpString", "[IocRegister(ServiceTypes = [typeof(INode<>)], Key = \"global::TestNamespace.Keys<T>.Name\", KeyType = KeyType.Csharp)]", "\"global::TestNamespace.Keys<T>.Name\"")]
    public async Task SGIOC040_KeyUsesImplementationTypeParameters_ReportsWarning(string scenario, string attribute, string key)
    {
        var source = CreateSource($$"""
            {{attribute}}
            public sealed class Node<T> : INode<T>
            {
                public const string Name = "node";
            }
            """);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc040 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC040).ToList();

        await Assert.That(sgioc040).Count().IsEqualTo(1);
        await Assert.That(sgioc040[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(sgioc040[0].GetMessage()).Contains($"'{key}'").And.Contains("'Node<T>'");
        await Assert.That(sgioc040[0].Location.SourceTree!.GetText().ToString(sgioc040[0].Location.SourceSpan)).IsEqualTo(key);
    }

    [Test]
    [Arguments("NonGenericKey", "[IocRegister(ServiceTypes = [typeof(INode<>)], Key = nameof(Keys.Name), KeyType = KeyType.Csharp)] public sealed class Node<T> : INode<T>;")]
    [Arguments("NameofTypeParameter", "[IocRegister(ServiceTypes = [typeof(INode<>)], Key = \"nameof(T)\", KeyType = KeyType.Csharp)] public sealed class Node<T> : INode<T>;")]
    [Arguments("ValueKey", "[IocRegister(ServiceTypes = [typeof(INode<>)], Key = nameof(Keys<T>.Name))] public sealed class Node<T> : INode<T>;")]
    [Arguments("ClosedRegistration", "[IocRegister(ServiceTypes = [typeof(INode<string>)], Key = nameof(Keys<int>.Name), KeyType = KeyType.Csharp)] public sealed class Node : INode<string>;")]
    [Arguments("Instance", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Key = nameof(Keys<T>.Name), KeyType = KeyType.Csharp, Instance = nameof(Default))] public sealed class Node<T> : INode<T> { public static readonly Node<T> Default = new(); }")]
    public async Task SGIOC040_KeyRegisteredOpen_NoDiagnostic(string scenario, string registration)
    {
        // A key without the implementation's type parameters is registered open; a value key is the member's name; an
        // instance is never registered open
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(registration));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC040)).IsEmpty();
    }
}
