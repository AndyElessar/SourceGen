using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC044: Factory names a method group with more than one usable overload.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC044)]
public class SGIOC044Tests
{
    private static string CreateSource(string registration, string factories) => $$"""
        using System;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;
        using TestNamespace;

        {{registration}}

        namespace TestNamespace;

        public interface IFoo;
        public interface IBar;
        public interface IHandler<T>;
        public sealed class Foo : IFoo;

        {{factories}}
        """;

    private const string RegisterFor = "[assembly: IocRegisterFor<Foo>(ServiceTypes = [typeof(IFoo)], Factory = nameof(Factories.Create))]";

    [Test]
    [Arguments("OverloadsOnSameType", RegisterFor, """
        public static class Factories
        {
            public static IFoo Create(IServiceProvider sp) => new Foo();
            public static IFoo Create(IBar bar, [ServiceKey] string key) => new Foo();
        }
        """)]
    [Arguments("OverloadsAcrossBaseAndDerived", RegisterFor, """
        public class FactoriesBase { public static IFoo Create(IServiceProvider sp) => new Foo(); }
        public class Factories : FactoriesBase { public static IFoo Create(IBar bar) => new Foo(); }
        """)]
    [Arguments("MappedGenericAndNonGeneric", RegisterFor, """
        public static class Factories
        {
            [IocGenericFactory(typeof(IHandler<int>), typeof(int))]
            public static IFoo Create<T>() => new Foo();
            public static IFoo Create(IServiceProvider sp) => new Foo();
        }
        """)]
    [Arguments("TypeLevelRegister", "", """
        [IocRegister(ServiceTypes = [typeof(IFoo)], Factory = nameof(Factories.Create))]
        public sealed class Registered : IFoo;
        public static class Factories
        {
            public static IFoo Create(IServiceProvider sp) => new Foo();
            public static IFoo Create(IBar bar) => new Foo();
        }
        """)]
    [Arguments("RegisterDefaults", "[assembly: IocRegisterDefaults(typeof(IFoo), ServiceLifetime.Singleton, Factory = nameof(Factories.Create))]", """
        public static class Factories
        {
            public static IFoo Create(IServiceProvider sp) => new Foo();
            public static IFoo Create(IBar bar) => new Foo();
        }
        """)]
    public async Task SGIOC044_MethodGroupWithSeveralUsableOverloads_ReportsDiagnostic(string scenario, string registration, string factories)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(registration, factories));
        var sgioc044 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC044).ToList();

        await Assert.That(sgioc044).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc044[0].Severity).IsEqualTo(DiagnosticSeverity.Error).Because(scenario);
        await Assert.That(sgioc044[0].GetMessage()).Contains("'Create'").Because(scenario);
        await Assert.That(sgioc044[0].Location.SourceTree!.GetText().ToString(sgioc044[0].Location.SourceSpan)).StartsWith("nameof(").Because(scenario);
        // No overload is chosen, so the diagnostics of a single factory method are not reported
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC008)).IsEmpty().Because(scenario);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC014)).IsEmpty().Because(scenario);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC016)).IsEmpty().Because(scenario);
    }

    [Test]
    [Arguments("InstanceOverload", """
        public class Factories
        {
            public IFoo Create(IServiceProvider sp) => new Foo();
            public static IFoo Create(IBar bar) => new Foo();
        }
        """)]
    [Arguments("PrivateOverload", """
        public static class Factories
        {
            private static IFoo Create(IServiceProvider sp) => new Foo();
            public static IFoo Create(IBar bar) => new Foo();
        }
        """)]
    [Arguments("VoidOverload", """
        public static class Factories
        {
            public static void Create(IServiceProvider sp) { }
            public static IFoo Create(IBar bar) => new Foo();
        }
        """)]
    [Arguments("UnmappedGenericOverload", """
        public static class Factories
        {
            public static IFoo Create<T>(T value) => new Foo();
            public static IFoo Create(IBar bar) => new Foo();
        }
        """)]
    public async Task SGIOC044_MethodGroupWithOneUsableOverload_NoDiagnostic(string scenario, string factories)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(RegisterFor, factories));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC044)).IsEmpty().Because(scenario);
        // The single usable overload is checked: the others are not reported
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC008)).IsEmpty().Because(scenario);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC016)).IsEmpty().Because(scenario);
    }

    [Test]
    public async Task SGIOC044_GenericOverloadUsableWithAttributeMapping_ReportsDiagnostic()
    {
        // GenericFactoryTypeMapping on the registration makes the generic overload usable too
        const string registration = "[assembly: IocRegisterFor<Foo>(ServiceTypes = [typeof(IFoo)], Factory = nameof(Factories.Create), GenericFactoryTypeMapping = [typeof(IHandler<int>), typeof(int)])]";
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(registration, """
            public static class Factories
            {
                public static IFoo Create<T>() => new Foo();
                public static IFoo Create(IBar bar) => new Foo();
            }
            """));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC044)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC044_ServiceKeyOnTheSingleUsableOverload_ReportsSGIOC014()
    {
        // The [ServiceKey] parameter is on the overload the generator calls, which is not the first candidate
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(RegisterFor, """
            public class Factories
            {
                public IFoo Create(IServiceProvider sp) => new Foo();
                public static IFoo Create(IBar bar, [ServiceKey] string key) => new Foo();
            }
            """));
        var sgioc014 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC014).ToList();

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC044)).IsEmpty();
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC008)).IsEmpty();
        await Assert.That(sgioc014).Count().IsEqualTo(1);
        await Assert.That(sgioc014[0].GetMessage()).Contains("key");
    }

    [Test]
    public async Task SGIOC044_MethodGroupWithoutUsableOverload_ReportsSGIOC008()
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(RegisterFor, """
            public class Factories
            {
                public IFoo Create(IServiceProvider sp) => new Foo();
                public IFoo Create(IBar bar) => new Foo();
            }
            """));
        var sgioc008 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC008).ToList();

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC044)).IsEmpty();
        await Assert.That(sgioc008).Count().IsEqualTo(1);
        await Assert.That(sgioc008[0].GetMessage()).Contains("'Create'").And.Contains("none of whose overloads can be the factory");
    }
}
