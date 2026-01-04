namespace SourceGen.Ioc.Test.Register.Analyzer;

/// <summary>
/// Tests for SGIOC006: Nested open generic registration warning.
/// The source generator can auto-generate closed generic registrations when used in constructor parameters or GetService calls.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC006)]
public class SGIOC006Tests
{
    [Test]
    public async Task SGIOC006_NestedOpenGenericInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
        await Assert.That(sgioc006[0].GetMessage()).Contains("NestedGenericHandler").And.Contains("IHandler<Wrapper<T>>");
    }

    [Test]
    public async Task SGIOC006_DeeplyNestedOpenGenericInterface_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IProcessor<T> { }
            public class Outer<T> { }
            public class Inner<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class DeeplyNestedHandler<T> : IProcessor<Outer<Inner<T>>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC006_NestedOpenGenericBaseClass_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class BaseHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllBaseClasses = true)]
            public class NestedGenericHandler<T> : BaseHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC006_SimpleOpenGeneric_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IoCRegister]
            public class SimpleHandler<T> : IHandler<T> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_ClosedGeneric_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister]
            public class ClosedGenericHandler : IHandler<Wrapper<string>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_NonGenericClass_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister]
            public class SimpleService : IService { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_MultipleNestedOpenGenerics_ReportsMultipleDiagnostics()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public interface IProcessor<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class MultiNestedHandler<T> : IHandler<Wrapper<T>>, IProcessor<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(2);
    }

    [Test]
    public async Task SGIOC006_NestedOpenGenericWithSelfRegistrationOnly_NoDiagnostic()
    {
        // When an open generic only registers itself (no ServiceTypes, no RegisterAllInterfaces, no RegisterAllBaseClasses),
        // it should not report SGIOC006 because the nested open generic interface won't be registered.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_NestedOpenGenericWithServiceTypes_ReportsDiagnostic()
    {
        // When ServiceTypes is specified, it will try to register the interface, so SGIOC006 should be reported.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(ServiceTypes = [typeof(IHandler<>)])]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC006_NestedOpenGenericWithRegisterAllInterfaces_ReportsDiagnostic()
    {
        // When RegisterAllInterfaces is true, SGIOC006 should be reported for nested open generics.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllInterfaces = true)]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC006_NestedOpenGenericWithRegisterAllBaseClasses_ReportsDiagnostic()
    {
        // When RegisterAllBaseClasses is true, SGIOC006 should be reported for nested open generics in base classes.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class BaseHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister(RegisterAllBaseClasses = true)]
            public class NestedGenericHandler<T> : BaseHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC006_NestedOpenGenericBaseClassWithSelfRegistrationOnly_NoDiagnostic()
    {
        // When only registering self (no RegisterAllBaseClasses), nested open generic base class should not cause SGIOC006.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class BaseHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegister]
            public class NestedGenericHandler<T> : BaseHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC006_IoCRegisterForAttribute_NestedOpenGeneric_ReportsDiagnostic()
    {
        // IoCRegisterForAttribute always registers for service types, so SGIOC006 should be reported.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }
            public class Wrapper<T> { }

            [IoCRegisterFor(typeof(NestedGenericHandler<>), RegisterAllInterfaces = true)]
            public class NestedGenericHandler<T> : IHandler<Wrapper<T>> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc006 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC006").ToList();

        await Assert.That(sgioc006).Count().IsEqualTo(1);
    }
}
