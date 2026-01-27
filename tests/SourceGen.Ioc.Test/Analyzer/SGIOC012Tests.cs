namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC012: Duplicated IocRegisterDefaults Detected - Same target type and at least one matching tag has multiple default settings.
/// Services with tags are only registered when matching tags are passed, so services with different tags don't overlap.
/// Services without tags are always registered.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC012)]
public class SGIOC012Tests
{
    [Test]
    public async Task SGIOC012_DuplicatedDefaults_SameTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_DifferentTargetTypes_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService1), ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService2), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService1 { }
            public interface IMyService2 { }

            [IocRegister]
            public class MyService1 : IMyService1 { }

            [IocRegister]
            public class MyService2 : IMyService2 { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_SingleDefault_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_GenericAttribute_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MixedGenericAndNonGeneric_SameTargetType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MultipleDefaults_SameTargetType_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped)]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Transient)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        // Should report 2 diagnostics (second and third are duplicates)
        await Assert.That(sgioc012).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC012_NoDefaults_NoDiagnostic()
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
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_GenericTargetType_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IGenericService<T> { }

            [IocRegister]
            public class MyGenericService<T> : IGenericService<T> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IGenericService");
    }

    [Test]
    public async Task SGIOC012_TypeLevelAttribute_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Singleton)]
            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped)]
            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MixedAssemblyAndTypeLevelAttribute_DuplicatedDefaults_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped)]
            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_SameTargetTypeWithSameTags_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Tags = ["tag1", "tag2"])]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped, Tags = ["tag1", "tag2"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_SameTargetTypeWithDifferentTags_NoDiagnostic()
    {
        // Services with different tags don't overlap (implicit tag-only behavior)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Tags = ["tag1"])]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped, Tags = ["tag2"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_SameTargetTypeWithTagsAndWithoutTags_NoDiagnostic()
    {
        // Tagged and untagged defaults don't overlap
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton)]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped, Tags = ["tag1"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_GenericAttribute_SameTargetTypeWithSameTags_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Singleton, Tags = ["tag1"])]
            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(ServiceLifetime.Scoped, Tags = ["tag1"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_TypeLevelAttribute_SameTargetTypeWithSameTags_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Singleton, Tags = ["tag1"])]
            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped, Tags = ["tag1"])]
            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_TypeLevelAttribute_SameTargetTypeWithDifferentTags_NoDiagnostic()
    {
        // Services with different tags don't overlap (implicit tag-only behavior)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Singleton, Tags = ["tag1"])]
            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped, Tags = ["tag2"])]
            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_MixedAssemblyAndTypeLevelAttribute_SameTagsDifferentLevels_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Tags = ["tag1"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped, Tags = ["tag1"])]
            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_MixedAssemblyAndTypeLevelAttribute_DifferentTagsDifferentLevels_NoDiagnostic()
    {
        // Services with different tags don't overlap (implicit tag-only behavior)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Tags = ["tag1"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegisterDefaults<IMyService>(ServiceLifetime.Scoped, Tags = ["tag2"])]
            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC012_PartialTagOverlap_ReportsDiagnostic()
    {
        // When tags partially overlap (tag2 is common), should report duplicate
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Tags = ["tag1", "tag2"])]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped, Tags = ["tag2", "tag3"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012").ToList();

        await Assert.That(sgioc012).Count().IsEqualTo(1);
        await Assert.That(sgioc012[0].GetMessage()).Contains("IMyService");
    }

    [Test]
    public async Task SGIOC012_NoTagOverlap_NoDiagnostic()
    {
        // When tags don't overlap, no duplicate (services only registered when matching tags are passed)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Tags = ["tag1", "tag2"])]
            [assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Scoped, Tags = ["tag3", "tag4"])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc012 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC012");

        await Assert.That(sgioc012).Count().IsEqualTo(0);
    }
}
