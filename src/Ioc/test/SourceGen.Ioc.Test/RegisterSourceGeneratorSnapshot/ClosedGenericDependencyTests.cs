namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for closed generic dependency resolution (Feature #11).
/// When an open generic registration exists and a class depends on its closed version,
/// the generator should automatically create factory registrations for the closed generic type.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.BasicRegistration)]
public class ClosedGenericDependencyTests
{
    [Test]
    public async Task ClosedGenericDependency_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class TestHandler<T>(ILogger<TestHandler<T>> logger)
            {
                private readonly ILogger<TestHandler<T>> logger = logger;
                public void Handle() => logger.Log("Handling");
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(TestHandler<TestEntity> handler)
            {
                private readonly TestHandler<TestEntity> handler = handler;
                public void Query() => handler.Handle();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_TransitiveDependencyOfClosedImplementation_ClosesWithTypeArgument()
    {
        // Outer<string> is closed for Consumer; its dependencies IRepo<string> and ILogger<Outer<string>> are closed in turn
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface ILogger<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(ILogger<>)])]
            internal sealed class Logger<T> : ILogger<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(IRepo<T> repo, ILogger<Outer<T>> logger) : IOuter<T>;

            [IocRegister]
            internal sealed class Consumer(IOuter<string> outer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("class")]
    [Arguments("struct")]
    [Arguments("unmanaged")]
    [Arguments("new()")]
    public async Task ClosedGenericDependency_ImplementationWithSpecialConstraint_ClosesOnlySatisfyingTypeArguments(string constraint)
    {
        // class: string, Constructible, NoDefaultConstructor; struct: int, KeyValuePair; unmanaged: int;
        // new(): int, int?, KeyValuePair, Constructible. KeyValuePair<string, int> holds a reference, so it is not unmanaged.
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T> where T : {{constraint}};

            public class Constructible { }

            public class NoDefaultConstructor(int value) { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer(
                IRepo<string> text,
                IRepo<int> number,
                IRepo<int?> optionalNumber,
                IRepo<KeyValuePair<string, int>> pair,
                IRepo<Constructible> constructible,
                IRepo<NoDefaultConstructor> noDefaultConstructor);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseTextForParameters(constraint.Replace("()", string.Empty));
    }

    [Test]
    public async Task ClosedGenericDependency_ImplementationWithTypeConstraint_ClosesOnlySatisfyingTypeArguments()
    {
        // Entity implements IEntity and DerivedEntity inherits it; PlainEntity does not
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IEntity;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T> where T : IEntity;

            public class Entity : IEntity;
            public class DerivedEntity : Entity;
            public class PlainEntity;

            [IocRegister]
            internal sealed class Consumer(IRepo<Entity> entity, IRepo<DerivedEntity> derived, IRepo<PlainEntity> plain);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_ImplementationsWithDifferentConstraints_ClosesSatisfyingImplementation()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class StructRepo<T> : IRepo<T> where T : struct;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class ClassRepo<T> : IRepo<T> where T : class;

            [IocRegister]
            internal sealed class Consumer(IRepo<int> number, IRepo<string> text);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_TransitiveDependencyWithConstraint_ClosesOnlySatisfyingTypeArguments()
    {
        // Outer<int> and Outer<string> are closed; only IRepo<int> satisfies Repo<T>'s struct constraint
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T> where T : struct;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(IRepo<T> repo) : IOuter<T>;

            [IocRegister]
            internal sealed class Consumer(IOuter<int> number, IOuter<string> text);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_ImplementationConstraintOnObjectEnumOrDelegate_Closes()
    {
        // string converts to object, int to ValueType by boxing, DayOfWeek to Enum, and Action to Delegate
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T, U>;
            public interface IEnumRepo<T>;
            public interface IDelegateRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IHandler<,>)])]
            internal sealed class Handler<T, U> : IHandler<T, U> where T : U;

            [IocRegister(ServiceTypes = [typeof(IEnumRepo<>)])]
            internal sealed class EnumRepo<T> : IEnumRepo<T> where T : Enum;

            [IocRegister(ServiceTypes = [typeof(IDelegateRepo<>)])]
            internal sealed class DelegateRepo<T> : IDelegateRepo<T> where T : Delegate;

            [IocRegister]
            internal sealed class Consumer(
                IHandler<string, object> text,
                IHandler<int, ValueType> number,
                IEnumRepo<DayOfWeek> day,
                IDelegateRepo<Action> action);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_TransitiveDependencyDeclaredAsTypeParameter_Closes()
    {
        // Outer<IRepo<string>> depends on T itself, which stands for IRepo<string>
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(T inner) : IOuter<T>;

            [IocRegister]
            internal sealed class Consumer(IOuter<IRepo<string>> outer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_TransitiveConstructedTypeArgument_ChecksConstraint()
    {
        // List<string> and string[] are reference types, so they do not satisfy Repo<U>'s struct constraint
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<U> : IRepo<U> where U : struct;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(IRepo<List<T>> list, IRepo<T[]> array, IRepo<KeyValuePair<T, int>> pair) : IOuter<T>;

            [IocRegister]
            internal sealed class Consumer(IOuter<string> outer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Timeout(60_000)]
    public async Task ClosedGenericDependency_ExpandingGenericDependency_StopsClosing(CancellationToken cancellationToken)
    {
        // Closing Node<string> requires INode<List<string>>, which would require INode<List<List<string>>>, and so on
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            internal sealed class Node<T>(Lazy<INode<List<T>>> next) : INode<T>;

            [IocRegister]
            internal sealed class Consumer(INode<string> node);
            """;

        // The token stops the generator if the expansion regresses, instead of leaving it running after the timeout
        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, cancellationToken: cancellationToken);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("NodeFirst", "INode<string> node, IOther<string> other")]
    [Arguments("OtherFirst", "IOther<string> other, INode<string> node")]
    public async Task ClosedGenericDependency_ExpandingDependencyAlsoReachedIndependently_ClosesRegardlessOfOrder(string order, string parameters)
    {
        // INode<List<string>> expands Node<string>'s path, but Other<string> needs it on an independent path
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;
            public interface IOther<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            internal sealed class Node<T>(IEnumerable<INode<List<T>>> children) : INode<T>;

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            internal sealed class Other<T>(INode<List<T>> node) : IOther<T>;

            [IocRegister]
            internal sealed class Consumer({{parameters}});
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        // Both orders close Node<List<string>> for Other<string>, and neither expands further
        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Node<global::System.Collections.Generic.List<string>>, global::TestNamespace.Node<global::System.Collections.Generic.List<string>>>();");
        await Assert.That(generatedSource).DoesNotContain("global::TestNamespace.Node<global::System.Collections.Generic.List<global::System.Collections.Generic.List<string>>>");
        await Verify(generatedSource).UseTextForParameters(order);
    }

    [Test]
    [Arguments("NodeFirst", "INode<string> node, IOther<string> other")]
    [Arguments("OtherFirst", "IOther<string> other, INode<string> node")]
    public async Task ClosedGenericDependency_ExpandingCycleThroughSharedRegistration_StopsAtSameDependencyRegardlessOfOrder(string order, string parameters)
    {
        // Node<T> -> IBridge<T> -> INode<List<T>> expands, and Other<string> reaches Bridge<string> independently
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INode<T>;
            public interface IBridge<T>;
            public interface IOther<T>;

            [IocRegister(ServiceTypes = [typeof(INode<>)])]
            internal sealed class Node<T>(IEnumerable<IBridge<T>> bridges) : INode<T>;

            [IocRegister(ServiceTypes = [typeof(IBridge<>)])]
            internal sealed class Bridge<T>(INode<List<T>> node) : IBridge<T>;

            [IocRegister(ServiceTypes = [typeof(IOther<>)])]
            internal sealed class Other<T>(IBridge<T> bridge) : IOther<T>;

            [IocRegister]
            internal sealed class Consumer({{parameters}});
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        // The edge from Bridge<T> to INode<List<T>> expands the cycle, so it is cut in both orders
        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Bridge<string>, global::TestNamespace.Bridge<string>>();");
        await Assert.That(generatedSource).DoesNotContain("global::TestNamespace.Node<global::System.Collections.Generic.List<string>>");
        await Verify(generatedSource).UseTextForParameters(order);
    }

    [Test]
    [Timeout(60_000)]
    [Arguments("DeeperDecorator", """
        public interface IService<T>;

        [IocRegister(ServiceTypes = [typeof(IService<>)], Decorators = [typeof(Decorator<>)])]
        internal sealed class Service<T> : IService<T>;

        internal sealed class Decorator<U>(IService<List<U>> inner, IService<List<List<U>>> other) : IService<List<U>>;

        [IocRegister]
        internal sealed class Consumer(IService<List<string>> service);
        """, "global::TestNamespace.Service<global::System.Collections.Generic.List<global::System.Collections.Generic.List<string>>>")]
    [Arguments("TypeParameterDependency", """
        public interface IA<T>;
        public interface IB<T>;

        [IocRegister(ServiceTypes = [typeof(IA<>)])]
        internal sealed class A<T>(T dependency) : IA<T> where T : class;

        [IocRegister(ServiceTypes = [typeof(IB<>)])]
        internal sealed class B<X>(IA<IB<List<X>>> a) : IB<X>;

        [IocRegister]
        internal sealed class Consumer(IA<IB<string>> a);
        """, "global::TestNamespace.A<global::TestNamespace.IB<global::System.Collections.Generic.List<string>>>")]
    [Arguments("PartlyMatchedDependency", """
        public interface IB<T1, T2>;
        public interface IC<T1, T2>;
        public sealed class Box<T>;

        [IocRegister(ServiceTypes = [typeof(IB<,>)])]
        internal sealed class B<U, V>(IC<U, List<int>> c) : IB<U, List<V>>;

        [IocRegister(ServiceTypes = [typeof(IC<,>)])]
        internal sealed class C<T, S>(IB<Box<T>, S> b) : IC<T, S>;

        [IocRegister]
        internal sealed class Consumer(IC<string, List<int>> c);
        """, "global::TestNamespace.C<global::TestNamespace.Box<string>")]
    public async Task ClosedGenericDependency_ExpandingCycle_StopsClosing(string scenario, string declarations, string expandedType, CancellationToken cancellationToken)
    {
        // Each cycle nests a type argument deeper on every round: through a decorator declared with a deeper service type,
        // through a dependency that is a type parameter, and through a dependency that matches its target only partly
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            {{declarations}}
            """;

        // The token stops the generator if the expansion regresses, instead of leaving it running after the timeout
        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, cancellationToken: cancellationToken);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain(expandedType).Because(scenario);
    }

    [Test]
    public async Task ClosedGenericDependency_ExpandingDependencyWithSeveralImplementations_ClosesRegardlessOfOrder()
    {
        // INode<List<string>> expands Node<string>, but Other<string> needs it independently; NodeB<T> is registered for
        // INode<> too, so an expanding closing must not fall through to it
        static string Generate(string parameters)
        {
            var source = $$"""
                using System;
                using System.Collections.Generic;
                using Microsoft.Extensions.DependencyInjection;
                using SourceGen.Ioc;

                namespace TestNamespace;

                public interface INode<T>;
                public interface IOther<T>;

                [IocRegister(ServiceTypes = [typeof(INode<>)])]
                internal sealed class Node<T>(Lazy<INode<List<T>>> next) : INode<T>;

                [IocRegister(ServiceTypes = [typeof(INode<>)])]
                internal sealed class NodeB<T> : INode<T>;

                [IocRegister(ServiceTypes = [typeof(IOther<>)])]
                internal sealed class Other<T>(INode<List<T>> node) : IOther<T>;

                [IocRegister]
                internal sealed class Consumer({{parameters}});
                """;

            var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
            var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
            return string.Join("\n", (generatedSource ?? string.Empty).Split('\n').Select(static line => line.Trim()).Where(static line => line.StartsWith("services.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        }

        var nodeFirst = Generate("INode<string> node, IOther<string> other");
        var otherFirst = Generate("IOther<string> other, INode<string> node");

        await Assert.That(otherFirst).IsEqualTo(nodeFirst);
        await Assert.That(nodeFirst).Contains("global::TestNamespace.Node<global::System.Collections.Generic.List<string>>");
        await Assert.That(nodeFirst).DoesNotContain("global::System.Collections.Generic.List<global::System.Collections.Generic.List<string>>");
    }

    [Test]
    public async Task ClosedGenericDependency_TypeParameterDependencyOutsideCycle_Closes()
    {
        // W<T>(T inner) can only become types written as type arguments, and no List<> implementation is registered, so
        // Consumer<string> -> Handler<List<string>> -> W<List<string>> ends
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public interface IWrapper<T>;
            public interface IConsumer<T>;

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            internal sealed class Handler<T>(IWrapper<T> wrapper) : IHandler<T>;

            [IocRegister(ServiceTypes = [typeof(IWrapper<>)])]
            internal sealed class W<T>(T inner) : IWrapper<T> where T : class;

            [IocRegister(ServiceTypes = [typeof(IConsumer<>)])]
            internal sealed class Consumer<T>(IHandler<List<T>> handler) : IConsumer<T>;

            [IocRegister]
            internal sealed class Root(IConsumer<string> consumer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.W<global::System.Collections.Generic.List<string>>, global::TestNamespace.W<global::System.Collections.Generic.List<string>>>();");
    }

    [Test]
    public async Task ClosedGenericDependency_CycleThroughDeeperServiceType_Closes()
    {
        // B<U> : IB<List<U>> takes a part of A's type argument, so the cycle A<List<string>> -> B<string> -> A<string> ends
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IA<T>;
            public interface IB<T>;

            [IocRegister(ServiceTypes = [typeof(IA<>)])]
            internal sealed class A<T>(IB<T> b) : IA<T>;

            [IocRegister(ServiceTypes = [typeof(IB<>)])]
            internal sealed class B<U>(IA<U> a) : IB<List<U>>;

            [IocRegister]
            internal sealed class Consumer(IA<List<string>> a);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.B<string>, global::TestNamespace.B<string>>();");
        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.A<string>, global::TestNamespace.A<string>>();");
    }

    [Test]
    public async Task ClosedGenericDependency_TypeArgumentMovedWithoutNesting_Closes()
    {
        // Pair<string, List<string>> needs IPair<List<string>, List<string>>: T takes U's type argument, which contains
        // T's earlier one, but no type argument is nested deeper, so the closings end there
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPair<T, U>;

            [IocRegister(ServiceTypes = [typeof(IPair<,>)])]
            internal sealed class Pair<T, U>(IPair<U, U> next) : IPair<T, U>;

            [IocRegister]
            internal sealed class Consumer(IPair<string, List<string>> pair);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Pair<global::System.Collections.Generic.List<string>, global::System.Collections.Generic.List<string>>, global::TestNamespace.Pair<global::System.Collections.Generic.List<string>, global::System.Collections.Generic.List<string>>>();");
    }

    [Test]
    [Timeout(60_000)]
    public async Task ClosedGenericDependency_ExpandingDecoratorDependency_StopsClosing(CancellationToken cancellationToken)
    {
        // Decorating IService<string> requires IService<List<string>>, whose decorator would require IService<List<List<string>>>
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService<T>;

            [IocRegister(ServiceTypes = [typeof(IService<>)], Decorators = [typeof(Decorator<>)])]
            internal sealed class Service<T> : IService<T>;

            internal sealed class Decorator<T>(IService<T> inner, IService<List<T>> other) : IService<T>;

            [IocRegister]
            internal sealed class Consumer(IService<string> service);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, cancellationToken: cancellationToken);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("global::TestNamespace.Decorator<string>");
        await Assert.That(generatedSource).DoesNotContain("global::TestNamespace.Service<global::System.Collections.Generic.List<string>>");
    }

    [Test]
    public async Task ClosedGenericDependency_ArrayTypeArgumentWithInterfaceConstraint_Closes()
    {
        // int[] implements IEnumerable<int>, and string[] converts to IEnumerable<object> through IEnumerable<string>;
        // int[] does not convert to IEnumerable<object>, because covariance needs a reference type argument
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INumbers<T>;
            public interface IObjects<T>;

            [IocRegister(ServiceTypes = [typeof(INumbers<>)])]
            internal sealed class Numbers<T> : INumbers<T> where T : IEnumerable<int>;

            [IocRegister(ServiceTypes = [typeof(IObjects<>)])]
            internal sealed class Objects<T> : IObjects<T> where T : IEnumerable<object>;

            [IocRegister]
            internal sealed class Consumer(INumbers<int[]> numbers, IObjects<string[]> texts, IObjects<int[]> values);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_TransitiveCovariantTypeArgument_RecomputesConversions()
    {
        // List<T> in Outer<T>(IRepo<List<T>>) becomes List<string> for Outer<string>, which converts to IEnumerable<object>;
        // List<int> for Outer<int> does not, because covariance needs a reference type argument
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<U> : IRepo<U> where U : IEnumerable<object>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(IRepo<List<T>> repo) : IOuter<T>;

            [IocRegister]
            internal sealed class Consumer(IOuter<string> texts, IOuter<int> numbers);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Repo<global::System.Collections.Generic.List<string>>, global::TestNamespace.Repo<global::System.Collections.Generic.List<string>>>();");
        await Assert.That(generatedSource).DoesNotContain("global::TestNamespace.Repo<global::System.Collections.Generic.List<int>>");
    }

    [Test]
    public async Task ClosedGenericDependency_InterfaceTypeArgumentWithObjectConstraint_Closes()
    {
        // An interface converts to object like any reference type, so IEnumerable<IComparable> satisfies IEnumerable<object>
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IObjects<T>;

            [IocRegister(ServiceTypes = [typeof(IObjects<>)])]
            internal sealed class Objects<T> : IObjects<T> where T : IEnumerable<object>;

            [IocRegister]
            internal sealed class Consumer(IObjects<IEnumerable<IComparable>> direct, IObjects<List<IComparable>> implemented);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Objects<global::System.Collections.Generic.IEnumerable<global::System.IComparable>>, global::TestNamespace.Objects<global::System.Collections.Generic.IEnumerable<global::System.IComparable>>>();");
        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Objects<global::System.Collections.Generic.List<global::System.IComparable>>, global::TestNamespace.Objects<global::System.Collections.Generic.List<global::System.IComparable>>>();");
    }

    [Test]
    [Arguments("IVariantRepo<IVariant<string, string>>", "VariantRepo<global::TestNamespace.IVariant<string, string>>", true)]
    [Arguments("IVariantRepo<IVariant<string, object>>", "VariantRepo<global::TestNamespace.IVariant<string, object>>", false)]
    [Arguments("IListRepo<List<string>>", "ListRepo<global::System.Collections.Generic.List<string>>", false)]
    [Arguments("IPair<Func<string>, Func<object>>", "Pair<global::System.Func<string>, global::System.Func<object>>", true)]
    [Arguments("IPair<Func<int>, Func<object>>", "Pair<global::System.Func<int>, global::System.Func<object>>", false)]
    [Arguments("IOuter<IComparable>", "Repo<global::System.Collections.Generic.List<global::System.IComparable>>", true)]
    [Arguments("IArrayOuter<string>", "Repo<string[]>", true)]
    [Arguments("IArrayOuter<int>", "Repo<int[]>", false)]
    [Arguments("INestedRepo<Host<int>.INested<string>>", "NestedRepo<global::TestNamespace.Host<int>.INested<string>>", false)]
    public async Task ClosedGenericDependency_CovariantConstraint_ClosesOnlyForCovariantReferenceConversions(string parameterType, string closedImplementation, bool closes)
    {
        // Only covariant positions with reference type arguments convert, also after substitution (List<T>, T[]) and for
        // a type parameter's type argument (where A : B); invariant, contravariant and nested-in-generic types do not
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IVariant<out TOut, in TIn>;
            public class Host<H> { public interface INested<out X>; }

            public interface IVariantRepo<T>;
            public interface IListRepo<T>;
            public interface IPair<A, B>;
            public interface IRepo<T>;
            public interface IOuter<T>;
            public interface IArrayOuter<T>;
            public interface INestedRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IVariantRepo<>)])]
            internal sealed class VariantRepo<T> : IVariantRepo<T> where T : IVariant<object, string>;

            [IocRegister(ServiceTypes = [typeof(IListRepo<>)])]
            internal sealed class ListRepo<T> : IListRepo<T> where T : IList<object>;

            [IocRegister(ServiceTypes = [typeof(IPair<,>)])]
            internal sealed class Pair<A, B> : IPair<A, B> where A : B;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<U> : IRepo<U> where U : IEnumerable<object>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(IRepo<List<T>> repo) : IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IArrayOuter<>)])]
            internal sealed class ArrayOuter<T>(IRepo<T[]> repo) : IArrayOuter<T>;

            [IocRegister(ServiceTypes = [typeof(INestedRepo<>)])]
            internal sealed class NestedRepo<T> : INestedRepo<T> where T : Host<int>.INested<object>;

            [IocRegister]
            internal sealed class Consumer({{parameterType}} dependency);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        var closedType = $"global::TestNamespace.{closedImplementation}";
        if(closes)
        {
            await Assert.That(generatedSource).Contains($"services.AddTransient<{closedType}, {closedType}>();");
        }
        else
        {
            await Assert.That(generatedSource).DoesNotContain(closedType);
        }
    }

    [Test]
    public async Task ClosedGenericDependency_TypeArgumentWithManyCovariantConversions_Closes()
    {
        // Each position of IGrouping<Many, Many> converts to Many, its 8 interfaces, or object, so a type argument with many
        // interfaces satisfies IGrouping<object, object> like any other
        const string source = """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface I1; public interface I2; public interface I3; public interface I4;
            public interface I5; public interface I6; public interface I7; public interface I8;
            public class Many : I1, I2, I3, I4, I5, I6, I7, I8;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T> where T : IGrouping<object, object>;

            [IocRegister]
            internal sealed class Consumer(IRepo<IGrouping<Many, Many>> repo);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("services.AddTransient<global::TestNamespace.Repo<global::System.Linq.IGrouping<global::TestNamespace.Many, global::TestNamespace.Many>>, global::TestNamespace.Repo<global::System.Linq.IGrouping<global::TestNamespace.Many, global::TestNamespace.Many>>>();");
    }

    [Test]
    public async Task ClosedGenericDependency_DecoratorDependencyWithConstraint_ClosesWithTypeArgumentHierarchy()
    {
        // Decorator<T>(IService<T>, IRepo<T>) decorates IService<Entity>; Entity implements IEntity, so Repo<Entity> is closed,
        // both for the open generic Service<T> and for the closed EntityService
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
            public class Other : IEntity;

            public interface IService<T>;
            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<U> : IRepo<U> where U : IEntity;

            [IocRegister(ServiceTypes = [typeof(IService<>)])]
            internal sealed class Service<T> : IService<T>;

            [IocRegister(ServiceTypes = [typeof(IService<>)])]
            internal sealed class OtherService : IService<Other>;

            internal sealed class Decorator<T>(IService<T> inner, IRepo<T> repo) : IService<T>;

            [IocRegister]
            internal sealed class Consumer(IService<Entity> service);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_ConstructorConstraintWithRequiredMembers_ClosesOnlySatisfyingTypeArguments()
    {
        // Types with required members only satisfy new() when the parameterless constructor sets them
        const string source = """
            using System.Diagnostics.CodeAnalysis;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T> where T : new();

            public class RequiredEntity
            {
                public required string Name { get; init; }
            }

            public class DerivedRequiredEntity : RequiredEntity;

            public class SetsRequiredEntity
            {
                [SetsRequiredMembers]
                public SetsRequiredEntity() => Name = string.Empty;

                public required string Name { get; init; }
            }

            public struct RequiredStruct
            {
                public required int Value;
            }

            public class PlainEntity;

            [IocRegister]
            internal sealed class Consumer(
                IRepo<RequiredEntity> required,
                IRepo<DerivedRequiredEntity> derived,
                IRepo<SetsRequiredEntity> setsRequired,
                IRepo<RequiredStruct> requiredStruct,
                IRepo<PlainEntity> plain);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_InjectedMemberInitializerOfClosedImplementation_SubstitutesTypeParameters()
    {
        // The initializers are copied as fallbacks for the unregistered IBox<T>, so T must become string there too
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IBox<T>;
            public sealed class Box<T> : IBox<T>
            {
                public static Box<T> Empty { get; } = new();
            }

            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T> : IOuter<T>
            {
                [IocInject]
                public IBox<T> Box { get; set; } = new global::TestNamespace.Box<T>();

                [IocInject]
                public IBox<T> Empty { get; set; } = global::TestNamespace.Box<T>.Empty;
            }

            [IocRegister]
            internal sealed class Consumer(IOuter<string> outer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_NameofTypeParameterInInjectedMemberInitializer_KeepsTypeParameterName()
    {
        // nameof(T) is "T" in Outer<T>; the copied fallback must keep that value, and T does not exist in generated code
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T> : IOuter<T>
            {
                [IocInject]
                public string Name { get; set; } = nameof(T);

                [IocInject]
                public global::System.Type Type { get; set; } = typeof(T);
            }

            [IocRegister]
            internal sealed class Consumer(IOuter<string> outer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_TupleTypeArgumentsWithDifferentElementNames_DoNotSatisfyTypeParameterConstraint()
    {
        // C# converts (int A, int B) to (int X, int Y) by identity, but differently named elements mean different things,
        // so Handler<,> is not closed (SGIOC018 reports it)
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T, U>;

            [IocRegister(ServiceTypes = [typeof(IHandler<,>)])]
            internal sealed class Handler<T, U> : IHandler<T, U> where T : U;

            [IocRegister]
            internal sealed class Consumer(IHandler<(int A, int B), (int X, int Y)> handler);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_DecoratorOfServiceDiscoveredThroughImplementation_ClosesConstrainedDependency()
    {
        // Service<Entity> is requested directly, so its IService<List<Entity>> is built from the implementation's type
        // arguments; Decorator<U>'s IRepo<Entity> still closes Repo<U> where U : IEntity
        const string source = """
            using System.Collections.Generic;
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
            internal sealed class Repo<U> : IRepo<U> where U : IEntity;

            [IocRegister(ServiceTypes = [typeof(IService<>)])]
            internal sealed class Service<T> : IService<List<T>>;

            internal sealed class Decorator<U>(IService<List<U>> inner, IRepo<U> repo) : IService<List<U>>;

            [IocRegister]
            internal sealed class Consumer(Service<Entity> service);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_WrappedTransitiveDependency_UnwrapsAndCloses()
    {
        // Outer<string> is closed for Consumer; the services inside its wrapper dependencies are closed in turn
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface ILogger<T>;
            public interface IValidator<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(ILogger<>)])]
            internal sealed class Logger<T> : ILogger<T>;

            [IocRegister(ServiceTypes = [typeof(IValidator<>)])]
            internal sealed class Validator<T> : IValidator<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T>(
                Lazy<IRepo<T>> repo,
                IEnumerable<IValidator<T>> validators,
                Func<ILogger<Outer<T>>> loggerFactory) : IOuter<T>;

            [IocRegister]
            internal sealed class Consumer(IOuter<string> outer);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_InjectionMembersOfClosedImplementation_SubstitutesAndClosesTypeArguments()
    {
        // Outer<string> is closed for Consumer; its injected property, field, and method parameter are closed in turn
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepo<T>;
            public interface ILogger<T>;
            public interface IValidator<T>;
            public interface IOuter<T>;

            [IocRegister(ServiceTypes = [typeof(IRepo<>)])]
            internal sealed class Repo<T> : IRepo<T>;

            [IocRegister(ServiceTypes = [typeof(ILogger<>)])]
            internal sealed class Logger<T> : ILogger<T>;

            [IocRegister(ServiceTypes = [typeof(IValidator<>)])]
            internal sealed class Validator<T> : IValidator<T>;

            [IocRegister(ServiceTypes = [typeof(IOuter<>)])]
            internal sealed class Outer<T> : IOuter<T>
            {
                [IocInject]
                public IRepo<T> Repo { get; set; } = default!;

                [IocInject]
                internal ILogger<Outer<T>> logger = default!;

                [IocInject]
                public void Initialize(IValidator<T> validator) { }
            }

            [IocRegister]
            internal sealed class Consumer(IOuter<string> outer);
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
    public async Task ClosedGenericDependency_WithDecorators_GeneratesFactoryWithDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.HandlerDecorator1<,>), typeof(TestNamespace.HandlerDecorator2<,>)])]

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record TestRequest<T>(System.Guid PK) : IRequest<TestRequest<T>, List<T>>;

            [IocRegister]
            internal sealed class TestHandler<T>(
                ILogger<TestHandler<T>> logger
            ) : IRequestHandler<TestRequest<T>, List<T>>
            {
                private readonly ILogger<TestHandler<T>> logger = logger;

                public List<T> Handle(TestRequest<T> request)
                {
                    logger.Log(request.ToString() ?? string.Empty);
                    return [];
                }
            }

            internal sealed class HandlerDecorator1<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner,
                ILogger<HandlerDecorator1<TRequest, TResponse>> logger
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                private readonly ILogger<HandlerDecorator1<TRequest, TResponse>> logger = logger;

                public TResponse Handle(TRequest request)
                {
                    logger.Log(request.ToString() ?? string.Empty);
                    return inner.Handle(request);
                }
            }

            internal sealed class HandlerDecorator2<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(TestHandler<TestEntity> handler)
            {
                private readonly TestHandler<TestEntity> handler = handler;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("IConvertible")]
    [Arguments("IComparable<T>")]
    [Arguments("EntityBase")]
    [Arguments("GenericBase<T>")]
    public async Task ClosedGenericDependency_DecoratorWithTypeConstraint_AppliesOnlyToSatisfyingTypeArguments(string constraint)
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.ConstrainedDecorator<>)])]

            namespace TestNamespace;

            public interface IHandler<T>;

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            internal sealed class Handler<T> : IHandler<T>;

            internal sealed class ConstrainedDecorator<T>(IHandler<T> inner) : IHandler<T> where T : {{constraint}};

            public interface IEntity;

            public class Entity : IEntity { }

            public class PlainEntity { }

            public class EntityBase { }

            public class DerivedEntity : EntityBase { }

            public class GenericBase<T> { }

            public class SelfEntity : GenericBase<SelfEntity> { }

            // Each type argument satisfies at most one of the constraints; Entity has an unrelated interface and PlainEntity has nothing.
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer(
                IHandler<string> text,
                IHandler<Entity> entity,
                IHandler<PlainEntity> plain,
                IHandler<DerivedEntity> derived,
                IHandler<SelfEntity> self);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseParameters(constraint.Replace("<T>", "OfT"));
    }

    [Test]
    public async Task ClosedGenericDependency_DecoratorWithSeveralTypeParameters_ChecksEachConstraintAgainstItsOwnTypeArgument()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.RequestDecorator<,>)])]

            namespace TestNamespace;

            public interface IRequest;

            public interface IHandler<TRequest, TResponse>;

            [IocRegister(ServiceTypes = [typeof(IHandler<,>)])]
            internal sealed class Handler<TRequest, TResponse> : IHandler<TRequest, TResponse>;

            internal sealed class RequestDecorator<TRequest, TResponse>(IHandler<TRequest, TResponse> inner)
                : IHandler<TRequest, TResponse> where TRequest : IRequest;

            public class Request : IRequest { }

            public class Response : IRequest { }

            public class Other { }

            // Only the first handler's TRequest is an IRequest; in the second, only TResponse is.
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer(IHandler<Request, Other> matching, IHandler<Other, Response> swapped);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("Renamed", "Handler<TA, TB> : IHandler<TA, TB>", "IHandler<Req, Res> handler")]
    [Arguments("SwappedRenamed", "Handler<TB, TA> : IHandler<TA, TB>", "IHandler<Req, Res> handler")]
    [Arguments("PartiallyClosed", "TextHandler<TRequest> : IHandler<TRequest, string>", "IHandler<Req, string> text, IHandler<Req, int> number")]
    [Arguments("NestedTypeArguments", "ListHandler<TItem, TKey> : IHandler<List<TItem>, Dictionary<TKey, TItem>> where TKey : notnull", "IHandler<List<Req>, Dictionary<string, Req>> handler")]
    [Arguments("RepeatedTypeParameter", "ListHandler<T> : IHandler<T, List<T>>", "IHandler<Req, List<Req>> matching, IHandler<Req, List<Res>> mismatched")]
    [Arguments("ArrayTypeArgument", "ArrayHandler<T> : IHandler<T[], T>", "IHandler<Req[], Req> matching, IHandler<Req[], Res> mismatched")]
    [Arguments("ArrayRank", "GridHandler<T> : IHandler<T[,], T>", "IHandler<Req[,], Req> matching, IHandler<Req[], Req> otherRank")]
    [Arguments("JaggedArray", "JaggedHandler<T> : IHandler<T[][], T>", "IHandler<Req[][], Req> matching")]
    public async Task ClosedGenericDependency_ImplementationTypeArgumentsDifferFromServiceType_ClosesImplementationFromImplementedServiceType(
        string name, string declaration, string parameters)
    {
        // Only closed service types the implementation can produce get registrations: not IHandler<Req, int> for
        // TextHandler<TRequest>, not IHandler<Req, List<Res>> for ListHandler<T>, not IHandler<Req[], Res> for
        // ArrayHandler<T>, and not IHandler<Req[], Req> for GridHandler<T>.
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse>;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<,>)])]
            internal sealed class {{declaration}};

            public class Req { }

            public class Res { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer({{parameters}});
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseTextForParameters(name);
    }

    [Test]
    [Arguments("SwappedTypeParameters", "Decorator<TResponse, TRequest>(IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse>", "Decorator<,>", "IHandler<Req, Res> handler")]
    [Arguments("PartiallyClosed", "Decorator<TRequest>(IHandler<TRequest, string> inner) : IHandler<TRequest, string>", "Decorator<>", "IHandler<Req, string> text, IHandler<Req, int> number")]
    [Arguments("ConstraintOnOtherTypeParameter", "Decorator<TRequest, TResponse>(IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>", "Decorator<,>", "IHandler<Req, Res> matching, IHandler<Req, Other> mismatched")]
    [Arguments("TypeParameterConstraint", "Decorator<TRequest, TResponse>(IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse> where TRequest : TResponse", "Decorator<,>", "IHandler<Req, IRequest> matching, IHandler<Other, IRequest> mismatched")]
    [Arguments("ArrayTypeArgument", "Decorator<T>(IHandler<T[], T> inner) : IHandler<T[], T>", "Decorator<>", "IHandler<Req[], Req> matching, IHandler<Req[], Res> mismatched")]
    public async Task ClosedGenericDependency_DecoratorTypeArgumentsDifferFromServiceType_ClosesDecoratorFromImplementedServiceType(
        string name, string decorator, string decoratorType, string parameters)
    {
        // Only the first dependency of each case is decorated: Decorator<Res, Req>, Decorator<Req> for IHandler<Req, string>,
        // IRequest<Res> for Req, Req : IRequest, and Decorator<Req> for IHandler<Req[], Req>.
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.{{decoratorType}})])]

            namespace TestNamespace;

            public interface IRequest;

            public interface IRequest<TResponse>;

            public interface IHandler<TRequest, TResponse>;

            [IocRegister(ServiceTypes = [typeof(IHandler<,>)])]
            internal sealed class Handler<TRequest, TResponse> : IHandler<TRequest, TResponse>;

            internal sealed class {{decorator}};

            public class Req : IRequest, IRequest<Res> { }

            public class Res { }

            public class Other { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer({{parameters}});
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseTextForParameters(name);
    }

    [Test]
    [Arguments("IEnumerable<object>")]
    [Arguments("IReadOnlyList<IComparable>")]
    public async Task ClosedGenericDependency_DecoratorWithCovariantConstraint_AcceptsReferenceTypeConversions(string constraint)
    {
        // List<string> and IEnumerable<string> convert to the constraints through covariance; List<int> does not,
        // because variance does not apply to value types.
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.CovariantDecorator<>)])]

            namespace TestNamespace;

            public interface IHandler<T>;

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            internal sealed class Handler<T> : IHandler<T>;

            internal sealed class CovariantDecorator<T>(IHandler<T> inner) : IHandler<T> where T : {{constraint}};

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer(
                IHandler<List<string>> texts,
                IHandler<List<int>> numbers,
                IHandler<IReadOnlyList<string>> readOnlyTexts);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseTextForParameters(constraint.Replace('<', '(').Replace('>', ')'));
    }

    [Test]
    [Arguments("class")]
    [Arguments("struct")]
    [Arguments("unmanaged")]
    [Arguments("new()")]
    public async Task ClosedGenericDependency_DecoratorWithSpecialConstraint_AppliesOnlyToSatisfyingTypeArguments(string constraint)
    {
        // class: string, Constructible, NoDefaultConstructor; struct: int, KeyValuePair; unmanaged: int;
        // new(): int, int?, KeyValuePair, Constructible. KeyValuePair<string, int> holds a reference, so it is not unmanaged.
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.ConstrainedDecorator<>)])]

            namespace TestNamespace;

            public interface IHandler<T>;

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            internal sealed class Handler<T> : IHandler<T>;

            internal sealed class ConstrainedDecorator<T>(IHandler<T> inner) : IHandler<T> where T : {{constraint}};

            public class Constructible { }

            public class NoDefaultConstructor(int value) { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer(
                IHandler<string> text,
                IHandler<int> number,
                IHandler<int?> optionalNumber,
                IHandler<KeyValuePair<string, int>> pair,
                IHandler<Constructible> constructible,
                IHandler<NoDefaultConstructor> noDefaultConstructor);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseTextForParameters(constraint.Replace("()", string.Empty));
    }

    [Test]
    public async Task ClosedGenericDependency_GenericBaseClassServiceType_ClosesImplementationFromBaseClass()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class HandlerBase<TRequest, TResponse>;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(HandlerBase<,>)])]
            internal sealed class Handler<TB, TA> : HandlerBase<TA, TB>;

            public class Req { }

            public class Res { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Consumer(HandlerBase<Req, Res> handler);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_AlreadyRegistered_DoesNotDuplicate()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal class TestHandler<T>
            {
                public void Handle() { }
            }

            // TestHandler<TestEntity> is explicitly registered via a concrete class
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestHandler<TestEntity>)])]
            internal sealed class TestHandlerTestEntity : TestHandler<TestEntity> { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(TestHandler<TestEntity> handler)
            {
                private readonly TestHandler<TestEntity> handler = handler;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_MultipleClosedTypes_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class Entity1 { }
            public class Entity2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Repository<T>
            {
                public void Save(T entity) { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Service1(Repository<Entity1> repo)
            {
                private readonly Repository<Entity1> repo = repo;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Service2(Repository<Entity2> repo)
            {
                private readonly Repository<Entity2> repo = repo;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ClosedGenericDependency_DependsOnServiceInterface_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.HandlerDecorator1<,>), typeof(TestNamespace.HandlerDecorator2<,>)])]

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record GenericRequest<T>(int Count) : IRequest<GenericRequest<T>, List<T>> where T : new();

            [IocRegister]
            internal sealed class GenericRequestHandler<T>(ILogger<GenericRequestHandler<T>> logger)
                : IRequestHandler<GenericRequest<T>, List<T>> where T : new()
            {
                private readonly ILogger<GenericRequestHandler<T>> logger = logger;

                public List<T> Handle(GenericRequest<T> request)
                {
                    logger.Log(request.ToString() ?? string.Empty);
                    return [];
                }
            }

            internal sealed class HandlerDecorator1<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner,
                ILogger<HandlerDecorator1<TRequest, TResponse>> logger
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                private readonly ILogger<HandlerDecorator1<TRequest, TResponse>> logger = logger;

                public TResponse Handle(TRequest request)
                {
                    logger.Log(request.ToString() ?? string.Empty);
                    return inner.Handle(request);
                }
            }

            internal sealed class HandlerDecorator2<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            public class Entity
            {
                public System.Guid Id { get; init; } = System.Guid.NewGuid();
            }

            // This depends on the service interface, not the implementation type
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(IRequestHandler<GenericRequest<Entity>, List<Entity>> handler)
            {
                private readonly IRequestHandler<GenericRequest<Entity>, List<Entity>> handler = handler;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceProviderInvocation_GetRequiredService_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.HandlerDecorator1<,>)])]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record QueryRequest<T>(Guid PK) : IRequest<QueryRequest<T>, List<T>>;

            [IocRegister]
            internal sealed class QueryRequestHandler<T> : IRequestHandler<QueryRequest<T>, List<T>>
            {
                public List<T> Handle(QueryRequest<T> request) => [];
            }

            internal sealed class HandlerDecorator1<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            public class TestEntity { }

            // This uses GetRequiredService to get a closed generic type - should trigger factory generation
            public class SomeService(IServiceProvider sp)
            {
                private readonly IServiceProvider sp = sp;

                public IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>> GetHandler() =>
                    sp.GetRequiredService<IRequestHandler<QueryRequest<TestEntity>, List<TestEntity>>>();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceProviderInvocation_GetService_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => Console.WriteLine(msg);
            }

            public class MyEntity { }

            // This uses GetService to get a closed generic type - should trigger factory generation
            public class SomeService(IServiceProvider sp)
            {
                private readonly IServiceProvider sp = sp;

                public ILogger<MyEntity>? GetLogger() =>
                    sp.GetService<ILogger<MyEntity>>();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceProviderInvocation_MultipleInvocations_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? Get(Guid id);
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public T? Get(Guid id) => default;
            }

            public class Entity1 { }
            public class Entity2 { }
            public class Entity3 { }

            // Multiple GetRequiredService calls for different closed types
            public class UnitOfWork(IServiceProvider sp)
            {
                private readonly IServiceProvider sp = sp;

                public IRepository<Entity1> Entity1Repo => sp.GetRequiredService<IRepository<Entity1>>();
                public IRepository<Entity2> Entity2Repo => sp.GetRequiredService<IRepository<Entity2>>();
                public IRepository<Entity3> Entity3Repo => sp.GetRequiredService<IRepository<Entity3>>();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IocRegister]
            public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>
            {
                public List<T> Handle(TestRequest<T> request) => [];
            }

            // Using IocDiscoverAttribute to discover closed generic type
            public class ViewModel
            {
                [IocDiscover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
                public void DoAction()
                {
                    // Mediator.Send(new TestRequest<string>());
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_MultipleIocDiscoverAttributes_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IocRegister]
            public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>
            {
                public List<T> Handle(TestRequest<T> request) => [];
            }

            public class Entity1 { }
            public class Entity2 { }

            // Using multiple IocDiscoverAttribute to discover different closed generic types
            [IocDiscover(typeof(IRequestHandler<TestRequest<Entity1>, List<Entity1>>))]
            [IocDiscover(typeof(IRequestHandler<TestRequest<Entity2>, List<Entity2>>))]
            [IocDiscover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_WithDecorators_GeneratesFactoryWithDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.LoggingDecorator<,>)])]

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IocRegister]
            public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>
            {
                public List<T> Handle(TestRequest<T> request) => [];
            }

            public class LoggingDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner,
                ILogger<LoggingDecorator<TRequest, TResponse>> logger
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request)
                {
                    logger.Log("Before");
                    var result = inner.Handle(request);
                    logger.Log("After");
                    return result;
                }
            }

            // Using IocDiscoverAttribute to discover closed generic type
            [IocDiscover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_OnMethod_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? Get(int id);
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public T? Get(int id) => default;
            }

            public class Entity { }

            // IocDiscoverAttribute on method
            public class Service
            {
                [IocDiscover(typeof(IRepository<Entity>))]
                public void Process()
                {
                    // Some indirect usage
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_OnAssembly_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocDiscover(typeof(TestNamespace.IRepository<TestNamespace.Entity1>))]
            [assembly: IocDiscover(typeof(TestNamespace.IRepository<TestNamespace.Entity2>))]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? Get(int id);
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public T? Get(int id) => default;
            }

            public class Entity1 { }
            public class Entity2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_IgnoresNonGenericTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class Service : IService { }

            // IocDiscoverAttribute with non-generic type should be ignored
            [IocDiscover(typeof(IService))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_IgnoresOpenGenericTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            // IocDiscoverAttribute with open generic type should be ignored (if even possible)
            // Note: typeof(IRepository<>) in attribute is open generic
            [IocDiscover(typeof(IRepository<>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests IocDiscoverAttribute with nested open generic service interface (e.g., IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt;).
    /// This is the scenario from IocSample where GenericRequestHandler2 implements IRequestHandler&lt;GenericRequest2&lt;T&gt;, List&lt;T&gt;&gt;.
    /// </summary>
    [Test]
    public async Task IocDiscoverAttribute_WithNestedOpenGenericServiceInterface_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            // GenericRequest2<T> is a nested open generic when used as type argument
            public sealed record GenericRequest2<T>(int Count) : IRequest<GenericRequest2<T>, List<T>> where T : new();

            // GenericRequestHandler2<T> implements IRequestHandler<GenericRequest2<T>, List<T>>
            // This is a nested open generic service interface
            [IocRegister]
            internal sealed class GenericRequestHandler2<T> : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest2<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity2 { }

            // Using IocDiscoverAttribute to discover the closed generic service type
            // IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>
            internal sealed class ViewModel2
            {
                [IocDiscover(typeof(IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>))]
                public void SendRequest2()
                {
                    // Some indirect usage via mediator pattern
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in [IocInject] method parameters are discovered.
    /// The generator should automatically create factory registrations for the closed generic type.
    /// </summary>
    [Test]
    public async Task InjectMethod_ClosedGenericParameter_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record GenericRequest<T>(int Count) : IRequest<GenericRequest<T>, List<T>> where T : new();

            [IocRegister]
            internal sealed class GenericRequestHandler<T> : IRequestHandler<GenericRequest<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity { }

            // This class uses [IocInject] method with closed generic parameter
            // The generator should discover IRequestHandler<GenericRequest<Entity>, List<Entity>>
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel
            {
                private IRequestHandler<GenericRequest<Entity>, List<Entity>>? handler;

                [IocInject]
                public void Initialize(IRequestHandler<GenericRequest<Entity>, List<Entity>> handler)
                {
                    this.handler = handler;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in [IocInject] property are discovered.
    /// </summary>
    [Test]
    public async Task InjectProperty_ClosedGenericType_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IocRegister]
            internal sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            internal class User { }

            // This class uses [IocInject] property with closed generic type
            // The generator should discover IRepository<User>
            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            internal sealed class UserService
            {
                [IocInject]
                public IRepository<User> Repository { get; init; } = null!;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that multiple closed generic dependencies in [IocInject] method parameters are all discovered.
    /// </summary>
    [Test]
    public async Task InjectMethod_MultipleClosedGenericParameters_GeneratesAllFactoryRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IocRegister]
            internal sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            internal class User { }
            internal class Order { }
            internal class Product { }

            // This class uses [IocInject] method with multiple closed generic parameters
            // The generator should discover all three: IRepository<User>, IRepository<Order>, IRepository<Product>
            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            internal sealed class DashboardService
            {
                private IRepository<User>? userRepo;
                private IRepository<Order>? orderRepo;
                private IRepository<Product>? productRepo;

                [IocInject]
                public void Initialize(
                    IRepository<User> userRepo,
                    IRepository<Order> orderRepo,
                    IRepository<Product> productRepo)
                {
                    this.userRepo = userRepo;
                    this.orderRepo = orderRepo;
                    this.productRepo = productRepo;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in [IocInject] method parameters with decorators work correctly.
    /// </summary>
    [Test]
    public async Task InjectMethod_ClosedGenericParameter_WithDecorators_GeneratesFactoryWithDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.LoggingDecorator<,>)])]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record TestRequest<T>(int Count) : IRequest<TestRequest<T>, List<T>> where T : new();

            [IocRegister]
            internal sealed class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>> where T : new()
            {
                public List<T> Handle(TestRequest<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal sealed class LoggingDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request)
                {
                    System.Console.WriteLine($"Handling {typeof(TRequest).Name}");
                    return inner.Handle(request);
                }
            }

            internal class Entity { }

            // This class uses [IocInject] method with closed generic parameter
            // The decorator should be applied to the closed generic registration
            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel
            {
                private IRequestHandler<TestRequest<Entity>, List<Entity>>? handler;

                [IocInject]
                public void Initialize(IRequestHandler<TestRequest<Entity>, List<Entity>> handler)
                {
                    this.handler = handler;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests combined scenario: constructor with closed generic + [IocInject] method with different closed generic.
    /// Both should be discovered and generate factory registrations.
    /// </summary>
    [Test]
    public async Task MixedInjection_ConstructorAndInjectMethod_BothClosedGenerics_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IocRegister]
            internal sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            internal class User { }
            internal class Order { }

            // This class has closed generic in constructor and different closed generic in [IocInject] method
            // Both IRepository<User> and IRepository<Order> should be discovered
            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            internal sealed class OrderService(IRepository<User> userRepo)
            {
                private readonly IRepository<User> userRepo = userRepo;
                private IRepository<Order>? orderRepo;

                [IocInject]
                public void SetOrderRepository(IRepository<Order> orderRepo)
                {
                    this.orderRepo = orderRepo;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in Factory method's additional parameters are discovered.
    /// When a Factory method has parameters that are closed generic types, the generator should
    /// automatically create factory registrations for those closed generic types.
    /// </summary>
    [Test]
    public async Task Factory_WithClosedGenericParameter_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IocRegister]
            public sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            public class Customer { }

            public interface ICustomerService { }

            // This class uses Factory with closed generic parameter IRepository<Customer>
            // The generator should discover IRepository<Customer> from the factory's additional parameters
            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(ICustomerService)],
                Factory = nameof(CustomerServiceFactory.Create))]
            public class CustomerService : ICustomerService { }

            public static class CustomerServiceFactory
            {
                public static ICustomerService Create(IServiceProvider sp, IRepository<Customer> customerRepo)
                    => new CustomerService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that multiple closed generic dependencies in Factory method's additional parameters are all discovered.
    /// </summary>
    [Test]
    public async Task Factory_WithMultipleClosedGenericParameters_GeneratesAllClosedGenericRegistrations()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IocRegister]
            public sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            public class Customer { }
            public class Order { }
            public class Product { }

            public interface IAggregateService { }

            // This class uses Factory with multiple closed generic parameters
            // The generator should discover IRepository<Customer>, IRepository<Order>, and IRepository<Product>
            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IAggregateService)],
                Factory = nameof(AggregateServiceFactory.Create))]
            public class AggregateService : IAggregateService { }

            public static class AggregateServiceFactory
            {
                public static IAggregateService Create(
                    IServiceProvider sp,
                    IRepository<Customer> customerRepo,
                    IRepository<Order> orderRepo,
                    IRepository<Product> productRepo)
                    => new AggregateService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic collection parameters in Factory method are also discovered.
    /// </summary>
    [Test]
    public async Task Factory_WithClosedGenericCollectionParameter_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IHandler<>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            [IocRegister]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) { }
            }

            public class Event { }

            public interface IEventProcessor { }

            // This class uses Factory with IEnumerable<IHandler<Event>> parameter
            // The generator should discover IHandler<Event> from the collection's element type
            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IEventProcessor)],
                Factory = nameof(EventProcessorFactory.Create))]
            public class EventProcessor : IEventProcessor { }

            public static class EventProcessorFactory
            {
                public static IEventProcessor Create(IServiceProvider sp, IEnumerable<IHandler<Event>> handlers)
                    => new EventProcessor();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in decorator's [IocInject] property are discovered.
    /// When a decorator has a property with [IocInject] attribute that depends on a closed generic type,
    /// the generator should automatically create factory registrations for that closed generic type.
    /// </summary>
    [Test]
    public async Task DecoratorInjectionProperty_ClosedGenericDependency_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.LoggingDecorator<,>)])]

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IocRegister]
            public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>
            {
                public List<T> Handle(TestRequest<T> request) => [];
            }

            // Decorator with [IocInject] property that depends on a closed generic type
            // This decorator uses property injection for ILogger<LoggingDecorator<TRequest, TResponse>>
            public class LoggingDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                [IocInject]
                public ILogger<LoggingDecorator<TRequest, TResponse>>? Logger { get; set; }

                public TResponse Handle(TRequest request)
                {
                    Logger?.Log("Before");
                    var result = inner.Handle(request);
                    Logger?.Log("After");
                    return result;
                }
            }

            // This triggers the closed generic resolution for IRequestHandler<TestRequest<string>, List<string>>
            // The decorator's injection property should also trigger ILogger<LoggingDecorator<TestRequest<string>, List<string>>> resolution
            [IocDiscover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in decorator's [IocInject] method parameters are discovered.
    /// </summary>
    [Test]
    public async Task DecoratorInjectionMethod_ClosedGenericDependency_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.CachingDecorator<>)])]

            namespace TestNamespace;

            public interface ICache<T>
            {
                T? Get(string key);
                void Set(string key, T value);
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ICache<>)])]
            public sealed class MemoryCache<T> : ICache<T>
            {
                private readonly Dictionary<string, T> _cache = [];
                public T? Get(string key) => _cache.TryGetValue(key, out var value) ? value : default;
                public void Set(string key, T value) => _cache[key] = value;
            }

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            [IocRegister]
            public class Handler<T> : IHandler<T>
            {
                public void Handle(T item) { }
            }

            // Decorator with [IocInject] method that depends on a closed generic type
            public class CachingDecorator<T>(IHandler<T> inner) : IHandler<T>
            {
                private ICache<T>? cache;

                [IocInject]
                public void Initialize(ICache<T> cache)
                {
                    this.cache = cache;
                }

                public void Handle(T item)
                {
                    // Use cache...
                    inner.Handle(item);
                }
            }

            public class Event { }

            // This triggers the closed generic resolution for IHandler<Event>
            // The decorator's injection method should also trigger ICache<Event> resolution
            [IocDiscover(typeof(IHandler<Event>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }


    [Test]
    public async Task Decorator_ConstrainedOnMetadataTypeInterface_AppliesOnlyToMatchingTypeArguments()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IHandler<>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.ConvertibleDecorator<>)])]

            namespace TestNamespace;

            public interface IHandler<T>;

            internal sealed class ConvertibleDecorator<T>(IHandler<T> inner) : IHandler<T> where T : IConvertible;

            public interface IEntity;

            public class TestEntity : IEntity { }

            // string's interfaces come from metadata, so both string handlers read the same interface data.
            [IocRegister(ServiceTypes = [typeof(IHandler<string>)])]
            internal sealed class StringHandler : IHandler<string>;

            [IocRegister(ServiceTypes = [typeof(IHandler<string>)], Key = "other")]
            internal sealed class OtherStringHandler : IHandler<string>;

            [IocRegister(ServiceTypes = [typeof(IHandler<TestEntity>)])]
            internal sealed class EntityHandler : IHandler<TestEntity>;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceProviderInvocation_TypeNamesResolvedInScope_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;
            using AliasedRepository = TestNamespace.IRepository<TestNamespace.Entity1>;

            namespace TestNamespace;

            public interface IRepository<T>;

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>;

            public class Entity1 { }
            public class Entity2 { }

            // Type arguments use an alias, typeof, and a nested type only visible inside the containing class.
            public class UnitOfWork(IServiceProvider sp)
            {
                public class Nested { }

                private readonly IServiceProvider sp = sp;

                public AliasedRepository Entity1Repo => sp.GetRequiredService<AliasedRepository>();
                public object? Entity2Repo => sp.GetService(typeof(IRepository<Entity2>));
                public IRepository<Nested> NestedRepo => sp.GetRequiredService<IRepository<Nested>>();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
