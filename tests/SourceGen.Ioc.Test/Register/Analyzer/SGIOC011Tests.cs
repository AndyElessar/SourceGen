namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC011: Duplicated Registration Detected - Same implementation type, key, and at least one matching tag are registered multiple times.
/// When TagOnly=false, an empty tag is added for comparison.
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

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
    public async Task SGIOC011_SameTypeWithDifferentTags_TagOnlyTrue_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Tags = ["tag1"], TagOnly = true)]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag2"], TagOnly = true)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SameTypeWithDifferentTags_TagOnlyFalse_ReportsDiagnostic()
    {
        // When TagOnly=false (default), both registrations have an implicit empty tag
        // so they will be considered duplicates due to the empty tag overlap
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_SameTypeWithTagsAndWithoutTags_TagOnlyTrue_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag1"], TagOnly = true)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC011_SameTypeWithTagsAndWithoutTags_TagOnlyFalse_ReportsDiagnostic()
    {
        // When TagOnly=false (default), both have empty tag, causing overlap
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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
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
    public async Task SGIOC011_SameTypeSameKeyDifferentTags_TagOnlyTrue_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "key1", Tags = ["tag1"], TagOnly = true)]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Key = "key1", Tags = ["tag2"], TagOnly = true)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

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
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

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

            [IocRegister(Tags = ["tag1", "tag2"], TagOnly = true)]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag2", "tag3"], TagOnly = true)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(1);
        await Assert.That(sgioc011[0].GetMessage()).Contains("MyService");
    }

    [Test]
    public async Task SGIOC011_NoTagOverlap_TagOnlyTrue_NoDiagnostic()
    {
        // When tags don't overlap and TagOnly=true, no duplicate
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Tags = ["tag1", "tag2"], TagOnly = true)]
            public class MyService : IMyService { }

            [IocRegisterFor(typeof(MyService), Tags = ["tag3", "tag4"], TagOnly = true)]
            public interface IServiceMarker { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc011 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC011").ToList();

        await Assert.That(sgioc011).Count().IsEqualTo(0);
    }
}
