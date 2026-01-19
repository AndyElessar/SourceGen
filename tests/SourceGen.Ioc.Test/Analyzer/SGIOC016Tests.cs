namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC016: Factory Method is unmatched - Generic factory method does not have [IocGenericFactory] attribute.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC016)]
public class SGIOC016Tests
{
    [Test]
    public async Task SGIOC016_GenericFactory_WithoutIocGenericFactoryAttribute_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Generic factory method without [IocGenericFactory] attribute - should report SGIOC016
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create").And.Contains("[IocGenericFactory]");
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithIocGenericFactoryAttribute_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC016_NonGenericFactory_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                // Non-generic factory - no [IocGenericFactory] required
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_OnIocRegister_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IRequestHandler<>)],
                Factory = nameof(FactoryContainer.Create))]
            public class Handler<T> : IRequestHandler<Task<T>> { }

            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory] - should report SGIOC016
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_OnIocRegisterFor_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocRegisterFor(
                typeof(Handler<>),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IRequestHandler<>)],
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory] - should report SGIOC016
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_TwoTypeParameters_WithoutAttribute_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Two type parameter generic factory without [IocGenericFactory] - should report SGIOC016
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create");
    }
}
