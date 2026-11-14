using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC038: an open type declared in a generic type, such as <c>Outer&lt;O&gt;.Node&lt;T&gt;</c>, is not registered.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC038)]
public class SGIOC038Tests
{
    [Test]
    [Arguments("GenericNested", "", """
        [IocRegister(ServiceTypes = [typeof(INode<>)])]
        public sealed class Node<T> : INode<T>;
        """, "Outer<O>.Node<T>")]
    [Arguments("NonGenericNested", "", """
        [IocRegister]
        public sealed class Plain;
        """, "Outer<O>.Plain")]
    [Arguments("RegisterForUnbound", "[assembly: IocRegisterFor(typeof(Outer<>.Node<>))]", """
        public sealed class Node<T> : INode<T>;
        """, "Outer<O>.Node<T>")]
    public async Task SGIOC038_OpenTypeNestedInGenericType_ReportsError(string scenario, string assemblyAttribute, string nestedDeclaration, string expectedType)
    {
        var source = $$"""
            using SourceGen.Ioc;
            using TestNamespace;

            {{assemblyAttribute}}

            namespace TestNamespace;

            public static class Outer<O>
            {
                public interface INode<T>;

                {{nestedDeclaration}}
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc038 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC038).ToList();

        await Assert.That(sgioc038).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc038[0].Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(sgioc038[0].GetMessage()).Contains($"'{expectedType}'").And.Contains("'Outer<O>'").Because(scenario);
    }

    [Test]
    [Arguments("ServiceTypes", "", """
        [IocRegister(ServiceTypes = [typeof(Outer<>.INode<>)])]
        public sealed class IntNode : Outer<int>.INode<string>;
        """, "typeof(Outer<>.INode<>)", "Outer<O>.INode<T>")]
    [Arguments("Decorators", "", """
        [IocRegister(ServiceTypes = [typeof(IService<>)], Decorators = [typeof(Outer<>.Decorator<>)])]
        public sealed class Service<T> : IService<T>;
        """, "typeof(Outer<>.Decorator<>)", "Outer<O>.Decorator<T>")]
    [Arguments("DefaultSettingsTarget", "[assembly: IocRegisterDefaults(typeof(Outer<>.INode<>), ServiceLifetime.Singleton)]", "", "typeof(Outer<>.INode<>)", "Outer<O>.INode<T>")]
    [Arguments("DefaultSettingsImplementationTypes", "[assembly: IocRegisterDefaults(typeof(IService<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(Outer<>.Decorator<>)])]", "", "typeof(Outer<>.Decorator<>)", "Outer<O>.Decorator<T>")]
    [Arguments("RegisterAllInterfacesOfOpenImplementation", "", """
        [IocRegister(RegisterAllInterfaces = true)]
        public sealed class OpenNode<T> : Outer<T>.INode<T>;
        """, "IocRegister(RegisterAllInterfaces = true)", "Outer<O>.INode<T>")]
    [Arguments("DecoratorOfNestedForm", "", """
        public sealed class NodeDecorator<T>(Outer<T>.INode<T> inner) : Outer<T>.INode<T>;

        [IocRegister(ServiceTypes = [typeof(Outer<int>.INode<int>)], Decorators = [typeof(NodeDecorator<>)])]
        public sealed class IntNode : Outer<int>.INode<int>;
        """, "typeof(NodeDecorator<>)", "Outer<O>.INode<T>")]
    [Arguments("IgnoredDefaultSettingsWithOpenImplementationTypes", "[assembly: IocRegisterDefaults(typeof(Outer<>.INode<>), ServiceLifetime.Singleton, RegisterAllInterfaces = true, ImplementationTypes = [typeof(OpenNode<>)])]", """
        public sealed class OpenNode<T> : Outer<T>.INode<T>;
        """, "typeof(Outer<>.INode<>)", "Outer<O>.INode<T>")]
    [Arguments("DefaultSettingsOpenImplementationTypes", "[assembly: IocRegisterDefaults(typeof(IService<>), ServiceLifetime.Singleton, RegisterAllInterfaces = true, ImplementationTypes = [typeof(OpenNode<>)])]", """
        public sealed class OpenNode<T> : IService<T>, Outer<T>.INode<T>;
        """, "typeof(OpenNode<>)", "Outer<O>.INode<T>")]
    [Arguments("UnboundContainerNonGeneric", "[assembly: IocRegisterFor(typeof(Outer<>.Plain))]", "", "typeof(Outer<>.Plain)", "Outer<O>.Plain")]
    [Arguments("GlobalQualified", "[assembly: IocRegisterFor(typeof(global::TestNamespace.Outer<>.Mid.Node<>))]", "", "typeof(global::TestNamespace.Outer<>.Mid.Node<>)", "Outer<O>.Mid.Node<T>")]
    [Arguments("RegisterForTarget", "[assembly: IocRegisterFor(typeof(Outer<>.Mid.Node<>))]", "", "typeof(Outer<>.Mid.Node<>)", "Outer<O>.Mid.Node<T>")]
    public async Task SGIOC038_OpenTypeNestedInGenericTypeInAttributeArguments_ReportsAtTypeOf(string scenario, string assemblyAttribute, string declarations, string expectedLocation, string expectedType)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            {{assemblyAttribute}}

            namespace TestNamespace
            {
                {{declarations}}

                public interface IService<T>;

                public static class Outer<O>
                {
                    public interface INode<T>;

                    public sealed class Decorator<T>(IService<T> inner) : IService<T>;

                    public sealed class Plain;

                    public static class Mid
                    {
                        public sealed class Node<T>;
                    }
                }
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc038 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC038).ToList();

        await Assert.That(sgioc038).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc038[0].Location.SourceTree!.GetText().ToString(sgioc038[0].Location.SourceSpan)).IsEqualTo(expectedLocation).Because(scenario);
        await Assert.That(sgioc038[0].GetMessage()).Contains($"'{expectedType}'").And.Contains("generic type 'Outer<O>'").Because(scenario);
    }

    [Test]
    [Arguments("ClosedNestedInGeneric", "[assembly: IocRegisterFor(typeof(Outer<int>.Node<string>))]", """
        public static class Outer<O>
        {
            public sealed class Node<T>;
        }
        """)]
    [Arguments("NestedInNonGeneric", "", """
        public static class Outer
        {
            [IocRegister]
            public sealed class Node<T>;
        }
        """)]
    [Arguments("DecoratorWithUnrelatedNestedInterface", "", """
        public interface IService<T>;

        public static class Outer<O>
        {
            public interface IMarker;
        }

        public sealed class Decorator<T>(IService<T> inner) : IService<T>, Outer<T>.IMarker;

        [IocRegister(ServiceTypes = [typeof(IService<>)], Decorators = [typeof(Decorator<>)])]
        public sealed class Service<T> : IService<T>;
        """)]
    public async Task SGIOC038_ClosedOrNotNestedInGenericType_NoDiagnostic(string scenario, string assemblyAttribute, string declarations)
    {
        var source = $$"""
            using SourceGen.Ioc;
            using TestNamespace;

            {{assemblyAttribute}}

            namespace TestNamespace;

            {{declarations}}
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC038)).IsEmpty().Because(scenario);
    }

    [Test]
    [Arguments("RegisterAllInterfaces", "", """
        [IocRegister(RegisterAllInterfaces = true)]
        public sealed class OpenNode<T> : Outer<T>.INode<T>;

        [IocRegister]
        public sealed class Consumer(Outer<int>.INode<int> node);
        """, "INode<int>")]
    [Arguments("DefaultSettingsTargetWithImplementationTypes", "[assembly: IocRegisterDefaults(typeof(Outer<>.INode<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(IntNode)])]", """
        public sealed class IntNode : Outer<int>.INode<string>;

        [IocRegister]
        public sealed class Consumer(IntNode node);
        """, "IntNode")]
    public async Task SGIOC018_ServiceIgnoredWithOpenTypeNestedInGenericType_ReportsUnresolved(string scenario, string assemblyAttribute, string declarations, string unresolved)
    {
        // Like the generator, the container analyzer ignores the nested open interface and the settings that target it
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            {{assemblyAttribute}}

            namespace TestNamespace
            {
                public static class Outer<O>
                {
                    public interface INode<T>;
                }

                {{declarations}}

                [IocContainer(IntegrateServiceProvider = false)]
                public partial class TestContainer;
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1).Because(scenario);
        await Assert.That(sgioc018[0].GetMessage()).Contains($"'{unresolved}'").Because(scenario);
    }

    [Test]
    public async Task SGIOC018_DependencyOnlyRegisteredByOpenTypeNestedInGenericType_ReportsUnresolved()
    {
        // Outer<O>.Node<T> is not registered, so the container cannot resolve Outer<int>.INode<string>
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public static class Outer<O>
            {
                public interface INode<T>;

                [IocRegister(ServiceTypes = [typeof(INode<>)])]
                public sealed class Node<T> : INode<T>;
            }

            [IocRegister]
            public sealed class Consumer(Outer<int>.INode<string> node);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<ContainerAnalyzer>(source);
        var sgioc018 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC018").ToList();

        await Assert.That(sgioc018).Count().IsEqualTo(1);
        await Assert.That(sgioc018[0].GetMessage()).Contains("INode<string>");
    }
}
