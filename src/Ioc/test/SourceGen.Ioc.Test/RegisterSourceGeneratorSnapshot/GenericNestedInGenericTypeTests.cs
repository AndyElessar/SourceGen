namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for types nested in a generic type, such as <c>Outer&lt;O&gt;.Node&lt;T&gt;</c>. Open ones are not supported (SGIOC038):
/// their containing type's type arguments are not part of the generator's type model. Closed ones are ordinary types.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.GenericService)]
public class GenericNestedInGenericTypeTests
{
    [Test]
    [Arguments("GenericNested", """
        public static class Outer<O>
        {
            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T> : INode<T>;
        }

        [IocRegister]
        public sealed class Consumer(Outer<int>.INode<string> node);
        """)]
    [Arguments("NonGenericNested", """
        public static class Outer<O>
        {
            public interface INode;

            [IocRegister(RegisterAllInterfaces = true)]
            public sealed class Node : INode;
        }

        [IocRegister]
        public sealed class Consumer(Outer<int>.INode node);
        """)]
    [Arguments("RegisterFor", """
        [IocRegisterFor(typeof(Outer<>.Node<>), ServiceTypes = [typeof(Outer<>.INode<>)])]
        public static class Module;

        public static class Outer<O>
        {
            public interface INode<T>;

            public sealed class Node<T> : INode<T>;
        }

        [IocRegister]
        public sealed class Consumer(Outer<int>.INode<string> node);
        """)]
    public async Task OpenTypeNestedInGenericType_IsNotRegistered(string scenario, string declarations)
    {
        // Outer<O>.Node<T> cannot be closed with the containing type's type arguments, so it is neither registered as an open
        // generic nor closed for Outer<int>.INode<string>
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            {{declarations}}
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain("Outer<O>").Because(scenario);
        await Assert.That(generatedSource).DoesNotContain("typeof(global::TestNamespace.Outer<>)").Because(scenario);
        await Assert.That(generatedSource).DoesNotContain("Node").Because(scenario);
        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Consumer, global::TestNamespace.Consumer>();").Because(scenario);
    }

    [Test]
    [Arguments("ServiceTypeOfClosedImplementation", "", """
        [IocRegister(ServiceTypes = [typeof(Outer<>.INode<>)])]
        public sealed class IntNode : Outer<int>.INode<string>;
        """, "Outer<", "services.AddTransient<global::TestNamespace.IntNode, global::TestNamespace.IntNode>();")]
    [Arguments("DefaultSettingsTarget", "[assembly: IocRegisterDefaults(typeof(Outer<>.INode<>), ServiceLifetime.Singleton)]", """
        [IocRegister]
        public sealed class IntNode : Outer<int>.INode<string>;
        """, "Outer<", "services.AddTransient<global::TestNamespace.IntNode, global::TestNamespace.IntNode>();")]
    [Arguments("DefaultSettingsImplementationType", "[assembly: IocRegisterDefaults(typeof(IService<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(Outer<>.Node<>)])]", "", "Outer<", "services.AddTransient<global::TestNamespace.Consumer, global::TestNamespace.Consumer>();")]
    [Arguments("DefaultSettingsTargetWithImplementationTypes", "[assembly: IocRegisterDefaults(typeof(Outer<>.INode<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(IntNode)])]", """
        public sealed class IntNode : Outer<int>.INode<string>;
        """, "IntNode", "services.AddTransient<global::TestNamespace.Consumer, global::TestNamespace.Consumer>();")]
    [Arguments("RegisterAllInterfacesOfOpenImplementation", "", """
        [IocRegister(RegisterAllInterfaces = true)]
        public sealed class OpenNode<T> : Outer<T>.INode<T>;
        """, "Outer<", "services.AddTransient(typeof(global::TestNamespace.OpenNode<>), typeof(global::TestNamespace.OpenNode<>));")]
    [Arguments("RegisterAllInterfacesOfClosedOpenImplementation", "", """
        [IocRegister(RegisterAllInterfaces = true)]
        public sealed class OpenNode<T> : IService<T>, Outer<T>.INode<T>;
        """, "Outer<", "global::TestNamespace.OpenNode<int>")]
    [Arguments("DefaultSettingsRegisterAllInterfaces", "[assembly: IocRegisterDefaults(typeof(IService<>), ServiceLifetime.Singleton, RegisterAllInterfaces = true)]", """
        [IocRegister(ServiceTypes = [typeof(IService<>)])]
        public sealed class OpenNode<T> : IService<T>, Outer<T>.INode<T>;
        """, "Outer<", "global::TestNamespace.OpenNode<int>")]
    [Arguments("DecoratorOfNestedForm", "", """
        public sealed class NodeDecorator<T>(Outer<T>.INode<T> inner) : Outer<T>.INode<T>;

        [IocRegister(ServiceTypes = [typeof(Outer<int>.INode<int>)], Decorators = [typeof(NodeDecorator<>)])]
        public sealed class IntNode : Outer<int>.INode<int>;
        """, "NodeDecorator", "services.AddTransient<global::TestNamespace.IntNode, global::TestNamespace.IntNode>();")]
    [Arguments("Decorator", "", """
        [IocRegister(ServiceTypes = [typeof(IService<>)], Decorators = [typeof(Outer<>.Decorator<>)])]
        public sealed class Service<T> : IService<T>;
        """, "Decorator", "services.AddTransient<global::TestNamespace.Service<int>, global::TestNamespace.Service<int>>();")]
    public async Task OpenTypeNestedInGenericType_InAttributeArguments_IsIgnored(string scenario, string assemblyAttribute, string declarations, string ignored, string expected)
    {
        // typeof(Outer<>.X<>) cannot be closed with the type arguments of Outer<>, so it is ignored (SGIOC038)
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

                    public sealed class Node<T> : INode<T>, IService<T>;

                    public sealed class Decorator<T>(IService<T> inner) : IService<T>;
                }

                [IocRegister]
                public sealed class Consumer(IService<int> service);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain(ignored).Because(scenario);
        await Assert.That(generatedSource).Contains(expected).Because(scenario);
    }

    [Test]
    public async Task OpenImplementationOfTypeNestedInGenericType_InContainer_DoesNotResolveNestedType()
    {
        // OpenNode<int> is closed for IService<int>, but Outer<T>.INode<T> is not one of its service types (SGIOC038)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService<T>;

            public static class Outer<O>
            {
                public interface INode<T>;
            }

            [IocRegister(RegisterAllInterfaces = true)]
            public sealed class OpenNode<T> : IService<T>, Outer<T>.INode<T>;

            [IocRegister]
            public sealed class Consumer(IService<int> service);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.OpenNode<int>()");
        await Assert.That(generatedSource).DoesNotContain("Outer<");
    }

    [Test]
    public async Task ClosedTypeNestedInGenericType_IsRegisteredAsClosedType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegisterFor(typeof(Outer<int>.Node<string>), ServiceTypes = [typeof(Outer<int>.INode<string>)])]
            public static class Module;

            public static class Outer<O>
            {
                public interface INode<T>;

                public sealed class Node<T> : INode<T>;
            }

            [IocRegister]
            public sealed class Consumer(Outer<int>.INode<string> node);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DependencyNestedInGenericType_DoesNotCloseOpenGenericContainingType()
    {
        // Outer<int>.INode<string> is not a form of Outer<T>, although its name starts with Outer<
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            public sealed class Outer<T> : IOuter<T>
            {
                public interface INode<U>;
            }

            public interface IOuter<T>
            {
                public interface INode<U>;
            }

            [IocRegister]
            public sealed class Consumer(Outer<int>.INode<string> node, IOuter<int>.INode<string> other);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain("Outer<int>, ");
        await Assert.That(generatedSource).DoesNotContain("Outer<string>");
        await Assert.That(generatedSource).DoesNotContain("AddTransient<global::TestNamespace.Outer<int>");
    }

    [Test]
    public async Task ClosedTypeNestedInGenericType_InContainer_ResolvesClosedType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegisterFor(typeof(Outer<int>.Node<string>), ServiceTypes = [typeof(Outer<int>.INode<string>)])]
            public static class Module;

            public static class Outer<O>
            {
                public interface INode<T>;

                public sealed class Node<T> : INode<T>;

                [IocRegister(ServiceTypes = [typeof(INode<>)])]
                public sealed class OpenNode<T> : INode<T>;
            }

            [IocRegister]
            public sealed class Consumer(Outer<int>.INode<string> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.Outer<int>.Node<string>()");
        await Assert.That(generatedSource).DoesNotContain("OpenNode");
        await Assert.That(generatedSource).DoesNotContain("Outer<O>");
    }
}
