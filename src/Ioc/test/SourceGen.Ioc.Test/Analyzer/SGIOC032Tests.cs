namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC032: Service type is not compatible with implementation.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC032)]
public class SGIOC032Tests
{
    [Test]
    public async Task SGIOC032_OpenImplementation_WithClosedServiceType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IocRegister(ServiceTypes = [typeof(IRepository<int>)])]
            public class Repository<T> : IRepository<T> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc032 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC032).ToList();

        await Assert.That(sgioc032).Count().IsEqualTo(1);
        await Assert.That(sgioc032[0].GetMessage()).Contains("IRepository<int>").And.Contains("open generic implementation");
    }

    [Test]
    public async Task SGIOC032_ClosedImplementation_WithOpenServiceType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IocRegister(ServiceTypes = [typeof(IRepository<>)])]
            public class IntRepository : IRepository<int> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc032 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC032).ToList();

        await Assert.That(sgioc032).Count().IsEqualTo(1);
        await Assert.That(sgioc032[0].GetMessage()).Contains("closed implementation");
    }

    [Test]
    public async Task SGIOC032_ReorderedTypeParameters_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse> { }

            [IocRegister(ServiceTypes = [typeof(IHandler<,>)])]
            public class Handler<TResponse, TRequest> : IHandler<TRequest, TResponse> { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc032 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC032).ToList();

        await Assert.That(sgioc032).Count().IsEqualTo(1);
        await Assert.That(sgioc032[0].GetMessage()).Contains("same order");
    }

    [Test]
    public async Task SGIOC032_RegisterFor_OpenImplementation_WithClosedServiceType_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            public class Repository<T> : IRepository<T> { }

            [IocRegisterFor(typeof(Repository<>), ServiceTypes = [typeof(IRepository<string>)])]
            public static class Module { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc032 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC032);

        await Assert.That(sgioc032).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC032_CompatibleServiceTypes_NoDiagnostic()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }
            public interface IService { }

            [IocRegister(ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            [IocRegister(ServiceTypes = [typeof(IRepository<int>), typeof(IService)])]
            public class IntRepository : IRepository<int>, IService { }

            [IocRegister<IService>]
            public class GenericAttributeService : IService { }

            [IocRegisterFor(typeof(Repository<>), ServiceTypes = [typeof(IRepository<>), typeof(Repository<>)])]
            public static class Module { }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc032 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC032);

        await Assert.That(sgioc032).Count().IsEqualTo(0);
    }
}
