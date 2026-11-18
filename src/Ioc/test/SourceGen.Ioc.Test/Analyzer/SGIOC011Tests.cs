namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC011: Duplicated Registration Detected - Same implementation type, key, and at least one matching tag are registered multiple times.
/// Services with tags are only registered when matching tags are passed, so services with different tags don't overlap.
/// Services without tags are always registered.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC011)]
public class SGIOC011Tests
{
    [Test]
    public async Task SGIOC011_DuplicatedRegistration_SameTypeWithoutKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService))]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_DuplicatedRegistration_SameTypeWithSameKey_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "key1")]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "key1")]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService").And.Contains("key1");
    }

    [Test]
    public async Task SGIOC011_DifferentKeys_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "key1")]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "key2")]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_DifferentTypes_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService1 : IMyService { }

            [IocRegister]
            public class MyService2 : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SingleRegistration_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_GenericAttribute_DuplicatedRegistration_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            [IocRegisterFor<MyService>]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_WithKeyAndWithoutKey_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "keyed")]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_MultipleIoCRegisterFor_SameType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService))]
            public interface IServiceMarker1 { }

            [IocRegisterFor(typeof(MyService))]
            public interface IServiceMarker2 { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_SameTypeWithSameTags_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Tags = ["tag1", "tag2"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag1", "tag2"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_SameTypeWithDifferentTags_NoDiagnostic()
    {
        // Services with different tags don't overlap (implicit tag-only behavior)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Tags = ["tag1"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag2"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SameTypeWithTagsAndWithoutTags_NoDiagnostic()
    {
        // Tagged service and untagged service don't overlap
        // (untagged is always registered, tagged only when matching tag is passed)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag1"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SameTypeSameKeySameTags_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "key1", Tags = ["tag1"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "key1", Tags = ["tag1"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService").And.Contains("key1");
    }

    [Test]
    public async Task SGIOC011_SameTypeSameKeyDifferentTags_NoDiagnostic()
    {
        // Services with same key but different tags don't overlap
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "key1", Tags = ["tag1"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "key1", Tags = ["tag2"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SameTypeDifferentKeySameTags_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "key1", Tags = ["tag1"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "key2", Tags = ["tag1"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_PartialTagOverlap_ReportsDiagnostic()
    {
        // When tags partially overlap (tag2 is common), should report duplicate
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Tags = ["tag1", "tag2"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag2", "tag3"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_NoTagOverlap_NoDiagnostic()
    {
        // When tags don't overlap, no duplicate (services only registered when matching tags are passed)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Tags = ["tag1", "tag2"])]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag3", "tag4"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011");

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_AssemblyLevelRegisterFor_SameType_ReportsDiagnosticOnSecondAttribute()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyService))]
            [assembly: IocRegisterFor(typeof(TestNamespace.MyService))]

            namespace TestNamespace;

            public class MyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(4);
    }

    [Test]
    [Category(Constants.Defaults)]
    [Arguments("AssemblyLevelSameTag", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Transient, Tags = [\"tag1\"])]", "", "tag1", 1)]
    [Arguments("TypeLevelSameTag", "", "[IocRegisterDefaults(typeof(IMyService), ServiceLifetime.Transient, Tags = [\"tag1\"])]", "tag1", 1)]
    [Arguments("DifferentTag", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Transient, Tags = [\"tag1\"])]", "", "tag2", 0)]
    public async Task SGIOC011_RegistrationWithoutTags_UsesTagsOfBestDefaultSettings(
        string scenario, string assemblyDefaults, string typeDefaults, string otherTag, int expected)
    {
        // Like the generator, a registration without Tags takes the Tags of its best default settings
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{assemblyDefaults}}

            namespace TestNamespace;

            {{typeDefaults}}
            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["{{otherTag}}"])]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC011)).Count().IsEqualTo(expected);
    }

    [Test]
    [Arguments("SameKey", "", "", 1)]
    [Arguments("DifferentKeys", ", Key = \"a\"", ", Key = \"b\"", 0)]
    public async Task SGIOC011_ImplementationTypesDifferingOnlyInTupleElementNames_ComparedAsRuntimeTypes(
        string scenario, string firstKey, string secondKey, int expected)
    {
        // Tuple element names are not part of the runtime type, so both attributes register the same implementation type
        var source = $$"""
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.Impl<(int A, int B)>){{firstKey}})]
            [assembly: IocRegisterFor(typeof(TestNamespace.Impl<(int X, int Y)>){{secondKey}})]

            namespace TestNamespace;

            public sealed class Impl<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC011).ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(expected);
        if(expected > 0)
        {
            await Assert.That(sgioc011[0].Location.GetLineSpan().StartLinePosition.Line).IsEqualTo(3);
        }
    }

    [Test]
    public async Task SGIOC011_RegisterForBeforeRegisterInSource_ReportsAtRegisterFor()
    {
        // The generator adds [IocRegister] registrations before [IocRegisterFor<T>] ones, so the holder's registration is the
        // later one that takes effect, although it comes first in source
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegisterFor<Foo>]
            public static class Registrations;

            [IocRegister]
            public class Foo;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].Location.SourceTree!.GetText().ToString(sgioc011[0].Location.SourceSpan))
            .IsEqualTo("IocRegisterFor<Foo>");
    }

    [Test]
    public async Task SGIOC011_GenericRegisterBeforeRegisterInSource_ReportsAtGenericRegister()
    {
        // [IocRegister<T>] registrations come after [IocRegister] ones in the generator's order
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            [IocRegister<IFoo>]
            [IocRegister]
            public class Foo : IFoo;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].Location.SourceTree!.GetText().ToString(sgioc011[0].Location.SourceSpan))
            .IsEqualTo("IocRegister<IFoo>");
    }

    [Test]
    public async Task SGIOC011_GenericRegisterForBeforeRegisterForInSource_ReportsAtGenericRegisterFor()
    {
        // [IocRegisterFor<T>] registrations come after [IocRegisterFor] ones in the generator's order
        const string source = """
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.Foo>]
            [assembly: IocRegisterFor(typeof(TestNamespace.Foo))]

            namespace TestNamespace;

            public class Foo;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].Location.SourceTree!.GetText().ToString(sgioc011[0].Location.SourceSpan))
            .IsEqualTo("IocRegisterFor<TestNamespace.Foo>");
    }

    [Test]
    public async Task SGIOC011_PartlyOverlappingTags_RecordsEveryTagOfDuplicate()
    {
        // B duplicates A on "x", and C duplicates B on "y": every tag of B is recorded, although B is reported for "x"
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Tags = ["x"])]
            public class Foo;

            [IocRegisterFor(typeof(Foo), Tags = ["x", "y"])]
            public static class RegistrationsB;

            [IocRegisterFor<Foo>(Tags = ["y"])]
            public static class RegistrationsC;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var locations = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011")
            .Select(static d => d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan))
            .ToList();

        await Assert.That(locations).Count().IsEqualTo(2);
        await Assert.That(locations).Contains("IocRegisterFor(typeof(Foo), Tags = [\"x\", \"y\"])");
        await Assert.That(locations).Contains("IocRegisterFor<Foo>(Tags = [\"y\"])");
    }
}
