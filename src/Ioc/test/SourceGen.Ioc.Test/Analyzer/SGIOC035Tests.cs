namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC035: no decorator constructor parameter can receive the decorated instance.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC035)]
public class SGIOC035Tests
{
    [Test]
    [Arguments("public class BadDecorator(ILogger logger) : IHandler;")]
    [Arguments("public class BadDecorator : IHandler;")]
    [Arguments("public class BadDecorator(IBase inner) : IHandler;")]
    public async Task SGIOC035_NoParameterOfServiceType_ReportsDiagnostic(string decorator)
    {
        var source = $$"""
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IBase;
            public interface IHandler : IBase;
            public interface ILogger;

            [IocRegister(ServiceTypes = [typeof(IHandler)], Decorators = [typeof(BadDecorator)])]
            public class Handler : IHandler;

            [IocRegister(ServiceTypes = [typeof(IHandler)], Key = "second", Decorators = [typeof(BadDecorator)])]
            public class SecondHandler : IHandler;

            {{decorator}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc035 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC035).ToList();

        // Reported once, although the decorator decorates two registrations
        await Assert.That(sgioc035).Count().IsEqualTo(1);
        await Assert.That(sgioc035[0].GetMessage()).Contains("BadDecorator").And.Contains("IHandler");
    }

    [Test]
    public async Task SGIOC035_DecoratorOfOneServiceTypeOnly_ReportsOtherServiceType()
    {
        // The decorator implements both service types but can only receive an IFoo
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;
            public interface IBar;

            [IocRegister(ServiceTypes = [typeof(IFoo), typeof(IBar)], Decorators = [typeof(FooDecorator)])]
            public class Service : IFoo, IBar;

            public class FooDecorator(IFoo inner) : IFoo, IBar;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc035 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC035).ToList();

        await Assert.That(sgioc035).Count().IsEqualTo(1);
        await Assert.That(sgioc035[0].GetMessage()).Contains("'IBar'");
    }

    [Test]
    public async Task SGIOC035_DecoratorNotImplementingServiceType_NoDiagnostic()
    {
        // A decorator is only applied to the service types it implements; others are skipped without a diagnostic
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;
            public interface IBar;

            [IocRegister(ServiceTypes = [typeof(IFoo), typeof(IBar)], Decorators = [typeof(FooDecorator)])]
            public class Service : IFoo, IBar;

            public class FooDecorator(IFoo inner) : IFoo;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC035)).IsEmpty();
    }
}
