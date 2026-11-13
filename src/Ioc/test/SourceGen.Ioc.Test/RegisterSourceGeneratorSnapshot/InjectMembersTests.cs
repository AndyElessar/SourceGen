namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for the <c>InjectMembers</c> property on registration attributes,
/// which allows specifying property/field/method injection without placing <c>[IocInject]</c>
/// directly on the member.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.InjectMembers)]
public class InjectMembersTests
{
    [Test]
    [Arguments("Property", "nameof(Node<int>.Repo)", """
        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? Repo { get; set; }
        }
        """, "Repo = ")]
    [Arguments("Field", "nameof(Node<int>.repo)", """
        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? repo;
        }
        """, "repo = ")]
    [Arguments("Method", "nameof(Node<int>.Initialize)", """
        public sealed class Node<T> : INode<T>
        {
            public void Initialize(IRepo<T> repo) { }
        }
        """, ".Initialize(")]
    [Arguments("Keyed", "new object[] { nameof(Node<int>.Repo), \"A\" }", """
        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? Repo { get; set; }
        }
        """, "Repo = ")]
    [Arguments("InheritedFromGenericBase", "nameof(Node<int>.Repo)", """
        public abstract class NodeBase<U>
        {
            public IRepo<U>? Repo { get; set; }
        }

        public sealed class Node<T> : NodeBase<T>, INode<T>;
        """, "Repo = ")]
    [Arguments("UnboundGenericNameof", "nameof(Node<>.Repo)", """
        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? Repo { get; set; }
        }
        """, "Repo = ")]
    public async Task InjectMembers_OpenGenericRegistration_UsesMembersOfGenericTypeDefinition(string scenario, string injectMember, string declarations, string injection)
    {
        // nameof(Node<int>.Repo) names a member of the constructed type Node<int>; the open generic registration injects
        // the member of Node<T>, so Node<string> injects IRepo<string>
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface INode<T>;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)], Key = "A")]
            public sealed class KeyedRepo<T> : IRepo<T>;

            {{declarations}}

            [IocRegister]
            public sealed class Consumer(INode<string> node);
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("global::TestNamespace.IRepo<string>").Because(scenario);
        await Assert.That(generatedSource).Contains(injection).Because(scenario);
        await Assert.That(generatedSource).DoesNotContain("<int>").Because(scenario);
    }

    [Test]
    [Arguments("ClosedRegistration", "typeof(Node<string>)", "typeof(INode<string>)", "nameof(Node<int>.Repo)", "Node<T> : NodeBase<T>, INode<T>", "global::TestNamespace.IRepo<string>")]
    [Arguments("NestedBaseTypeArgument", "typeof(Node<>)", "typeof(INode<>)", "nameof(NodeBase<int>.Repo)", "Node<T> : NodeBase<List<T>>, INode<T>", "global::TestNamespace.IRepo<global::System.Collections.Generic.List<string>>")]
    public async Task InjectMembers_NamedWithOtherTypeArguments_UsesTypeArgumentsOfRegisteredType(string scenario, string registeredType, string serviceType, string injectMember, string nodeDeclaration, string expectedDependency)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor({{registeredType}}, ServiceTypes = [{{serviceType}}], InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface INode<T>;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public abstract class NodeBase<U>
            {
                public IRepo<U>? Repo { get; set; }
            }

            public sealed class {{nodeDeclaration}};

            [IocRegister]
            public sealed class Consumer(INode<string> node);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains($"sp.GetService<{expectedDependency}>()").Because(scenario);
        await Assert.That(generatedSource).DoesNotContain("<int>").Because(scenario);
    }

    [Test]
    [Arguments("OpenGenericImplicit", "typeof(Node<>)", "nameof(INode<int>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get; set; }
        }

        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? Repo { get; set; }
        }
        """, "global::TestNamespace.IRepo<string>")]
    [Arguments("OpenGenericImplementedByBase", "typeof(Node<>)", "nameof(INode<int>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get; set; }
        }

        public abstract class NodeBase<U>
        {
            public IRepo<U>? Repo { get; set; }
        }

        public sealed class Node<T> : NodeBase<T>, INode<T>;
        """, "global::TestNamespace.IRepo<string>")]
    [Arguments("ClosedImplicit", "typeof(Node<string>)", "nameof(INode<int>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get; set; }
        }

        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? Repo { get; set; }
        }
        """, "global::TestNamespace.IRepo<string>")]
    [Arguments("GetterOnlyInterfaceMember", "typeof(Node<>)", "nameof(INode<int>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get; }
        }

        public sealed class Node<T> : INode<T>
        {
            public IRepo<T>? Repo { get; set; }
        }
        """, "global::TestNamespace.IRepo<string>")]
    [Arguments("DefaultInterfaceImplementation", "typeof(Node<>)", "nameof(INode<int>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get => null; set { } }
        }

        public sealed class Node<T> : INode<T>;
        """, null)]
    [Arguments("OpenGenericExplicit", "typeof(Node<>)", "nameof(INode<int>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get; set; }
        }

        public sealed class Node<T> : INode<T>
        {
            IRepo<T>? INode<T>.Repo { get; set; }
        }
        """, null)]
    [Arguments("ClosedExplicit", "typeof(Node<string>)", "nameof(INode<string>.Repo)", """
        public interface INode<T>
        {
            IRepo<T>? Repo { get; set; }
        }

        public sealed class Node<T> : INode<T>
        {
            IRepo<T>? INode<T>.Repo { get; set; }
        }
        """, null)]
    [Arguments("NotAMember", "typeof(Node<>)", "nameof(Other.Repo)", """
        public interface INode<T>;

        public sealed class Other
        {
            public IRepo<int>? Repo { get; set; }
        }

        public sealed class Node<T> : INode<T>;
        """, null)]
    public async Task InjectMembers_MemberOfInterfaceOrOtherType_InjectsImplementationOfRegisteredType(string scenario, string registeredType, string injectMember, string declarations, string? expectedDependency)
    {
        // An interface member is injected through its implementation on the registered type; an explicit implementation,
        // or a member the registered type does not have, cannot be assigned and is not injected (SGIOC024)
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor({{registeredType}}, ServiceTypes = [typeof(INode<>)], InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            {{declarations}}

            [IocRegister]
            public sealed class Consumer(INode<string> node);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        if(expectedDependency is not null)
        {
            await Assert.That(generatedSource).Contains($"sp.GetService<{expectedDependency}>()").Because(scenario);
            await Assert.That(generatedSource).Contains("Repo = ").Because(scenario);
        }
        else
        {
            await Assert.That(generatedSource).DoesNotContain("Repo = ").Because(scenario);
        }
        await Assert.That(generatedSource).DoesNotContain("<int>").Because(scenario);
    }

    [Test]
    [Timeout(60_000)]
    public async Task InjectMembers_OpenGenericRegistrationWithExpandingMember_StopsClosing(CancellationToken cancellationToken)
    {
        // Next, listed only in InjectMembers, nests T in List<T> on every round
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], InjectMembers = [nameof(Node<int>.Next)])]

            namespace TestNamespace;

            public interface INode<T>;

            public sealed class Node<T> : INode<T>
            {
                public Lazy<INode<List<T>>>? Next { get; set; }
            }

            [IocRegister]
            public sealed class Consumer(INode<string> node);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, cancellationToken: cancellationToken);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Node<string>>(");
        await Assert.That(generatedSource).DoesNotContain("global::TestNamespace.Node<global::System.Collections.Generic.List<string>>");
        await Assert.That(generatedSource).DoesNotContain("<int>");
    }

    [Test]
    public async Task InjectMembers_OpenGenericRegistrationInContainer_UsesMembersOfGenericTypeDefinition()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Node<>), ServiceTypes = [typeof(INode<>)], InjectMembers = [nameof(Node<int>.Repo)])]

            namespace TestNamespace;

            public interface INode<T>;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            public sealed class Node<T> : INode<T>
            {
                public IRepo<T>? Repo { get; set; }
            }

            [IocRegister]
            public sealed class Consumer(INode<string> node);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.Repo<string>()");
        await Assert.That(generatedSource).DoesNotContain("<int>");
    }

    [Test]
    public async Task InjectMembers_PropertyInjection_NoKey_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService.Dep)])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_FieldInjection_NoKey_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService._dep)])]
            public class MyService
            {
                public IDependency? _dep;
            }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_WithStringKey_GeneratesKeyedInjection()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "myKey")]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [new object[] { nameof(MyService.Dep), "myKey" }])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_And_IocInject_Mixed_IocInjectTakesPriority()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "keyed")]
            public class KeyedDependency : IDependency { }

            // InjectMembers specifies Dep with no key,
            // but [IocInject] on Dep specifies "keyed" key — [IocInject] wins.
            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService.Dep)])]
            public class MyService
            {
                [IocInject(Key = "keyed")]
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_MultipleMembers_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDep1 { }
            public interface IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [nameof(MyService.Dep1), nameof(MyService.Dep2)])]
            public class MyService
            {
                public IDep1? Dep1 { get; set; }
                public IDep2? Dep2 { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectMembers_WithCsharpKeyType_GeneratesKeyedInjection()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            public static class Keys
            {
                public static string PrimaryKey => "primary";
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = nameof(Keys.PrimaryKey), KeyType = KeyType.Csharp)]
            public class Dependency : IDependency { }

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [new object[] { nameof(MyService.Dep), nameof(Keys.PrimaryKey), KeyType.Csharp }])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
