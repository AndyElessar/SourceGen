using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC036 (no deferred wrapper on the cycle) and SGIOC037 (a Lazy or Func wrapper on the cycle): a dependency of
/// an open generic registration nests type arguments deeper on every round of a cycle, so the generator does not close it.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC036)]
[Category(Constants.SGIOC037)]
public class SGIOC036Tests
{
    private const string Usings = """
        using System;
        using System.Collections.Generic;
        using SourceGen.Ioc;

        namespace TestNamespace;

        """;

    [Test]
    [Arguments("INode<List<T>> next")]
    [Arguments("IEnumerable<INode<List<T>>> children")]
    public async Task SGIOC036_ExpandingDependencyWithoutDeferredWrapper_ReportsError(string parameter)
    {
        var source = Usings + $$"""
            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T>({{parameter}}) : INode<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(sgioc036[0].GetMessage()).Contains("Node<T>").And.Contains("INode<List<T>>");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    [Arguments("Lazy<INode<List<T>>> next")]
    [Arguments("Func<INode<List<T>>> next")]
    public async Task SGIOC037_ExpandingDependencyBehindDeferredWrapper_ReportsWarning(string parameter)
    {
        var source = Usings + $$"""
            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T>({{parameter}}) : INode<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc037 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC037).ToList();

        await Assert.That(sgioc037).Count().IsEqualTo(1);
        await Assert.That(sgioc037[0].Severity).IsEqualTo(DiagnosticSeverity.Warning);
        await Assert.That(sgioc037[0].GetMessage()).Contains("Node<T>").And.Contains("INode<List<T>>");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036)).IsEmpty();
    }

    [Test]
    [Arguments("IEnumerable<IBridge<T>> bridges", Constants.SGIOC036)]
    [Arguments("Lazy<IBridge<T>> bridge", Constants.SGIOC037)]
    public async Task SGIOC036_ExpandingCycleThroughAnotherRegistration_ReportsExpandingDependencyOnly(string nodeParameter, string expectedId)
    {
        // Only Bridge<T>'s INode<List<T>> nests T; a deferred wrapper anywhere on the cycle makes the object graph finite
        var source = Usings + $$"""
            public interface INode<T>;
            public interface IBridge<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T>({{nodeParameter}}) : INode<T>;

            [IocRegister(ServiceTypes = [typeof(IBridge<>)])]
            public sealed class Bridge<T>(INode<List<T>> node) : IBridge<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var reported = diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037).ToList();

        await Assert.That(reported).Count().IsEqualTo(1);
        await Assert.That(reported[0].Id).IsEqualTo(expectedId);
        await Assert.That(reported[0].GetMessage()).Contains("Bridge<T>");
    }

    [Test]
    public async Task SGIOC036_ExpandingDecoratorDependency_ReportsAtDecoratorParameter()
    {
        var source = Usings + """
            public interface IService<T>;

            [IocRegister(ServiceTypes = [typeof(IService<>)], Decorators = [typeof(Decorator<>)])]
            public sealed class Service<T> : IService<T>;

            public sealed class Decorator<T>(IService<T> inner, IService<List<T>> other) : IService<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].GetMessage()).Contains("Decorator<T>").And.Contains("IService<List<T>>");
    }

    [Test]
    [Arguments("public sealed class Pair<T, U>(IPair<U, U> next) : IPair<T, U>;")]
    [Arguments("public sealed class Pair<T, U>(Lazy<IPair<U, T>> next) : IPair<T, U>;")]
    [Arguments("public sealed class Pair<T, U>(IOther<List<T>> other) : IPair<T, U>;")]
    public async Task SGIOC036_FiniteClosings_NoDiagnostic(string pair)
    {
        // Moving or swapping type arguments ends, and List<T> is not on a cycle
        var source = Usings + $$"""
            public interface IPair<T, U>;
            public interface IOther<T>;

            [IocRegister(ServiceTypes = [typeof(IPair<,>)])]
            {{pair}}

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            public sealed class Other<T> : IOther<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    [Arguments("Lazy<IOther<T>> other")]
    [Arguments("Lazy<INode<T>> self")]
    public async Task SGIOC036_DeferredCycleBesideEagerExpandingCycle_ReportsError(string deferredParameter)
    {
        // The eager cycle through INode<List<T>> is infinite on its own, whatever another cycle through Lazy<T> does
        var source = Usings + $$"""
            public interface INode<T>;
            public interface IOther<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T>(INode<List<T>> next, {{deferredParameter}}) : INode<T>;

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            public sealed class Other<T>(INode<T> node) : IOther<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].GetMessage()).Contains("'INode<List<T>>'");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    public async Task SGIOC036_OverloadedInjectMethod_ReportsAtInjectedParameter()
    {
        var source = Usings + """
            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T> : INode<T>
            {
                public void Init(string text) { }

                [IocInject]
                public void Init(INode<List<T>> next) { }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].GetMessage()).Contains("'INode<List<T>>'");
    }

    [Test]
    public async Task SGIOC036_DisabledInjectedField_NoDiagnostic()
    {
        // Field injection is disabled by default, so the field is not injected (SGIOC022)
        var source = Usings + """
            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T> : INode<T>
            {
                [IocInject]
                public INode<List<T>>? Next;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    public async Task SGIOC036_ImplementationOfNestedOpenGenericInterface_ReportsDiagnostic()
    {
        // B<U, V> is only indexed through IB<U, List<V>>, like the generator indexes the interfaces of an implementation
        var source = Usings + """
            public interface IB<T1, T2>;
            public interface IC<T1, T2>;
            public sealed class Box<T>;

            [IocRegister]
            public sealed class B<U, V>(IC<U, List<int>> c) : IB<U, List<V>>;

            [IocRegister(ServiceTypes = [typeof(IC<,>)])]
            public sealed class C<T, S>(IB<Box<T>, S> b) : IC<T, S>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].GetMessage()).Contains("C<T, S>");
    }

    [Test]
    public async Task SGIOC036_ExpandingInjectedProperty_ReportsAtProperty()
    {
        var source = Usings + """
            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T> : INode<T>
            {
                [IocInject]
                public INode<List<T>>? Next { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].Location.SourceSpan.Length).IsGreaterThan(0);
    }
}
