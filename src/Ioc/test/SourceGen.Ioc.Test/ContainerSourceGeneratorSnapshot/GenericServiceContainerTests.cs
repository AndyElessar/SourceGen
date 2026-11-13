namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for generic service container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.GenericService)]
public class GenericServiceContainerTests
{
    [Test]
    public async Task Container_OpenGenericDependencyWithArrayOfTypeParameter_ClosesOnlyClosedForm()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;
            public interface IOther<T>;

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            public sealed class Other<T>(INode<T[]> node) : IOther<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T> : INode<T>;

            [IocRegister]
            public sealed class Consumer(IOther<string> other);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.Node<string[]>()");
        await Assert.That(generatedSource).DoesNotContain("T[]");
    }

    [Test]
    public async Task Container_WithGenericService_GeneratesGenericResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            [IocDiscover<IRepository<string>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithOpenGenerics_NoIntegrateServiceProvider_FallbacksToProviderForGenericTypes()
    {
        // When IntegrateServiceProvider = false but there are open generic registrations,
        // the generated code should still fallback to _fallbackProvider for generic types
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger<T> { void Log(string message); }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public class Logger<T> : ILogger<T>
            {
                public void Log(string message) { }
            }

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService : IService
            {
                public MyService(ILogger<MyService> logger) { }
            }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithOpenGenerics_UseSwitchStatement_FallbacksToProviderForGenericTypes()
    {
        // When UseSwitchStatement = true and there are open generic registrations,
        // the generated code should fallback to _fallbackProvider for generic types
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { T Get(int id); }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public T Get(int id) => default!;
            }

            public class User { }

            public interface IUserService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IUserService)])]
            public class UserService : IUserService
            {
                public UserService(IRepository<User> userRepo) { }
            }

            [IocContainer(IntegrateServiceProvider = false, UseSwitchStatement = true)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_ClosedGenericWithQualifiedNameMatchingTypeParameter_KeepsQualifiedName()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }

            public static class Keys
            {
                [IocRegister]
                public class T { }
            }

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            public class Handler<T>(Keys.T marker) : IHandler<T> { }

            [IocDiscover<IHandler<int>>]
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_TransitiveClosedGenericDependency_ResolvesClosedImplementation()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface ILogger<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            public sealed class Outer<T>(IRepo<T> repo, ILogger<Outer<T>> logger) : IOuter<T>;

            [IocRegister]
            public sealed class Consumer(IOuter<string> outer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_ExpandingDependencyAlsoReachedIndependently_StopsExpandingAndResolvesIndependentPath()
    {
        // Node<string> needs INode<List<string>> on an expanding path, but Other<string> needs it on an independent one,
        // so Node<List<string>> is resolved directly, and its own INode<List<List<string>>> is not closed
        const string source = """
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;
            public interface IOther<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            public sealed class Node<T>(IEnumerable<INode<List<T>>> children) : INode<T>;

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            public sealed class Other<T>(INode<List<T>> node) : IOther<T>;

            [IocRegister]
            public sealed class Consumer(INode<string> node, IOther<string> other);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_ExplicitOnly_ExpandingDependencyAlsoReachedIndependently_StopsExpandingAndResolvesIndependentPath()
    {
        // An ExplicitOnly container closes its open generic registrations separately, with the same rules
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;
            public interface IOther<T>;

            public sealed class Node<T>(IEnumerable<INode<List<T>>> children) : INode<T>;

            public sealed class Other<T>(INode<List<T>> node) : IOther<T>;

            public sealed class Consumer(INode<string> node, IOther<string> other);

            [IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
            [IocRegisterFor(typeof(Node<>), ServiceLifetime.Transient, ServiceTypes = [typeof(INode<>)])]
            [IocRegisterFor(typeof(Other<>), ServiceLifetime.Transient, ServiceTypes = [typeof(IOther<>)])]
            [IocRegisterFor<Consumer>(ServiceLifetime.Transient)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_InjectedMemberInitializerOfClosedGeneric_SubstitutesTypeParameters()
    {
        // IBox<T> is not registered, so the container falls back to the initializer, closed as new Box<string>()
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IBox<T>;
            public sealed class Box<T> : IBox<T>;

            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            public sealed class Outer<T> : IOuter<T>
            {
                [IocInject]
                public IBox<T> Box { get; set; } = new global::TestNamespace.Box<T>();
            }

            [IocRegister]
            public sealed class Consumer(IOuter<string> outer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_DecoratorDependencyWithConstraint_ResolvesClosedImplementation()
    {
        // Decorator<T>'s IRepo<Entity> dependency closes Repo<U> where U : IEntity, so the decorator resolves it directly
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IService<>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.Decorator<>)])]

            namespace TestNamespace;

            public interface IEntity;
            public class Entity : IEntity;

            public interface IService<T>;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<U> : IRepo<U> where U : IEntity;

            [IocRegister(ServiceTypes = [typeof(IService<>)])]
            public sealed class Service<T> : IService<T>;

            public sealed class Decorator<T>(IService<T> inner, IRepo<T> repo) : IService<T>;

            [IocRegister]
            public sealed class Consumer(IService<Entity> service);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_OpenGenericWithConstraint_ClosesOnlySatisfyingTypeArguments()
    {
        // IRepo<int> is closed directly and through Outer<int>; IRepo<string> does not satisfy the struct constraint
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T> where T : struct;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            public sealed class Outer<T>(IRepo<T> repo) : IOuter<T>;

            [IocRegister]
            public sealed class Consumer(IRepo<int> number, IOuter<int> outerNumber, IOuter<string> outerText);

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WrappedTransitiveClosedGenericDependency_ResolvesClosedImplementations()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface ILogger<T>;
            public interface IValidator<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>;

            [IocRegister(ServiceTypes = [typeof(IValidator<>)])]
            public sealed class Validator<T> : IValidator<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            public sealed class Outer<T>(
                Lazy<IRepo<T>> repo,
                IEnumerable<IValidator<T>> validators,
                Func<ILogger<Outer<T>>> loggerFactory) : IOuter<T>;

            [IocRegister]
            public sealed class Consumer(IOuter<string> outer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_InjectionMembersOfClosedGeneric_ResolveClosedImplementations()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface ILogger<T>;
            public interface IValidator<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            public sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>;

            [IocRegister(ServiceTypes = [typeof(IValidator<>)])]
            public sealed class Validator<T> : IValidator<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            public sealed class Outer<T> : IOuter<T>
            {
                [IocInject]
                public IRepo<T> Repo { get; set; } = default!;

                [IocInject]
                internal ILogger<Outer<T>> logger = default!;

                [IocInject]
                public void Initialize(IValidator<T> validator) { }
            }

            [IocRegister]
            public sealed class Consumer(IOuter<string> outer);

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_ClosedGenericWithTupleElementNamedLikeTypeParameter_KeepsElementName()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IocRegister]
            public class Options<T> { }

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            public class Handler<T>(Options<(T T, int Count)> options) : IHandler<T> { }

            [IocDiscover<IHandler<string>>]
            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}

