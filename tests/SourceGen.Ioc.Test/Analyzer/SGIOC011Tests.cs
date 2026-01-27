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
}
