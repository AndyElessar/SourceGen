using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for Factory and Instance registration attributes.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.FactoryAndInstance)]
public class FactoryAndInstanceTests
{
    [Test]
    public async Task Instance_OpenGeneric_ReadsInstanceOfClosedTypeWithoutOpenRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Instance = nameof(Default))]
            public sealed class Node<T> : INode<T>
            {
                public static readonly Node<T> Default = new();
            }

            [IocRegister]
            internal sealed class Consumer(INode<string> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        // MS.DI would construct Node<T> for an open registration instead of using the instance
        await Assert.That(generatedSource).DoesNotContain("typeof(global::TestNamespace.Node<>)");
        await Assert.That(generatedSource).Contains("services.AddSingleton<global::TestNamespace.INode<string>>(global::TestNamespace.Node<string>.Default)");
        await Assert.That(containerSource).Contains("global::TestNamespace.Node<string>.Default");
    }

    private const string OpenGenericFactoryDeclarations = """
        public interface INode<T>;

        public static class Factories
        {
            public static object CreateNode(IServiceProvider sp) => throw new NotImplementedException();

            [IocGenericFactory(typeof(INode<int>), typeof(int))]
            public static INode<T> CreateServiceType<T>() => new Node<T>();

            [IocGenericFactory(typeof(Node<int>), typeof(int))]
            public static Node<T> CreateImplementation<T>() => new();
        }

        public static class Factories<T>
        {
            public static Node<T> Create() => new();
        }
        """;

    [Test]
    [Arguments("OwnFactory", "", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = nameof(Create))]", "global::TestNamespace.Node<string>.Create(")]
    [Arguments("OtherGenericType", "", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<T>.Create))]", "global::TestNamespace.Factories<string>.Create(")]
    [Arguments("StringFactory", "", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = \"global::TestNamespace.Node<T>.CreateWith\")]", "global::TestNamespace.Node<string>.CreateWith(sp)")]
    [Arguments("DefaultFactory", "[assembly: IocRegisterDefaults(typeof(TestNamespace.INode<>), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.Factories.CreateNode))]", "[IocRegister]", "global::TestNamespace.Factories.CreateNode(sp)")]
    [Arguments("DefaultsImplementationTypes", "[assembly: IocRegisterDefaults(typeof(TestNamespace.INode<>), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.Factories.CreateNode), ImplementationTypes = [typeof(TestNamespace.Node<>)])]", "", "global::TestNamespace.Factories.CreateNode(sp)")]
    [Arguments("GenericFactoryOfImplementation", "","[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories.CreateImplementation))]", "global::TestNamespace.Factories.CreateImplementation<string>(")]
    public async Task Factory_OpenGeneric_DoesNotGenerateOpenRegistration(string scenario, string assemblyAttributes, string attribute, string expected)
    {
        // MS.DI would construct Node<T> for an open registration instead of calling the factory, so only the closed types
        // discovered at compile time are registered, and they call the factory
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{assemblyAttributes}}

            namespace TestNamespace;

            {{OpenGenericFactoryDeclarations}}

            {{attribute}}
            public sealed class Node<T> : INode<T>
            {
                public static Node<T> Create() => new();

                public static Node<T> CreateWith(IServiceProvider sp) => new();
            }

            [IocRegister]
            internal sealed class Consumer(INode<string> node, Node<string> implementation);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain("typeof(global::TestNamespace.Node<>)");
        await Assert.That(generatedSource).DoesNotContain("typeof(global::TestNamespace.INode<>)");
        await Assert.That(generatedSource).Contains(expected);
    }

    [Test]
    public async Task Factory_OpenGenericWithGenericFactory_KeepsOpenRegistrationOfConstructedImplementation()
    {
        // A generic factory is called for the service types its mapping matches: INode<string> calls it, so INode<> is not
        // registered open, while the implementation type is constructed (the mapping never matches Node<T>), like its open
        // registration
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            {{OpenGenericFactoryDeclarations}}

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories.CreateServiceType))]
            public sealed class Node<T> : INode<T>;

            [IocRegister]
            internal sealed class Consumer(INode<string> node, Node<string> implementation);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("NonGenericNamesake", "nameof(Node.Default)", "global::TestNamespace.Node.Default")]
    [Arguments("OtherGenericType", "nameof(Holder<T>.Default)", "global::TestNamespace.Holder<string>.Default")]
    public async Task Instance_OpenGeneric_ClosesOnlyTypeParametersOfImplementation(string scenario, string instance, string expected)
    {
        // Only the implementation's type parameters in the instance path are closed: a non-generic namesake of the
        // implementation type is kept, and a member of another generic type is read from that type
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            public static class Node
            {
                public static readonly Node<string> Default = new();
            }

            public static class Holder<T>
            {
                public static readonly Node<T> Default = new();
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Instance = {{instance}})]
            public sealed class Node<T> : INode<T>;

            [IocRegister]
            internal sealed class Consumer(INode<string> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains(expected);
        }
    }

    [Test]
    [Arguments("UnboundNameof", "[IocRegisterFor(typeof(Node<>), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Instance = nameof(Node<>.Default))] public static class Config; public sealed class Node<T> : INode<T> { public static readonly Node<T> Default = new(); }", "global::TestNamespace.Node<string>.Default")]
    [Arguments("GenericBaseMember", "public abstract class NodeBase<T> { public static readonly Node<T> Default = new(); } [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Instance = nameof(Default))] public sealed class Node<T> : NodeBase<T>, INode<T>;", "global::TestNamespace.NodeBase<string>.Default")]
    public async Task Instance_OpenGeneric_ReadsMemberOfClosedContainingType(string scenario, string declarations, string expected)
    {
        // The containing type of the instance is closed whether it is the implementation type named by an unbound
        // nameof() or a generic base type declaring the member
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            {{declarations}}

            [IocRegister]
            internal sealed class Consumer(INode<string> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains(expected);
        }
    }

    [Test]
    [Arguments("ImplementationMember", "nameof(Create)", "global::TestNamespace.Node<string>.Create(")]
    [Arguments("OtherGenericType", "nameof(Factories<T>.Create)", "global::TestNamespace.Factories<string>.Create(")]
    public async Task Factory_OpenGeneric_ClosesTypeParametersOfImplementation(string scenario, string factory, string expected)
    {
        // A factory whose path and parameters use the implementation's type parameters is called on the closed type, and
        // its closed parameters are collected as closed generic dependencies (IRepo<string> closes Repo<T>)
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public interface INode<T>;

            public static class Factories<T>
            {
                public static Node<T> Create(IRepo<T> repo) => new(repo);
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = {{factory}})]
            public sealed class Node<T>(IRepo<T> repo) : INode<T>
            {
                public static Node<T> Create(IRepo<T> repo) => new(repo);
            }

            [IocRegister]
            internal sealed class Consumer(INode<string> node);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains(expected);
        await Assert.That(containerSource).Contains(expected);
        await Assert.That(containerSource).Contains("new global::TestNamespace.Repo<string>()");
    }

    [Test]
    [Arguments("ImplementationMember", "nameof(Name)", "global::TestNamespace.Node<string>.Name")]
    [Arguments("OtherGenericType", "nameof(Keys<T>.Name)", "global::TestNamespace.Keys<string>.Name")]
    public async Task Key_OpenGeneric_ClosesTypeParametersOfImplementationWithoutOpenRegistration(string scenario, string key, string expected)
    {
        // A key whose path uses the implementation's type parameters can differ per closed type, while an open generic
        // registration has a single key, so only the closed types discovered at compile time are registered
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            public static class Keys<T>
            {
                public const string Name = "node";
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Key = {{key}}, KeyType = KeyType.Csharp)]
            public sealed class Node<T> : INode<T>
            {
                public const string Name = "node";
            }

            [IocRegister]
            internal sealed class Consumer([FromKeyedServices("node")] INode<string> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).DoesNotContain("typeof(global::TestNamespace.Node<>)");
        await Assert.That(generatedSource).Contains(expected);
        await Assert.That(containerSource).Contains(expected);
    }

    [Test]
    public async Task FactoryAndKey_OpenGeneric_ClosesTypeParametersOfImplementation()
    {
        // The example of the issue: Node<T>.Create and Node<T>.Name are read from the closed type
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public interface INode<T>;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(INode<>)], Factory = nameof(Create), Key = nameof(Name), KeyType = KeyType.Csharp)]
            public class Node<T>(IRepo<T> repo) : INode<T>
            {
                public const string Name = "node";

                public static Node<T> Create(IRepo<T> repo) => new(repo);
            }

            [IocRegister]
            internal sealed class Consumer([FromKeyedServices("node")] INode<int> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).DoesNotContain("Node<T>");
        await Assert.That(containerSource).DoesNotContain("Node<T>");
        await Assert.That(generatedSource).Contains("global::TestNamespace.Node<int>.Create(");
        await Assert.That(generatedSource).Contains("global::TestNamespace.Node<int>.Name");
    }

    [Test]
    [Arguments("Factory", "Factory = nameof(Create)")]
    [Arguments("FactoryKey", "Factory = nameof(Create), Key = nameof(Name), KeyType = KeyType.Csharp")]
    [Arguments("ForeignFactoryKey", "Factory = nameof(Factories.Make), Key = nameof(Name), KeyType = KeyType.Csharp")]
    public async Task FactoryOnly_TypeArgumentViolatingConstraint_IsNotClosed(string scenario, string argument)
    {
        // Node<string> violates where T : struct, so the closing is rejected, like a closed generic registration (SGIOC018)
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class Base<U>;

            public static class Factories
            {
                public static object Make() => new();
            }

            [IocRegister(ServiceTypes = [typeof(Node<>), typeof(Base<>)], {{argument}})]
            public sealed class Node<T> : Base<T> where T : struct
            {
                public const string Name = "node";

                public static Node<T> Create() => new();
            }

            [IocRegister]
            internal sealed class Consumer(Base<string>? node = null);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).DoesNotContain("global::TestNamespace.Node<string>");
        }
    }

    [Test]
    [Arguments("Factory", "Factory = nameof(Create)")]
    [Arguments("Key", "Key = nameof(Name), KeyType = KeyType.Csharp")]
    [Arguments("Instance", "Lifetime = ServiceLifetime.Singleton, Instance = nameof(Default)")]
    [Arguments("Constructor", "")]
    public async Task OpenGeneric_TypeParameterNotDeterminedByServiceType_IsNotClosed(string scenario, string argument)
    {
        // Base<int> determines T but not V of Node<T, V>, so no closed Node<int, V> can be written: the closing is rejected
        // instead of generating Node<int, V>.Create (CS0246)
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public abstract class Base<U>;

            [IocRegister(ServiceTypes = [typeof(Base<>)]{{(argument.Length > 0 ? ", " + argument : "")}})]
            public sealed class Node<T, V>(IRepo<T> repo) : Base<T>
            {
                public const string Name = "node";

                public static readonly Node<T, V> Default = new(null!);

                public static Node<T, V> Create(IRepo<T> repo) => new(repo);
            }

            [IocRegister]
            internal sealed class Consumer(Base<int>? node = null);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).DoesNotContain("global::TestNamespace.Node<int");
            await Assert.That(output).DoesNotContain("global::TestNamespace.Node<T");
        }
    }

    [Test]
    [Arguments("Register", "[IocRegister(Factory = nameof(Create), Key = nameof(Name), KeyType = KeyType.Csharp)]", "")]
    [Arguments("RegisterFor", "", "[IocRegisterFor(typeof(Node<>), Factory = nameof(Node<>.Create), Key = nameof(Node<>.Name), KeyType = KeyType.Csharp)] public static class Config;")]
    [Arguments("BaseClassWithOtherTypeParameterName", "[IocRegister(ServiceTypes = [typeof(Node<>), typeof(Base<>)], Factory = nameof(Create), Key = nameof(Name), KeyType = KeyType.Csharp)]", "")]
    public async Task FactoryAndKey_OpenGenericWithoutServiceTypes_ClosesTypeParametersOfImplementation(string scenario, string attribute, string registerFor)
    {
        // An open generic type registered without interfaces is closed like a factory-only registration; its Factory and Key
        // are read from the closed type too. Base<U> is matched by the dependency, while the factory and key use Node's own T.
        var dependency = scenario == "BaseClassWithOtherTypeParameterName" ? "Base<string>" : "Node<string>";
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public abstract class Base<U>;

            {{attribute}}
            public sealed class Node<T>(IRepo<T> repo) : Base<T>
            {
                public const string Name = "node";

                public static Node<T> Create(IRepo<T> repo) => new(repo);
            }

            {{registerFor}}

            [IocRegister]
            internal sealed class Consumer([FromKeyedServices("node")] {{dependency}} node);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        foreach(var output in new[] { generatedSource, containerSource })
        {
            await Assert.That(output).Contains("global::TestNamespace.Node<string>.Create(");
            await Assert.That(output).Contains("global::TestNamespace.Node<string>.Name");
            await Assert.That(output).DoesNotContain("Node<T>");
        }

        await Assert.That(containerSource).Contains("new global::TestNamespace.Repo<string>()");
    }

    [Test]
    [Arguments("Factory", "Factory = nameof(Node<>.Create)")]
    [Arguments("Instance", "Lifetime = ServiceLifetime.Singleton, Instance = nameof(Node<>.Default)")]
    [Arguments("Key", "Key = nameof(Node<>.Name), KeyType = KeyType.Csharp")]
    public async Task NameofOfOwnUnboundType_ClosedImplementation_IsNotRegistered(string scenario, string argument)
    {
        // A closed implementation type is not closed per registration, so its own unbound type cannot be either (SGIOC041)
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            public sealed class Node<T> : INode<T>
            {
                public const string Name = "node";

                public static readonly Node<T> Default = new();

                public static Node<T> Create() => new();
            }

            [IocRegisterFor(typeof(Node<int>), ServiceTypes = [typeof(INode<int>)], {{argument}})]
            public static class Config;

            [IocRegister]
            public sealed class Other;

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).DoesNotContain("global::TestNamespace.Node<");
            await Assert.That(output).Contains("global::TestNamespace.Other");
        }
    }

    [Test]
    [Arguments("Factory", "Factory = nameof(Factories<>.Create)")]
    [Arguments("Instance", "Instance = nameof(Factories<>.Default)")]
    [Arguments("Key", "Key = nameof(Factories<>.Name), KeyType = KeyType.Csharp")]
    public async Task NameofOfUnboundGenericType_NonGenericRegistration_IsNotRegistered(string scenario, string argument)
    {
        // The type arguments of an unbound generic type given with nameof() cannot be determined for a registration that
        // does not declare them, so the registration is ignored (SGIOC041) instead of generating Factories<T>.Create
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            public static class Factories<T>
            {
                public const string Name = "name";

                public static readonly MyService Default = new();

                public static MyService Create() => new();
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], {{argument}})]
            public sealed class MyService : IMyService;

            [IocRegister]
            public sealed class Other;

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).DoesNotContain("MyService");
            await Assert.That(output).Contains("global::TestNamespace.Other");
        }
    }

    [Test]
    [Arguments("GenericRegistration", "[IocRegister(ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<>.Create))] public sealed class Node<T> : INode<T>;")]
    [Arguments("RegisterFor", "[IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<>.Create))] public static class Config; public sealed class Node<T> : INode<T>;")]
    [Arguments("ContainerRegisterFor", "public sealed class Node<T> : INode<T>; [IocContainer(ExplicitOnly = true)] [IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], Factory = nameof(Factories<>.Create))] public partial class ExplicitContainer;")]
    public async Task NameofOfUnboundGenericType_OtherThanImplementation_IsNotRegistered(string scenario, string registration)
    {
        // The type parameters of an unbound generic type other than the implementation type are not the implementation's,
        // even when they have the same names, so the registration is ignored (SGIOC041)
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            public static class Factories<T>
            {
                public static INode<T> Create() => throw new NotImplementedException();
            }

            {{registration}}

            [IocRegister]
            internal sealed class Consumer(INode<string>? node = null);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var (_, output) in SourceGeneratorTestHelper.GetGeneratedSources(result))
        {
            await Assert.That(output).DoesNotContain("Factories");
            await Assert.That(output).DoesNotContain("global::TestNamespace.Node<");
        }
    }

    [Test]
    [Arguments("Assembly", "[assembly: IocRegisterDefaults(typeof(TestNamespace.INode<>), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.Factories<>.Create))]")]
    [Arguments("TypeLevel", "[IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))] public static class Config;")]
    [Arguments("Generic", "[IocRegisterDefaults<INode<int>>(ServiceLifetime.Singleton, Factory = nameof(Factories<>.Create))] public static class Config;")]
    [Arguments("ImplementationTypes", "[IocRegisterDefaults(typeof(INode<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(Other<>)], Factory = nameof(Factories<>.Create))] public static class Config; public sealed class Other<T> : INode<T>;")]
    public async Task NameofOfUnboundGenericType_DefaultSettings_AreIgnored(string scenario, string defaults)
    {
        // Default settings apply to registrations of other types, so the type arguments of an unbound generic type given
        // with nameof() cannot be determined, and the default settings are ignored as a whole (SGIOC041) instead of
        // generating Factories<T>.Create
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{(scenario == "Assembly" ? defaults : "")}}

            namespace TestNamespace;

            public interface INode<T>;

            public static class Factories<T>
            {
                public static INode<T> Create() => throw new NotImplementedException();
            }

            {{(scenario == "Assembly" ? "" : defaults)}}

            [IocRegister]
            public sealed class Node : INode<int>;

            [IocRegister]
            internal sealed class Consumer(INode<string>? node = null);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var (_, output) in SourceGeneratorTestHelper.GetGeneratedSources(result))
        {
            await Assert.That(output).DoesNotContain("Factories");
            await Assert.That(output).DoesNotContain("Other");
        }

        var registration = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        await Assert.That(registration).Contains("global::TestNamespace.Node");
        await Assert.That(registration).DoesNotContain("AddSingleton");
    }

    [Test]
    public async Task NameofOfUnboundGenericType_DefaultSettings_TargetOwnType_AreIgnored()
    {
        // The default factory applies to other types, so even the target's own unbound type is not closed (SGIOC041)
        var source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.Node<>), ServiceLifetime.Singleton, ImplementationTypes = [typeof(TestNamespace.Node<>)], Factory = nameof(TestNamespace.Node<>.Create))]

            namespace TestNamespace;

            public class Node<T>
            {
                public static Node<T> Create() => new();
            }

            public sealed class Sub<U> : Node<U>;

            [IocRegister]
            internal sealed class Consumer(Node<string>? node = null);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var (_, output) in SourceGeneratorTestHelper.GetGeneratedSources(result))
        {
            await Assert.That(output).DoesNotContain(".Create(");
            await Assert.That(output).DoesNotContain("AddSingleton");
        }
    }

    [Test]
    [Arguments("Method", "nameof(Factories<int>.Create)")]
    [Arguments("DelegateField", "nameof(Factories<int>.CreateDelegate)")]
    public async Task Factory_MemberOfClosedGenericType_KeepsTypeArguments(string scenario, string factory)
    {
        // A factory method resolved by nameof(), or a member that is not a method, such as a delegate field, keeps the
        // type arguments of its containing type
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            public static class Factories<T>
            {
                public static MyService Create(IServiceProvider sp) => new();

                public static readonly Func<IServiceProvider, MyService> CreateDelegate = static _ => new();
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Factory = {{factory}})]
            public sealed class MyService : IMyService;

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains("global::TestNamespace.Factories<int>.");
        }
    }

    [Test]
    public async Task Instance_KeywordMemberName_IsEscaped()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Instance = nameof(@default))]
            public sealed class MyService : IMyService
            {
                public static readonly MyService @default = new();
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains("global::TestNamespace.MyService.@default");
        }
    }

    [Test]
    public async Task Instance_MemberOfClosedGenericType_KeepsTypeArguments()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;

            public static class Holder<T>
            {
                public static readonly MyService Default = new();
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Instance = nameof(Holder<int>.Default))]
            public sealed class MyService : IMyService;

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains("global::TestNamespace.Holder<int>.Default");
        }
    }

    [Test]
    [Arguments("DefaultFactory", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), ServiceLifetime.Singleton, Factory = nameof(TestNamespace.MyServiceFactory.Create))]", "")]
    [Arguments("OwnFactory", "", ", Factory = nameof(MyServiceFactory.Create)")]
    public async Task Instance_WithFactory_UsesInstanceInRegisterAndContainer(string scenario, string defaults, string ownFactory)
    {
        // The instance takes precedence over a Factory of the registration (SGIOC010) or of its default settings
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{defaults}}

            namespace TestNamespace;

            public interface IMyService;

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) => throw new NotImplementedException();
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Instance = nameof(Default){{ownFactory}})]
            public sealed class MyService : IMyService
            {
                public static readonly MyService Default = new();
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains("global::TestNamespace.MyService.Default");
            await Assert.That(output).DoesNotContain("MyServiceFactory.Create");
        }
    }

    [Test]
    public async Task Factory_ConstantPath_IsCalledLikeStringLiteral()
    {
        // A const Factory is a path like a string literal: called with the service provider, without other dependencies
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public static class FactoryPaths
            {
                public const string Create = "global::TestNamespace.MyServiceFactory.Create";
            }

            // A factory given as a path has no known return type, so it returns the implementation type
            public static class MyServiceFactory
            {
                public static MyService Create(IServiceProvider sp) => throw new NotImplementedException();
            }

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Factory = FactoryPaths.Create)]
            public sealed class MyService(IRepo<string> repo) : IMyService;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("global::TestNamespace.MyServiceFactory.Create(sp)");
        await Assert.That(generatedSource).DoesNotContain("global::TestNamespace.Repo<string>");
    }

    [Test]
    public async Task Factory_WithNoParameters_GeneratesDirectInvocation()
    {
        const string source = """
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
                public static IMyService Create() => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithKeyedService_NoParameters_GeneratesDirectInvocation()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Key = "myKey",
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create() => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithNameof_GeneratesFactoryRegistration()
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
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithStringPath_GeneratesFactoryRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService)],
                Factory = "TestNamespace.MyServiceFactory.Create")]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                // Return the concrete type for compatibility with string path registration
                public static MyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        // String paths are output as-is without global:: prefix.
        // If the path is a valid fully-qualified name, it will compile successfully.
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithKeyedService_GeneratesKeyedFactoryRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IMyService)],
                Key = "myKey",
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Instance_WithNameof_GeneratesSingletonInstanceRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Instance_WithStringPath_GeneratesSingletonInstanceRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = "TestNamespace.MyService.Default")]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        // String paths are output as-is without global:: prefix.
        // If the path is a valid fully-qualified name, it will compile successfully.
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Instance_WithKeyedService_GeneratesKeyedSingletonInstanceRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Key = "myKey",
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithIocRegisterForAttribute_GeneratesFactoryRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService { }

            [IocRegisterFor(
                typeof(MyService),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_RegisteredForServiceTypeOnly_CallsFactoryWithoutCast()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class FastService : IMyService { }

            public class SafeService : IMyService { }

            [IocRegisterFor<IMyService>(ServiceLifetime.Singleton, Factory = nameof(MyServiceFactory.Create))]
            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) =>
                    Environment.ProcessorCount > 1 ? new FastService() : new SafeService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Instance_WithIocRegisterForAttribute_GeneratesSingletonInstanceRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }

            [IocRegisterFor(
                typeof(MyService),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = "TestNamespace.MyService.Default")]
            public static class ServiceConfigurator { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        // String paths are output as-is without global:: prefix.
        // If the path is a valid fully-qualified name, it will compile successfully.
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithMultipleServiceTypes_GeneratesMultipleFactoryRegistrations()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IFirst), typeof(ISecond)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IFirst, ISecond { }

            public static class MyServiceFactory
            {
                public static MyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Instance_WithMultipleServiceTypes_GeneratesMultipleInstanceRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IFirst), typeof(ISecond)],
                Instance = nameof(MyService.Default))]
            public class MyService : IFirst, ISecond
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments(ServiceLifetime.Transient)]
    [Arguments(ServiceLifetime.Scoped)]
    public async Task Instance_WithNonSingletonLifetime_DoesNotGenerateRegistration(ServiceLifetime lifetime)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.{{lifetime}},
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(MyService.Default))]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseParameters(lifetime);
    }

    [Test]
    public async Task Factory_WithDifferentReturnType_GeneratesCast()
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
                // Returns object instead of IMyService or MyService
                public static object Create(IServiceProvider sp) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithServiceKeyParameter_GeneratesKeyedFactoryWithKeyArgument()
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
                Key = "myKey",
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, [ServiceKey] string key) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithServiceKeyParameterOnly_GeneratesKeyedFactoryWithKeyArgument()
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
                Key = 42,
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create([ServiceKey] int key) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithServiceKeyParameterButNoKey_PassesDefaultKey()
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
                // [ServiceKey] is present but the registration has no Key: the factory gets default
                public static IMyService Create(IServiceProvider sp, [ServiceKey] string key) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithAdditionalDependency_GeneratesServiceResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }
            public class Logger : ILogger { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService
            {
                public MyService(ILogger logger) { }
            }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, ILogger logger) => new MyService(logger);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithMultipleAdditionalDependencies_GeneratesMultipleServiceResolutions()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }
            public interface IConfiguration { }
            public class Logger : ILogger { }
            public class Configuration : IConfiguration { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService
            {
                public MyService(ILogger logger, IConfiguration config) { }
            }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, ILogger logger, IConfiguration config)
                    => new MyService(logger, config);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithOptionalAdditionalDependency_GeneratesGetService()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, ILogger? logger) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithKeyedAdditionalDependency_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(
                    IServiceProvider sp,
                    [FromKeyedServices("main")] ILogger logger)
                    => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithMixedParameters_GeneratesCorrectResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }
            public interface IConfiguration { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IMyService)],
                Key = "myKey",
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(
                    IServiceProvider sp,
                    [ServiceKey] string key,
                    ILogger logger,
                    [FromKeyedServices("config")] IConfiguration config)
                    => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithServiceProviderAndKeyAfterDependencies_PassesArgumentsInDeclarationOrder()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }
            public interface IConfiguration { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IMyService)],
                Key = "myKey",
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(
                    ILogger logger,
                    IServiceProvider sp,
                    IConfiguration config,
                    [ServiceKey] string key)
                    => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithDefaultValueParameter_GeneratesConditionalResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }
            public class DefaultLogger : ILogger { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                private static readonly ILogger _default = new DefaultLogger();
                public static IMyService Create(IServiceProvider sp, ILogger logger = null) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithCollectionParameter_GeneratesGetServicesResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp, IEnumerable<IHandler> handlers) => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_SingleTypeParameter_GeneratesGenericFactoryCall()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            // Use open generic as the target type for default settings
            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }

            // Implementation without explicit ServiceTypes - let defaults match
            [IocRegister]
            public class Handler<T> : IRequestHandler<Task<T>> { }

            public class Entity { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            public sealed class App { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_TwoTypeParameters_GeneratesGenericFactoryCall()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            // Use open generic as the target type for default settings
            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Use different placeholder types: int for T1, decimal for T2
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>>), typeof(int), typeof(decimal))]
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }

            // Handler with 2 type parameters implementing 2 type param interface
            [IocRegister(ServiceTypes = [typeof(IRequestHandler<,>)])]
            public class Handler<T1, T2> : IRequestHandler<Task<T1>, List<T2>> { }

            public class Entity { }
            public class Dto { }

            [IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
            public sealed class App { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_WithServiceProvider_GeneratesGenericFactoryCallWithSp()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            // Use open generic as the target type for default settings
            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>(IServiceProvider sp) => throw new NotImplementedException();
            }

            [IocRegister]
            public class Handler<T> : IRequestHandler<Task<T>>
            {
                public Handler(IServiceProvider sp) { }
            }

            public class Entity { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            public sealed class App { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_TwoTypeParameters_ReversedMapping_GeneratesGenericFactoryCall()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            // Use open generic as the target type for default settings
            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // First placeholder (int) maps to T2, second placeholder (decimal) maps to T1
                // This means when we discover IRequestHandler<Task<Entity>, List<Dto>>:
                //   - Task<Entity> matches Task<int> -> T2 = Entity
                //   - List<Dto> matches List<decimal> -> T1 = Dto
                // So the factory call should be Create<Dto, Entity>()
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<decimal>>), typeof(decimal), typeof(int))]
                public static IRequestHandler<Task<T2>, List<T1>> Create<T1, T2>() => throw new NotImplementedException();
            }

            // Handler with 2 type parameters implementing 2 type param interface
            [IocRegister(ServiceTypes = [typeof(IRequestHandler<,>)])]
            public class Handler<T1, T2> : IRequestHandler<Task<T1>, List<T2>> { }

            public class Entity { }
            public class Dto { }

            // Discover IRequestHandler<Task<Entity>, List<Dto>>
            // With reversed mapping: T1 = Dto, T2 = Entity
            [IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
            public sealed class App { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_TwoTypeParameters_SamePlaceholderType_DoesNotGenerateRegistration()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            // Use open generic as the target type for default settings
            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Both placeholders use int - this is invalid and should not generate registration
                // because we cannot distinguish which type argument maps to T1 vs T2
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<int>>), typeof(int), typeof(int))]
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }

            // Handler with 2 type parameters implementing 2 type param interface
            [IocRegister(ServiceTypes = [typeof(IRequestHandler<,>)])]
            public class Handler<T1, T2> : IRequestHandler<Task<T1>, List<T2>> { }

            public class Entity { }
            public class Dto { }

            [IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
            public sealed class App { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_PlaceholderInArrayOrTuple_GeneratesGenericFactoryCall()
    {
        // Placeholders in array elements and tuple elements are matched like type arguments; an array of another rank
        // does not match, so RankMismatchHandler is not registered with the factory
        const string source = """
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            public static class Factories
            {
                [IocGenericFactory(typeof(IHandler<int[]>), typeof(int))]
                public static IHandler<T[]> CreateArray<T>() => throw new NotImplementedException();

                [IocGenericFactory(typeof(IHandler<int[,]>), typeof(int))]
                public static IHandler<T[,]> CreateRank2<T>() => throw new NotImplementedException();

                [IocGenericFactory(typeof(IHandler<List<int>[][]>), typeof(int))]
                public static IHandler<List<T>[][]> CreateJagged<T>() => throw new NotImplementedException();

                [IocGenericFactory(typeof(IHandler<(int, string)>), typeof(int))]
                public static IHandler<(T, string)> CreateTuple<T>() => throw new NotImplementedException();

                [IocGenericFactory(typeof(IHandler<(int, int, int, int, int, int, int, long)>), typeof(long))]
                public static IHandler<(int, int, int, int, int, int, int, T)> CreateLongTuple<T>() => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(IHandler<Entity[]>)], Factory = nameof(Factories.CreateArray))]
            public sealed class ArrayHandler : IHandler<Entity[]>;

            [IocRegister(ServiceTypes = [typeof(IHandler<Entity[,]>)], Factory = nameof(Factories.CreateRank2))]
            public sealed class Rank2Handler : IHandler<Entity[,]>;

            [IocRegister(ServiceTypes = [typeof(IHandler<Entity[,]>)], Factory = nameof(Factories.CreateArray))]
            public sealed class RankMismatchHandler : IHandler<Entity[,]>;

            [IocRegister(ServiceTypes = [typeof(IHandler<List<Entity>[][]>)], Factory = nameof(Factories.CreateJagged))]
            public sealed class JaggedHandler : IHandler<List<Entity>[][]>;

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity, string)>)], Factory = nameof(Factories.CreateTuple))]
            public sealed class TupleHandler : IHandler<(Entity, string)>;

            [IocRegister(ServiceTypes = [typeof(IHandler<(int, int, int, int, int, int, int, Entity)>)], Factory = nameof(Factories.CreateLongTuple))]
            public sealed class LongTupleHandler : IHandler<(int, int, int, int, int, int, int, Entity)>;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GenericFactory_TypeMappingOnAttribute_SingleTypeParameter_GeneratesGenericFactoryCall()
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
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(int)])]
            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory]; mapping provided on attribute
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(IRequestHandler<>)])]
            public class Handler<T> : IRequestHandler<Task<T>> { }

            public class Entity { }

            [IocDiscover(typeof(IRequestHandler<Task<Entity>>))]
            public sealed class App { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_InheritedThroughDerivedType_ResolvesBaseMethodParameters()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }
            public interface IMyService { }

            [IocRegister<IDependency>]
            public class Dependency : IDependency { }

            [IocRegister(
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(DerivedFactory.Create))]
            public class MyService : IMyService { }

            public class BaseFactory
            {
                public static IMyService Create(IDependency dependency) => new MyService();
            }

            public class DerivedFactory : BaseFactory { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Factory_WithOverloads_UsesCompilerLookup()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider provider) => new MyService();
                public static IMyService Create() => new MyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
