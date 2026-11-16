using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC042: the closed registrations of an open generic registration call its Factory, so the generator does not
/// register the open registration, which MS.DI would construct without the factory.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC042)]
public class SGIOC042Tests
{
    private const string Declarations = """
        public interface INode<T>;
        public interface IRequestHandler<TResponse>;

        public static class Factories
        {
            public static object CreateNode(IServiceProvider sp) => throw new NotImplementedException();

            [IocGenericFactory(typeof(INode<int>), typeof(int))]
            public static INode<T> CreateServiceType<T>() => throw new NotImplementedException();

            [IocGenericFactory(typeof(Node<int>), typeof(int))]
            public static Node<T> CreateImplementation<T>() => throw new NotImplementedException();

            [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
            public static IRequestHandler<Task<T>> CreateHandler<T>() => throw new NotImplementedException();

            public static INode<T> CreateUnmapped<T>() => throw new NotImplementedException();
        }

        public static class Factories<T>
        {
            public static Node<T> Create() => new();
        }
        """;

    private static string CreateSource(string assemblyAttributes, string registration) => $$"""
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;
        using TestNamespace;

        {{assemblyAttributes}}

        namespace TestNamespace;

        {{Declarations}}

        {{registration}}
        """;

    private const string Node = """
        public sealed class Node<T> : INode<T>
        {
            public const string Name = "node";

            public static readonly Node<T> Default = new();

            public static Node<T> Create() => new();
        }
        """;

    [Test]
    [Arguments("OwnFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Create))]", "Node<T>.Create")]
    [Arguments("OtherGenericType", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<T>.Create))]", "Factories<T>.Create")]
    [Arguments("StringFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = \"global::TestNamespace.Node<T>.Create\")]", "global::TestNamespace.Node<T>.Create")]
    [Arguments("RegisterFor", "[assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], Factory = nameof(Node<>.Create))]", "", "Node<T>.Create")]
    [Arguments("DefaultFactory", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateNode))]", "[IocRegister]", "Factories.CreateNode")]
    [Arguments("TypeLevelDefaultFactory", "", "[IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateNode))]\npublic sealed class Holder;\n\n[IocRegister]", "Factories.CreateNode")]
    [Arguments("GenericFactoryOfServiceType", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories.CreateServiceType))]", "Factories.CreateServiceType<T>")]
    [Arguments("GenericFactoryOfImplementation", "", "[IocRegister(Factory = nameof(Factories.CreateImplementation))]", "Factories.CreateImplementation<T>")]
    [Arguments("DefaultsImplementationTypes", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateNode), ImplementationTypes = [typeof(Node<>)])]", "", "Factories.CreateNode")]
    [Arguments("DefaultsImplementationTypesGenericFactory", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateServiceType), ImplementationTypes = [typeof(Node<>)])]", "", "Factories.CreateServiceType<T>")]
    public async Task SGIOC042_OpenGenericRegistrationCreatedByFactory_ReportsWarning(string scenario, string assemblyAttributes, string attribute, string factory)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(assemblyAttributes, $"{attribute}\n{Node}"));
        var sgioc042 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC042).ToList();

        await Assert.That(sgioc042).Count().IsEqualTo(1);
        await Assert.That(sgioc042[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(sgioc042[0].Descriptor.CustomTags).Contains(WellKnownDiagnosticTags.CompilationEnd);
        await Assert.That(sgioc042[0].GetMessage()).Contains("'Node<T>'").And.Contains($"'{factory}'");
    }

    [Test]
    [Arguments("NoFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)])]\n" + Node)]
    [Arguments("ClosedRegistration", "", "[IocRegister(ServiceTypes = [typeof(INode<string>)], Factory = nameof(Factories.CreateNode))]\npublic sealed class StringNode : INode<string>;\n" + Node)]
    [Arguments("Instance", "", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Instance = nameof(Default))]\n" + Node)]
    [Arguments("InstanceWithDefaultFactory", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateNode))]", "[IocRegister(Instance = nameof(Default))]\n" + Node)]
    [Arguments("KeyUsesTypeParameters", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Create), Key = nameof(Name), KeyType = KeyType.Csharp)]\n" + Node)]
    [Arguments("UnboundGenericFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<>.Create))]\n" + Node)]
    [Arguments("NonPublicConstructor", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Create))]\npublic sealed class Node<T> : INode<T> { internal Node() { } public static Node<T> Create() => new(); }")]
    [Arguments("GenericFactoryOfConstructedImplementation", "", "[IocRegister(RegisterAllInterfaces = true, Factory = nameof(Factories.CreateHandler))]\npublic sealed class Handler<T> : IRequestHandler<Task<T>>;\n" + Node)]
    [Arguments("GenericFactoryWithoutMapping", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories.CreateUnmapped))]\n" + Node)]
    public async Task SGIOC042_OpenRegistrationNotOmittedForFactory_NoDiagnostic(string scenario, string assemblyAttributes, string registration)
    {
        // No factory, a closed registration, an instance (which wins over any Factory), a key that differs per closed type
        // (SGIOC040), an ignored registration (SGIOC041), an implementation type MS.DI cannot construct, a generic factory
        // that never creates an open registered type, and one that is never called (SGIOC016) omit no open registration for
        // the factory
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(assemblyAttributes, registration));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC042)).IsEmpty();
    }

    [Test]
    public async Task SGIOC042_RegisterFeatureDisabled_NoDiagnostic()
    {
        // Only the Register output registers open generic registrations
        var source = CreateSource("", $"[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Create))]\n{Node}");
        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Container,PropertyInject,FieldInject,MethodInject"
        };

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source, analyzerConfigOptions: analyzerConfigOptions);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC042)).IsEmpty();
    }

    [Test]
    [Arguments("OwnFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Create))]\n" + Node)]
    [Arguments("NoFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)])]\n" + Node)]
    [Arguments("GenericFactoryOfServiceType", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories.CreateServiceType))]\n" + Node)]
    [Arguments("GenericFactoryOfConstructedImplementation", "", "[IocRegister(RegisterAllInterfaces = true, Factory = nameof(Factories.CreateHandler))]\npublic sealed class Handler<T> : IRequestHandler<Task<T>>;\n" + Node)]
    [Arguments("GenericFactoryWithoutMapping", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories.CreateUnmapped))]\n" + Node)]
    [Arguments("DefaultsImplementationTypes", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateNode), ImplementationTypes = [typeof(Node<>)])]", Node)]
    [Arguments("DefaultsImplementationTypesConstructedByGenericFactory", "[assembly: IocRegisterDefaults(typeof(IRequestHandler<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateHandler), ImplementationTypes = [typeof(Handler<>)])]", "public sealed class Handler<T> : IRequestHandler<Task<T>>;\n" + Node)]
    [Arguments("DefaultsImplementationTypesGenericFactoryOfServiceType", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories.CreateServiceType), ImplementationTypes = [typeof(Node<>)])]", Node)]
    public async Task SGIOC042_ReportedExactlyWhenGeneratorOmitsOpenRegistrationForFactory(string scenario, string assemblyAttributes, string registration)
    {
        // Parity: the open registrations the generator emits without the factory, compared with those it emits with it
        var source = CreateSource(assemblyAttributes, registration);
        var withoutFactory = CreateSource(RemoveFactory(assemblyAttributes), RemoveFactory(registration));

        static string RemoveFactory(string attributes) => Regex.Replace(attributes, @", Factory = nameof\([^)]*\)", string.Empty);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var reported = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC042).Any();

        await Assert.That(CountOpenRegistrations(source) < CountOpenRegistrations(withoutFactory)).IsEqualTo(reported);

        static int CountOpenRegistrations(string source)
        {
            var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
            return Regex.Matches(SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration") ?? string.Empty, @"typeof\([^)]*<,*>\), typeof\(").Count;
        }
    }
}
