using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC043: InjectMembers names a method group with more than one injectable overload.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC043)]
public class SGIOC043Tests
{
    private static string CreateSource(string injectMember, string declarations) => $$"""
        using SourceGen.Ioc;
        using TestNamespace;

        [assembly: IocRegisterFor(typeof(Derived), InjectMembers = [{{injectMember}}])]

        namespace TestNamespace;

        public interface IFoo;
        public interface IBar;

        {{declarations}}
        """;

    [Test]
    [Arguments("OverloadsAcrossBaseAndDerived", "nameof(Derived.Init)", """
        public class Base { public void Init(IFoo foo) { } }
        public sealed class Derived : Base { public void Init(IBar bar) { } }
        """)]
    [Arguments("OverloadsOnSameType", "nameof(Derived.Init)", """
        public sealed class Derived { public void Init(IFoo foo) { } public void Init(IBar bar) { } }
        """)]
    [Arguments("KeyedElement", "new object[] { nameof(Derived.Init), \"key\" }", """
        public sealed class Derived { public void Init(IFoo foo) { } public void Init(IBar bar) { } }
        """)]
    [Arguments("InterfaceMethodGroup", "nameof(IInit.Init)", """
        public interface IInit { void Init(IFoo foo); void Init(IBar bar); }
        public sealed class Derived : IInit { public void Init(IFoo foo) { } public void Init(IBar bar) { } }
        """)]
    // [IocInject] is only read from the registered type's own members, so it does not resolve a base class overload
    [Arguments("IocInjectOnBaseOverload", "nameof(Derived.Init)", """
        public class Base { [IocInject] public void Init(IFoo foo) { } }
        public sealed class Derived : Base { public void Init(IBar bar) { } }
        """)]
    public async Task SGIOC043_MethodGroupWithSeveralInjectableOverloads_ReportsDiagnostic(string scenario, string injectMember, string declarations)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource(injectMember, declarations));
        var sgioc043 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC043).ToList();

        await Assert.That(sgioc043).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc043[0].Severity).IsEqualTo(DiagnosticSeverity.Error).Because(scenario);
        await Assert.That(sgioc043[0].GetMessage()).Contains("'Init'").And.Contains("'Derived'").And.Contains("[IocInject]").Because(scenario);
        await Assert.That(sgioc043[0].Location.SourceTree!.GetText().ToString(sgioc043[0].Location.SourceSpan)).StartsWith("nameof(").Because(scenario);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024)).IsEmpty().Because(scenario);
    }

    [Test]
    [Arguments("StaticOverload", """
        public sealed class Derived { public void Init(IFoo foo) { } public static void Init(IBar bar) { } }
        """)]
    [Arguments("GenericOverload", """
        public sealed class Derived { public void Init(IFoo foo) { } public void Init<T>(T value) { } }
        """)]
    [Arguments("NonVoidOverload", """
        public sealed class Derived { public void Init(IFoo foo) { } public int Init(IBar bar) => 0; }
        """)]
    [Arguments("NonInjectableOverloadOnBase", """
        public class Base { public static void Init(IBar bar) { } }
        public sealed class Derived : Base { public void Init(IFoo foo) { } }
        """)]
    [Arguments("OverloadMarkedWithIocInject", """
        public sealed class Derived { [IocInject] public void Init(IFoo foo) { } public void Init(IBar bar) { } }
        """)]
    public async Task SGIOC043_MethodGroupWithOneInjectableOverloadOrIocInject_NoDiagnostic(string scenario, string declarations)
    {
        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(CreateSource("nameof(Derived.Init)", declarations));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC043)).IsEmpty().Because(scenario);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024)).IsEmpty().Because(scenario);
    }

    [Test]
    [Arguments("Single", "public Task Init(IFoo foo) => Task.CompletedTask;")]
    [Arguments("Overloaded", "public Task Init(IFoo foo) => Task.CompletedTask; public static void Init(IBar bar) { }")]
    public async Task SGIOC043_SingleTaskOverloadWithoutAsyncMethodInject_ReportsSGIOC024(string scenario, string methods)
    {
        // Like a single Task method, the only injectable overload is not injected without AsyncMethodInject
        var source = CreateSource("nameof(Derived.Init)", $$"""
            public sealed class Derived { {{methods}} }
            """).Replace("using SourceGen.Ioc;", "using System.Threading.Tasks;\nusing SourceGen.Ioc;");

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC043)).IsEmpty().Because(scenario);
        await Assert.That(sgioc024).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc024[0].GetMessage()).Contains("'Init'").And.Contains("method does not return void").Because(scenario);
    }

    [Test]
    public async Task SGIOC043_MethodGroupWithoutInjectableOverload_ReportsSGIOC024()
    {
        var source = CreateSource("nameof(Derived.Init)", """
            public sealed class Derived { public static void Init(IFoo foo) { } public static void Init(IBar bar) { } }
            """);

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc024 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC024).ToList();

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC043)).IsEmpty();
        await Assert.That(sgioc024).Count().IsEqualTo(1);
        await Assert.That(sgioc024[0].GetMessage()).Contains("'Init'").And.Contains("none of its overloads can be injected");
    }
}
