using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC039: a service type of a registration with a generic factory does not match the service type template of
/// the factory's mapping, so the generator does not register it.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC039)]
public class SGIOC039Tests
{
    private const string Declarations = """
        public interface IRequestHandler<TResponse>;
        public interface IOther;

        public class Entity;

        public static class FactoryContainer
        {
            [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
            public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
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

    [Test]
    [Arguments("OwnFactory", "", "[IocRegister(RegisterAllInterfaces = true, Factory = nameof(FactoryContainer.Create))]")]
    [Arguments("DefaultFactory", "[assembly: IocRegisterDefaults(typeof(IRequestHandler<>), ServiceLifetime.Singleton, Factory = nameof(FactoryContainer.Create))]", "[IocRegister(RegisterAllInterfaces = true)]")]
    public async Task SGIOC039_ClosedServiceTypesUnmatchedByTemplate_ReportsEach(string scenario, string defaults, string attribute)
    {
        var source = CreateSource(defaults, $"{attribute}\npublic sealed class EntityHandler : IRequestHandler<Task<Entity>>, IRequestHandler<Entity>, IOther;");

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc039 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Select(d => d.GetMessage()).ToList();

        await Assert.That(sgioc039).Count().IsEqualTo(2);
        await Assert.That(sgioc039).Contains(m => m.Contains("'IRequestHandler<Entity>'") && m.Contains("'EntityHandler'") && m.Contains("'IRequestHandler<Task<int>>'") && m.Contains("'FactoryContainer.Create<T>'"));
        await Assert.That(sgioc039).Contains(m => m.Contains("'IOther'"));
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).All(d => d.Severity == DiagnosticSeverity.Warning)).IsTrue();
    }

    [Test]
    public async Task SGIOC039_OpenServiceTypes_ReportsOnlyThoseThatCannotMatch()
    {
        // IRequestHandler<T> matches the closings of T as Task<...>, while IRequestHandler<List<T>> never matches; the
        // generator does not register the non-generic IOther for an open implementation at all
        var source = CreateSource("", """
            [IocRegister(RegisterAllInterfaces = true, Factory = nameof(FactoryContainer.Create))]
            public sealed class Handler<T> : IRequestHandler<Task<T>>, IRequestHandler<T>, IRequestHandler<System.Collections.Generic.List<T>>, IOther;
            """);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc039 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Select(d => d.GetMessage()).ToList();

        await Assert.That(sgioc039).Count().IsEqualTo(1);
        await Assert.That(sgioc039[0]).Contains("'IRequestHandler<List<T>>'").And.Contains("'Handler<T>'");
    }

    [Test]
    [Arguments("AttributeMapping", "[assembly: IocRegisterFor(typeof(EntityHandler), ServiceTypes = [typeof(IRequestHandler<Entity>)], Factory = nameof(FactoryContainer.CreateUnmapped), GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(int)])]", "public sealed class EntityHandler : IRequestHandler<Entity>;")]
    [Arguments("OpenServiceTypeOnClosed", "", "[IocRegister(ServiceTypes = [typeof(IRequestHandler<>)], Factory = nameof(FactoryContainer.Create))] public sealed class EntityHandler : IRequestHandler<Entity>;")]
    public async Task SGIOC039_OtherRegistrationShapes_ReportsUnmatchedServiceType(string scenario, string assemblyAttributes, string registration)
    {
        var source = CreateSource(assemblyAttributes, registration).Replace(
            "public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();",
            "public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();\n    public static IRequestHandler<Task<T>> CreateUnmapped<T>() => throw new NotImplementedException();");

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc039 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Select(d => d.GetMessage()).ToList();

        await Assert.That(sgioc039).Count().IsEqualTo(1);
        await Assert.That(sgioc039[0]).Contains("'IRequestHandler<Entity>'");
    }

    [Test]
    [Arguments("PublicConstructor", "public Handler() { }", 0)]
    [Arguments("PrivateConstructor", "private Handler() { }", 1)]
    public async Task SGIOC039_OpenServiceTypeOfOpenImplementation_ReportsOnlyUnconstructibleImplementation(string scenario, string constructor, int expected)
    {
        // IRequestHandler<> is registered as the implementation's own form IRequestHandler<Task<T>>, which the template matches
        var source = CreateSource("", $$"""
            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IRequestHandler<>)], Factory = nameof(FactoryContainer.Create))]
            public sealed class Handler<T> : IRequestHandler<Task<T>> { {{constructor}} }
            """);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc039 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Select(d => d.GetMessage()).ToList();

        await Assert.That(sgioc039).Count().IsEqualTo(expected);
        if(expected > 0)
        {
            await Assert.That(sgioc039[0]).Contains("Service type 'Handler<T>' of 'Handler<T>'");
        }
    }

    [Test]
    [Arguments("TypeParameterMayMatch", "IPair<T, int>", 0)]
    [Arguments("NeverMatches", "IPair<List<T>, int>", 1)]
    [Arguments("ArrayMayMatch", "IPair<T[], int[]>", 0)]
    [Arguments("ArrayOfGenericMayMatch", "IPair<List<T>[], List<int>[]>", 0)]
    [Arguments("ArrayNeverMatchesInt", "IPair<T[], int>", 1)]
    public async Task SGIOC039_RepeatedPlaceholderOnOpenImplementation_ReportsOnlyWhenNoClosingMatches(string scenario, string serviceType, int expected)
    {
        // IPair<T, int> matches IPair<int, int> when T is int, while List<T> can never be int
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPair<T1, T2>;

            public static class PairFactory
            {
                [IocGenericFactory(typeof(IPair<int, int>), typeof(int))]
                public static IPair<T, T> Create<T>() => throw new NotImplementedException();
            }

            [IocRegister(RegisterAllInterfaces = true, Factory = nameof(PairFactory.Create))]
            public sealed class Pair<T> : {{serviceType}};
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Count()).IsEqualTo(expected);
    }

    [Test]
    public async Task SGIOC039_ImplementationWithoutAccessibleConstructor_ReportsImplementationType()
    {
        var source = CreateSource("", """
            [IocRegister(RegisterAllInterfaces = true, Factory = nameof(FactoryContainer.Create))]
            public sealed class EntityHandler : IRequestHandler<Task<Entity>> { private EntityHandler() { } }
            """);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc039 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Select(d => d.GetMessage()).ToList();

        await Assert.That(sgioc039).Count().IsEqualTo(1);
        await Assert.That(sgioc039[0]).Contains("Service type 'EntityHandler' of 'EntityHandler'");
    }

    [Test]
    [Arguments("Unmatched", "IRequestHandler<Entity>", 1)]
    [Arguments("Matched", "IRequestHandler<Task<Entity>>", 0)]
    public async Task SGIOC039_InterfaceTarget_ReportsWhenUnmatched(string scenario, string target, int expected)
    {
        // An interface target cannot be constructed, so it is not registered when the mapping does not match it
        var source = CreateSource(
            $"[assembly: IocRegisterFor<{target}>(ServiceLifetime.Singleton, Factory = nameof(FactoryContainer.Create))]",
            "");

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039)).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("Matched", "[IocRegister(Factory = nameof(FactoryContainer.Create))] public sealed class EntityHandler : IRequestHandler<Task<Entity>>;")]
    [Arguments("Instance", "[IocRegister(ServiceLifetime.Singleton, RegisterAllInterfaces = true, Instance = nameof(Default))] public sealed class EntityHandler : IRequestHandler<Entity> { public static readonly EntityHandler Default = new(); }")]
    [Arguments("NonGenericFactory", "[IocRegister(RegisterAllInterfaces = true, Factory = nameof(Factory))] public sealed class EntityHandler : IRequestHandler<Entity> { public static EntityHandler Factory() => new(); }")]
    public async Task SGIOC039_NoUnmatchedServiceType_NoDiagnostic(string scenario, string registration)
    {
        var source = CreateSource("", registration);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039)).IsEmpty();
    }
}
