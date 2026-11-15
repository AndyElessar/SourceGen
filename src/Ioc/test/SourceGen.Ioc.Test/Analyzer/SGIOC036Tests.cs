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
    [Arguments("INode<List<T>> next", "INode<List<T>>")]
    [Arguments("IEnumerable<INode<List<T>>> children", "INode<List<T>>")]
    [Arguments("INode<T[]> next", "INode<T[]>")]
    public async Task SGIOC036_ExpandingDependencyWithoutDeferredWrapper_ReportsError(string parameter, string dependency)
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
        await Assert.That(sgioc036[0].GetMessage()).Contains("Node<T>").And.Contains(dependency);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    [Arguments("Lazy<INode<List<T>>> next", "INode<List<T>>")]
    [Arguments("Func<INode<List<T>>> next", "INode<List<T>>")]
    [Arguments("Lazy<INode<T[]>> next", "INode<T[]>")]
    public async Task SGIOC037_ExpandingDependencyBehindDeferredWrapper_ReportsWarning(string parameter, string dependency)
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
        await Assert.That(sgioc037[0].GetMessage()).Contains("Node<T>").And.Contains(dependency);
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

    [Test]
    [Arguments("public INode<List<T>>? Next { get; set; }", "nameof(Node<int>.Next)", Constants.SGIOC036)]
    [Arguments("public Lazy<INode<List<T>>>? Next { get; set; }", "nameof(Node<int>.Next)", Constants.SGIOC037)]
    [Arguments("public void Initialize(INode<List<T>> next) { }", "nameof(Node<int>.Initialize)", Constants.SGIOC036)]
    [Arguments("public INode<List<T>>? Next { get; set; }", "new object[] { nameof(Node<int>.Next), \"A\" }", Constants.SGIOC036)]
    public async Task SGIOC036_ExpandingMemberListedOnlyInInjectMembers_ReportsAtElement(string member, string element, string expectedId)
    {
        // nameof(Node<int>.Next) names Node<T>.Next of the open generic registration, whose type nests T
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], InjectMembers = [{{element}}])]

            namespace TestNamespace;

            public interface INode<T>;

            public sealed class Node<T> : INode<T>
            {
                {{member}}
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var expected = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, expectedId).ToList();
        var other = expectedId == Constants.SGIOC036 ? Constants.SGIOC037 : Constants.SGIOC036;

        await Assert.That(expected).Count().IsEqualTo(1);
        await Assert.That(expected[0].GetMessage()).Contains("Node<T>").And.Contains("INode<List<T>>");
        await Assert.That(expected[0].Location.SourceTree!.GetText().ToString(expected[0].Location.SourceSpan)).IsEqualTo(element);
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, other)).IsEmpty();
    }

    [Test]
    public async Task SGIOC036_ExpandingMemberInInjectMembersWithIocInject_ReportsOnceAtMember()
    {
        // [IocInject] on the member takes precedence over InjectMembers, like the generator's MergeInjectionMembers
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], InjectMembers = [nameof(Node<int>.Next)])]

            namespace TestNamespace;

            public interface INode<T>;

            public sealed class Node<T> : INode<T>
            {
                [IocInject]
                public INode<List<T>>? Next { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].Location.SourceTree!.GetText().ToString(sgioc036[0].Location.SourceSpan)).IsEqualTo("Next");
    }

    [Test]
    public async Task SGIOC036_ExpandingKeywordMemberInInjectMembersWithIocInject_ReportsOnceAtMember()
    {
        // A member named by a keyword is matched like any other member
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], InjectMembers = [nameof(Node<int>.@event)])]

            namespace TestNamespace;

            public interface INode<T>;

            public sealed class Node<T> : INode<T>
            {
                [IocInject]
                public INode<List<T>>? @event { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].Location.SourceTree!.GetText().ToString(sgioc036[0].Location.SourceSpan)).IsEqualTo("@event");
    }

    [Test]
    public async Task SGIOC036_ExpandingGenericFactoryKeywordParameter_ReportsAtFactoryParameter()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            public static class NodeFactory
            {
                [IocGenericFactory(typeof(INode<int>), typeof(int))]
                public static INode<TF> Create<TF>(INode<List<TF>> @event) => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]
            public sealed class Node<T> : INode<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].Location.SourceTree!.GetText().ToString(sgioc036[0].Location.SourceSpan)).IsEqualTo("@event");
    }

    [Test]
    [Arguments("Factory = nameof(NodeFactory.Create)")]
    [Arguments("Lifetime = ServiceLifetime.Singleton, Instance = nameof(Node<int>.Default)")]
    public async Task SGIOC036_ExpandingConstructorDependencyOfFactoryOrInstanceRegistration_NoDiagnostic(string arguments)
    {
        // The closed Node<T> is created by the factory or is the instance, so its constructor and members are not resolved
        var source = Usings + $$"""
            using Microsoft.Extensions.DependencyInjection;

            public interface INode<T>;

            public static class NodeFactory
            {
                public static object Create() => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(INode<>)], {{arguments}})]
            public sealed class Node<T>(INode<List<T>> next) : INode<T>
            {
                public static readonly Node<T> Default = null!;

                [IocInject]
                public INode<List<T>>? Other { get; set; }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    public async Task SGIOC036_CycleThroughConstructorOfFactoryRegistration_NoDiagnostic()
    {
        // Node<T> is created by its factory, so Bridge<T>'s INode<List<T>> is not on a cycle
        var source = Usings + """
            public interface INode<T>;
            public interface IBridge<T>;

            public static class NodeFactory
            {
                public static object Create() => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]
            public sealed class Node<T>(IBridge<T> bridge) : INode<T>;

            [IocRegister(ServiceTypes = [typeof(IBridge<>)])]
            public sealed class Bridge<T>(INode<List<T>> node) : IBridge<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    [Arguments("FactoryOnly", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Transient, Factory = nameof(NodeFactory.Create))]", "", Constants.SGIOC036)]
    [Arguments("OwnFactory", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]", Constants.SGIOC036)]
    [Arguments("DefaultFactory", "[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Transient, Factory = nameof(NodeFactory.Create))]", "[IocRegister]", Constants.SGIOC036)]
    [Arguments("Deferred", "", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]", Constants.SGIOC037)]
    public async Task SGIOC036_ExpandingGenericFactoryParameter_ReportsAtFactoryParameter(
        string scenario, string defaults, string registration, string expectedId)
    {
        var parameter = expectedId == Constants.SGIOC037 ? "Lazy<INode<List<TF>>> next" : "INode<List<TF>> next";
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            {{defaults}}

            namespace TestNamespace;

            public interface INode<T>;

            public static class NodeFactory
            {
                [IocGenericFactory(typeof(INode<int>), typeof(int))]
                public static INode<TF> Create<TF>({{parameter}}) => throw new NotImplementedException();
            }

            {{(scenario == "FactoryOnly" ? "" : registration + "\npublic sealed class Node<T> : INode<T>;")}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var expected = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, expectedId).ToList();
        var other = expectedId == Constants.SGIOC036 ? Constants.SGIOC037 : Constants.SGIOC036;

        await Assert.That(expected).Count().IsEqualTo(1);
        await Assert.That(expected[0].GetMessage()).Contains("'INode<List<TF>>'").And.Contains("'NodeFactory.Create<TF>'");
        await Assert.That(expected[0].Location.SourceTree!.GetText().ToString(expected[0].Location.SourceSpan)).IsEqualTo("next");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, other)).IsEmpty();
    }

    [Test]
    [Arguments("[assembly: IocRegisterDefaults(typeof(IRequestHandler<>), ServiceLifetime.Transient, Factory = nameof(HandlerFactory.Create))]", "[IocRegister]")]
    [Arguments("", "[IocRegister(ServiceTypes = [typeof(IRequestHandler<>)], Factory = nameof(HandlerFactory.Create))]")]
    public async Task SGIOC036_GenericFactoryTypeArgumentContainingImplementationTypeParameter_ReportsExpandingDependency(string defaults, string registration)
    {
        // TF takes Task<T> of Handler<T>, so the cycle Handler.T -> TF -> Other.V -> Handler.T nests Task deeper on every round
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            {{defaults}}

            namespace TestNamespace;

            public interface IRequestHandler<TResponse>;
            public interface IOther<V>;

            public static class HandlerFactory
            {
                [IocGenericFactory(typeof(IRequestHandler<int>), typeof(int))]
                public static IRequestHandler<TF> Create<TF>(IOther<TF> other) => throw new NotImplementedException();
            }

            {{registration}}
            public sealed class Handler<T> : IRequestHandler<Task<T>>;

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            public sealed class Other<V>(IRequestHandler<Task<V>> handler) : IOther<V>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var messages = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).Select(d => d.GetMessage()).ToList();

        // The nested link from Handler.T to TF belongs to no dependency, so every dependency on its cycle is expanding
        await Assert.That(messages).Count().IsEqualTo(2);
        await Assert.That(messages).Contains(m => m.Contains("'IOther<TF>'") && m.Contains("'HandlerFactory.Create<TF>'"));
        await Assert.That(messages).Contains(m => m.Contains("'IRequestHandler<Task<V>>'") && m.Contains("'Other<V>'"));
    }

    [Test]
    [Arguments("[assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create), GenericFactoryTypeMapping = [typeof(INode<int>), typeof(int)])]", "", "INode<List<TF>> next")]
    [Arguments("[assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Transient, Factory = nameof(NodeFactory.Create), GenericFactoryTypeMapping = [typeof(INode<int>), typeof(int)])]", "", "INode<List<TF>> next")]
    [Arguments("", "[IocGenericFactory(typeof(INode<int>), typeof(int))]", "IServiceProvider sp, [ServiceKey] object? key, INode<List<TF>> next")]
    public async Task SGIOC036_ExpandingFactoryParameterWithMappingOnAttributeOrServiceParameters_ReportsOnceAtParameter(string registration, string mapping, string parameters)
    {
        // The mapping of the registration attribute is used without [IocGenericFactory]; IServiceProvider and [ServiceKey]
        // parameters are not resolved
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            {{(registration.Length > 0 ? registration : "[assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]")}}

            namespace TestNamespace;

            public interface INode<T>;

            public sealed class Node<T> : INode<T>;

            public static class NodeFactory
            {
                {{mapping}}
                public static INode<TF> Create<TF>({{parameters}}) => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].Location.SourceTree!.GetText().ToString(sgioc036[0].Location.SourceSpan)).IsEqualTo("next");
    }

    [Test]
    [Arguments("Constructor", "[IocRegister] public sealed class Consumer(IA<IB<string>> a);", true)]
    [Arguments("FactoryParameter", "[IocRegister(Factory = nameof(ConsumerFactory.CreateWith))] public sealed class Consumer;", true)]
    [Arguments("ConstructorOfFactory", "[IocRegister(Factory = nameof(ConsumerFactory.Create))] public sealed class Consumer(IA<IB<string>> a);", false)]
    [Arguments("ClosedDecorator", "public interface IService; [IocRegister(ServiceTypes = [typeof(IService)], Decorators = [typeof(Dec)])] public sealed class Consumer : IService; public sealed class Dec(IService inner, IA<IB<string>> a) : IService;", true)]
    [Arguments("GenericFactoryForServiceType", "public interface IHandler<T>; public static class HF { [IocGenericFactory(typeof(IHandler<int>), typeof(int))] public static IHandler<TF> Create<TF>(IA<IB<TF>> a) => throw new NotImplementedException(); } [IocRegister(RegisterAllInterfaces = true, Factory = nameof(HF.Create))] public sealed class Consumer : IHandler<string>;", true)]
    [Arguments("GenericFactoryForOpenServiceType", "public interface IHandler<T>; public static class HF { [IocGenericFactory(typeof(IHandler<int>), typeof(int))] public static IHandler<TF> Create<TF>(IA<IB<TF>> a) => throw new NotImplementedException(); } [IocRegister(ServiceTypes = [typeof(IHandler<>)], Factory = nameof(HF.Create))] public sealed class Consumer : IHandler<string>;", true)]
    [Arguments("GenericFactoryUnmatched","public interface IHandler<T>; public static class HF { [IocGenericFactory(typeof(IHandler<int>), typeof(int))] public static IHandler<TF> Create<TF>(IA<IB<TF>> a) => throw new NotImplementedException(); } [IocRegister(Factory = nameof(HF.Create))] public sealed class Consumer;", false)]
    [Arguments("ConstructorOfInstance","[IocRegister(ServiceLifetime.Singleton, Instance = nameof(Default))] public sealed class Consumer(IA<IB<string>> a) { public static readonly Consumer Default = null!; }", false)]
    public async Task SGIOC036_RootDependencyByActivation_LinksTypeParameterDependency(string scenario, string consumer, bool reports)
    {
        // A's T can only become IB<...> through the root dependency IA<IB<string>>, which closes the cycle through B's IA<List<X>>
        var source = Usings + $$"""
            using Microsoft.Extensions.DependencyInjection;

            public interface IA<T>;
            public interface IB<T>;

            [IocRegister(ServiceTypes = [typeof(IA<>)])]
            public sealed class A<T>(T dependency) : IA<T> where T : class;

            [IocRegister(ServiceTypes = [typeof(IB<>)])]
            public sealed class B<X>(IA<List<X>> a) : IB<X>;

            public static class ConsumerFactory
            {
                public static object Create() => throw new NotImplementedException();

                public static object CreateWith(IA<IB<string>> a) => throw new NotImplementedException();
            }

            {{consumer}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var reported = diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037).ToList();

        await Assert.That(reported.Count > 0).IsEqualTo(reports);
    }

    [Test]
    [Arguments("Node1First", true)]
    [Arguments("Node2First", false)]
    public async Task SGIOC036_FactoryParameterEagerForOneRegistrationDeferredForAnother_ReportsError(string order, bool node1First)
    {
        // TF of Node1 closes an eager cycle through next, while TF of Node2 only closes one through Node1's Lazy dependency
        const string node1 = """
            [IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]
            public sealed class Node1<T>(Lazy<INode<Task<T>>> other) : INode<T>;
            """;
        const string node2 = """
            [IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]
            public sealed class Node2<T> : INode<Task<T>>;
            """;
        var source = Usings + $$"""
            using System.Threading.Tasks;

            public interface INode<T>;

            public static class NodeFactory
            {
                [IocGenericFactory(typeof(INode<int>), typeof(int))]
                public static INode<TF> Create<TF>(INode<List<TF>> next) => throw new NotImplementedException();
            }

            {{(node1First ? node1 : node2)}}

            {{(node1First ? node2 : node1)}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var atNext = diagnostics
            .Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037
                && d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) == "next")
            .ToList();

        await Assert.That(atNext).Count().IsEqualTo(1);
        await Assert.That(atNext[0].Id).IsEqualTo(Constants.SGIOC036);
    }

    [Test]
    public async Task SGIOC036_GenericFactoryWithoutMapping_NoDiagnostic()
    {
        // The generator does not call a generic factory without a mapping (SGIOC016), so its parameters are not dependencies
        var source = Usings + """
            public interface INode<T>;

            public static class NodeFactory
            {
                public static INode<TF> Create<TF>(INode<List<TF>> next) => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(NodeFactory.Create))]
            public sealed class Node<T> : INode<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(diagnostics.Where(d => d.Id is Constants.SGIOC036 or Constants.SGIOC037)).IsEmpty();
    }

    [Test]
    [Arguments("FactoryOnly", "")]
    [Arguments("Implementation", "[IocRegister] public sealed class Handler<T> : IRequestHandler<T>;")]
    public async Task SGIOC036_ExpandingGenericFactoryParameterMappedToPartOfTypeArgument_ReportsAtFactoryParameter(string scenario, string implementation)
    {
        // TF is a part of TResponse (TResponse = Task<TF>), and INode<List<TF>> closes the factory again for List<TF>
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterDefaults(typeof(IRequestHandler<>), ServiceLifetime.Transient, Factory = nameof(HandlerFactory.Create))]
            [assembly: IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Transient, Factory = nameof(NodeFactory.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TResponse>;
            public interface INode<T>;

            public static class HandlerFactory
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<TF>> Create<TF>(INode<List<TF>> next) => throw new NotImplementedException();
            }

            public static class NodeFactory
            {
                [IocGenericFactory(typeof(INode<int>), typeof(int))]
                public static INode<TN> Create<TN>(IRequestHandler<TN> handler) => throw new NotImplementedException();
            }

            {{implementation}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].GetMessage()).Contains("'INode<List<TF>>'").And.Contains("'HandlerFactory.Create<TF>'");
    }

    [Test]
    [Category(Constants.Defaults)]
    [Arguments("public sealed class Holder;", "INode<List<TF>> next", Constants.SGIOC036)]
    [Arguments("public struct Holder;", "INode<List<TF>> next", Constants.SGIOC036)]
    [Arguments("public interface IHolder;", "INode<List<TF>> next", Constants.SGIOC036)]
    [Arguments("public sealed class Holder;", "Lazy<INode<List<TF>>> next", Constants.SGIOC037)]
    public async Task SGIOC036_TypeLevelDefaultsWithFactory_ReportsAtFactoryParameter(string holder, string parameter, string expectedId)
    {
        // Type-level default settings with a Factory and an open generic target register the target, like assembly-level ones
        var source = Usings + $$"""
            using Microsoft.Extensions.DependencyInjection;

            public interface INode<T>;

            public static class NodeFactory
            {
                [IocGenericFactory(typeof(INode<int>), typeof(int))]
                public static INode<TF> Create<TF>({{parameter}}) => throw new NotImplementedException();
            }

            [IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Transient, Factory = nameof(NodeFactory.Create))]
            {{holder}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var expected = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, expectedId).ToList();

        await Assert.That(expected).Count().IsEqualTo(1);
        await Assert.That(expected[0].GetMessage()).Contains("'INode<List<TF>>'").And.Contains("'NodeFactory.Create<TF>'");
        await Assert.That(expected[0].Location.SourceTree!.GetText().ToString(expected[0].Location.SourceSpan)).IsEqualTo("next");
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task SGIOC036_TypeLevelDefaultsFactoryOfRegistration_ReportsAtFactoryParameter()
    {
        // The registration's best default settings are type-level, so the implementation is created by their Factory
        var source = Usings + """
            using Microsoft.Extensions.DependencyInjection;

            public interface INode<T>;

            public static class NodeFactory
            {
                [IocGenericFactory(typeof(INode<int>), typeof(int))]
                public static INode<TF> Create<TF>(INode<List<TF>> next) => throw new NotImplementedException();
            }

            [IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Transient, Factory = nameof(NodeFactory.Create))]
            public sealed class Holder;

            [IocRegister]
            public sealed class Node<T> : INode<T>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc036 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC036).ToList();

        await Assert.That(sgioc036).Count().IsEqualTo(1);
        await Assert.That(sgioc036[0].GetMessage()).Contains("'NodeFactory.Create<TF>'");
    }
}
