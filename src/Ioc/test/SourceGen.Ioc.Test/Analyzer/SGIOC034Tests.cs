namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC034: more than one decorator constructor parameter can receive the decorated instance.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC034)]
public class SGIOC034Tests
{
    [Test]
    public async Task SGIOC034_TwoParametersOfServiceType_ReportsSecondParameter()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler;

            [IocRegister(ServiceTypes = [typeof(IHandler)], Key = "other")]
            public class OtherHandler : IHandler;

            [IocRegister(ServiceTypes = [typeof(IHandler)], Decorators = [typeof(PairDecorator)])]
            public class Handler : IHandler;

            [IocRegister(ServiceTypes = [typeof(IHandler)], Key = "second", Decorators = [typeof(PairDecorator)])]
            public class SecondHandler : IHandler;

            public class PairDecorator(IHandler inner, [FromKeyedServices("other")] IHandler other) : IHandler;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc034 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC034).ToList();

        // Reported once, although the decorator decorates two registrations
        await Assert.That(sgioc034).Count().IsEqualTo(1);
        await Assert.That(sgioc034[0].GetMessage()).Contains("'other'").And.Contains("'inner'").And.Contains("PairDecorator");
    }

    [Test]
    [Arguments("public class LoggingDecorator(ILogger logger, IHandler inner) : IHandler;")]
    [Arguments("public class LoggingDecorator(IHandler inner, IBase other) : IHandler;")]
    [Arguments("public class LoggingDecorator(IHandler? inner) : IHandler;")]
    public async Task SGIOC034_OneParameterOfServiceType_NoDiagnostic(string decorator)
    {
        var source = $$"""
            #nullable enable
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IBase;
            public interface IHandler : IBase;
            public interface ILogger;

            [IocRegister(ServiceTypes = [typeof(IHandler)], Decorators = [typeof(LoggingDecorator)])]
            public class Handler : IHandler;

            {{decorator}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC034)).IsEmpty();
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC035)).IsEmpty();
    }

    [Test]
    public async Task SGIOC034_OpenGenericDecoratorFromDefaults_ReportsSecondParameter()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IHandler<>), ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.PairDecorator<>)])]

            namespace TestNamespace;

            public interface IHandler<T>;

            [IocRegister]
            public class IntHandler : IHandler<int>;

            [IocRegister]
            public class StringHandler : IHandler<string>;

            public class PairDecorator<T>(IHandler<T> inner, IHandler<T> other) : IHandler<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc034 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC034).ToList();

        await Assert.That(sgioc034).Count().IsEqualTo(1);
        await Assert.That(sgioc034[0].GetMessage()).Contains("'other'");
    }
}
