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
    [Arguments("HiddenProperty", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { public new IBar? X { get; set; } }
        """, false)]
    [Arguments("HiddenField", "nameof(Base.X)", """
        public class Base { public IFoo? X; }
        public sealed class Derived : Base { public new IBar? X; }
        """, false)]
    [Arguments("HiddenByMemberOfOtherKind", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { public new void X(IBar bar) { } }
        """, false)]
    [Arguments("HiddenMethod", "nameof(Base.Init)", """
        public class Base { public void Init(IFoo foo) { } }
        public sealed class Derived : Base { public new void Init(IFoo foo) { } }
        """, false)]
    [Arguments("HiddenOnIntermediateBase", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public class Middle : Base { public new IBar? X { get; set; } }
        public sealed class Derived : Middle;
        """, false)]
    [Arguments("InterfaceMemberImplementedByBaseAndHidden", "nameof(IHasX.X)", """
        public interface IHasX { IFoo? X { get; set; } }
        public class Base : IHasX { public IFoo? X { get; set; } }
        public sealed class Derived : Base { public new IBar? X { get; set; } }
        """, false)]
    [Arguments("OverriddenProperty", "nameof(Base.X)", """
        public class Base { public virtual IFoo? X { get; set; } }
        public sealed class Derived : Base { public override IFoo? X { get; set; } }
        """, true)]
    [Arguments("InaccessibleHidingMember", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { private new IBar? X { get; set; } }
        """, true)]
    [Arguments("MethodOverload", "nameof(Base.Init)", """
        public class Base { public void Init(IFoo foo) { } }
        public sealed class Derived : Base { public void Init(IBar bar) { } }
        """, true)]
    [Arguments("InterfaceMemberReimplementedOnRegisteredType", "nameof(IHasX.X)", """
        public interface IHasX { IFoo? X { get; set; } }
        public class Base : IHasX { public IFoo? X { get; set; } }
        public sealed class Derived : Base, IHasX { public new IFoo? X { get; set; } }
        """, true)]
    public async Task InjectMembers_MemberHiddenOnRegisteredType_IsNotInjected(string scenario, string injectMember, string declarations, bool injected)
    {
        // The generated code binds the member by name on the registered type, so a member hidden there (with `new`) is not
        // the member it assigns or calls: it is not injected (SGIOC024)
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo;
            public interface IBar;

            [IocRegister(ServiceTypes = [typeof(IFoo)])]
            public sealed class Foo : IFoo;

            [IocRegister(ServiceTypes = [typeof(IBar)])]
            public sealed class Bar : IBar;

            {{declarations}}
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        if(injected)
            await Assert.That(generatedSource).Contains("<global::TestNamespace.IFoo>()").Because(scenario);
        else
            await Assert.That(generatedSource).DoesNotContain("<global::TestNamespace.IFoo>()").Because(scenario);
        await Assert.That(generatedSource).DoesNotContain("<global::TestNamespace.IBar>()").Because(scenario);
    }

    [Test]
    [Arguments("nameof(Base<int>.X)", "public new IBar<T>? X { get; set; }", false)]
    [Arguments("nameof(IHasX<int>.X)", "public new IBar<T>? X { get; set; }", false)]
    [Arguments("nameof(Base<int>.X)", "", true)]
    public async Task InjectMembers_MemberHiddenOnOpenGenericRegisteredType_IsNotInjected(string injectMember, string derivedMember, bool injected)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived<>), ServiceTypes = [typeof(IDerived<>)], InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo<T>;
            public interface IBar<T>;
            public interface IDerived<T>;

            [IocRegister(ServiceTypes = [typeof(IFoo<>)])]
            public sealed class Foo<T> : IFoo<T>;

            public interface IHasX<T> { IFoo<T>? X { get; set; } }
            public class Base<T> : IHasX<T> { public IFoo<T>? X { get; set; } }
            public sealed class Derived<T> : Base<T>, IDerived<T> { {{derivedMember}} }

            [IocRegister]
            public sealed class Consumer(IDerived<string> derived);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        if(injected)
            await Assert.That(generatedSource).Contains("<global::TestNamespace.IFoo<string>>()");
        else
            await Assert.That(generatedSource).DoesNotContain("<global::TestNamespace.IFoo<string>>()");
    }

    [Test]
    [Arguments("Property", "nameof(Base.X)", """
        public class Base { public IFoo? X { get; set; } }
        public sealed class Derived : Base { [IocInject] public new IBar? X { get; set; } }
        """)]
    [Arguments("Field", "nameof(Base.X)", """
        public class Base { public IFoo? X; }
        public sealed class Derived : Base { [IocInject] public new IBar? X; }
        """)]
    [Arguments("Method", "nameof(Base.Init)", """
        public class Base { public void Init(IFoo foo) { } }
        public sealed class Derived : Base { [IocInject] public new void Init(IFoo foo) { } }
        """)]
    public async Task InjectMembers_MemberHiddenByIocInjectMember_InjectsIocInjectMember(string scenario, string injectMember, string declarations)
    {
        // [IocInject] on the hiding member takes precedence over the InjectMembers element of the same name
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof(Derived), InjectMembers = [{{injectMember}}])]

            namespace TestNamespace;

            public interface IFoo;
            public interface IBar;

            [IocRegister(ServiceTypes = [typeof(IFoo)])]
            public sealed class Foo : IFoo;

            [IocRegister(ServiceTypes = [typeof(IBar)])]
            public sealed class Bar : IBar;

            {{declarations}}
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        // The [IocInject] member is injected once, with its own type
        var expected = scenario == "Method" ? "<global::TestNamespace.IFoo>()" : "<global::TestNamespace.IBar>()";
        var other = scenario == "Method" ? "<global::TestNamespace.IBar>()" : "<global::TestNamespace.IFoo>()";
        await Assert.That(generatedSource).Contains(expected).Because(scenario);
        await Assert.That(generatedSource!.Split(expected).Length).IsEqualTo(2).Because(scenario);
        await Assert.That(generatedSource).DoesNotContain(other).Because(scenario);
    }

    [Test]
    [Arguments("InternalHidingMemberOfReferencedAssembly", "", true)]
    [Arguments("InternalHidingMemberVisibleThroughInternalsVisibleTo", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"TestAssembly\")]", false)]
    public async Task InjectMembers_InternalHidingMemberOfReferencedAssembly_HidesOnlyWhenAccessible(string scenario, string externalAttribute, bool injected)
    {
        // An internal member of another assembly is not visible to the generated code, so it does not hide Base.X there
        var externalSource = $$"""
            {{externalAttribute}}

            namespace External;

            public interface IFoo;
            public interface IBar;

            public class Base { public IFoo? X { get; set; } }
            public sealed class Derived : Base { internal new IBar? X { get; set; } }
            """;

        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(External.Derived), InjectMembers = [nameof(External.Base.X)])]

            namespace TestNamespace;

            [IocRegister(ServiceTypes = [typeof(External.IFoo)])]
            public sealed class Foo : External.IFoo;
            """;

        var externalCompilation = SourceGeneratorTestHelper.CreateCompilation("ExternalAssembly", externalSource);
        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, additionalReferences: [externalCompilation.ToMetadataReference()]);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        if(injected)
            await Assert.That(generatedSource).Contains("<global::External.IFoo>()").Because(scenario);
        else
            await Assert.That(generatedSource).DoesNotContain("<global::External.IFoo>()").Because(scenario);
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

    [Test]
    public async Task CsharpKey_KeywordMemberName_IsEscaped()
    {
        // A member named by a keyword is written with @, like the factory and the key of a registration and its dependency
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            public static class Keys
            {
                public static string @default => "default";
            }

            public static class Factories
            {
                public static Dependency @new(IServiceProvider sp) => new();
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = nameof(Keys.@default), KeyType = KeyType.Csharp, Factory = nameof(Factories.@new))]
            public class Dependency : IDependency { }

            [IocRegister]
            public class Consumer([IocInject(nameof(Keys.@default), KeyType = KeyType.Csharp)] IDependency dependency);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains("global::TestNamespace.Keys.@default");
            await Assert.That(output).Contains("global::TestNamespace.Factories.@new(");
        }
    }

    [Test]
    public async Task CsharpKey_MemberOfClosedGenericType_KeepsTypeArguments()
    {
        // The key of a registration, an [IocInject] parameter and an InjectMembers element keeps the type arguments of
        // the member's containing type
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            public static class Keys<T>
            {
                public static string PrimaryKey => typeof(T).Name;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = nameof(Keys<int>.PrimaryKey), KeyType = KeyType.Csharp)]
            public class Dependency : IDependency { }

            [IocRegister]
            public class Consumer([IocInject(nameof(Keys<int>.PrimaryKey), KeyType = KeyType.Csharp)] IDependency dependency);

            [IocRegisterFor(typeof(MyService),
                InjectMembers = [new object[] { nameof(MyService.Dep), nameof(Keys<int>.PrimaryKey), KeyType.Csharp }])]
            public class MyService
            {
                public IDependency? Dep { get; set; }
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration"), SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") })
        {
            await Assert.That(output).Contains("global::TestNamespace.Keys<int>.PrimaryKey");
            await Assert.That(output).DoesNotContain("global::TestNamespace.Keys.PrimaryKey");
        }
    }
}
