namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC017: Generic Factory Method's type parameters are duplicated - Placeholder types in [IocGenericFactory] must be unique.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC017)]
public class SGIOC017Tests
{
    [Test]
    public async Task SGIOC017_DuplicatedPlaceholderTypes_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            public static class FactoryContainer
            {
                // Both placeholders use int - duplicated, should report SGIOC017
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<int>>), typeof(int), typeof(int))]
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc017 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC017").ToList();

        await Assert.That(sgioc017).Count().IsEqualTo(1);
        await Assert.That(sgioc017[0].GetMessage()).Contains("int").And.Contains("duplicated");
    }

    [Test]
    public async Task SGIOC017_UniquePlaceholderTypes_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            public static class FactoryContainer
            {
                // Different placeholder types (int and decimal) - unique, no diagnostic
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>>), typeof(int), typeof(decimal))]
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc017 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC017");

        await Assert.That(sgioc017).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC017_SinglePlaceholder_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public static class FactoryContainer
            {
                // Single placeholder - cannot have duplicates
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc017 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC017");

        await Assert.That(sgioc017).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC017_ThreePlaceholders_TwoDuplicated_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<T1, T2, T3> { }

            public static class FactoryContainer
            {
                // Three placeholders, but int appears twice - should report SGIOC017
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>, int>), typeof(int), typeof(decimal), typeof(int))]
                public static IRequestHandler<Task<TA>, List<TB>, TC> Create<TA, TB, TC>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc017 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC017").ToList();

        await Assert.That(sgioc017).Count().IsEqualTo(1);
        await Assert.That(sgioc017[0].GetMessage()).Contains("int");
    }

    [Test]
    public async Task SGIOC017_ThreeUniquePlaceholders_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<T1, T2, T3> { }

            public static class FactoryContainer
            {
                // Three unique placeholders - no duplicate
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>, string>), typeof(int), typeof(decimal), typeof(string))]
                public static IRequestHandler<Task<TA>, List<TB>, TC> Create<TA, TB, TC>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc017 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC017");

        await Assert.That(sgioc017).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC017_DuplicatedGenericTypes_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            public static class FactoryContainer
            {
                // Both placeholders use List<int> - duplicated, should report SGIOC017
                [IocGenericFactory(typeof(IRequestHandler<List<int>, List<int>>), typeof(List<int>), typeof(List<int>))]
                public static IRequestHandler<T1, T2> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc017 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC017").ToList();

        await Assert.That(sgioc017).Count().IsEqualTo(1);
        await Assert.That(sgioc017[0].GetMessage()).Contains("List<int>");
    }
}
