namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for generic factory container generation.
/// These tests verify that [IocGenericFactory] attribute correctly maps
/// open generic service type placeholders to factory method type parameters.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.GenericFactory)]
public class GenericFactoryContainerTests
{
    [Test]
    public async Task Container_WithGenericFactory_GeneratesCorrectFactoryCall()
    {
        // Basic generic factory: Single type parameter mapping
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Entity { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>()
                    => throw new NotImplementedException();
            }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithGenericFactory_MultipleTypeParameters_GeneratesCorrectFactoryCall()
    {
        // Multiple type parameters: Map 'int' -> T1, 'decimal' -> T2
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            public class Entity { }
            public class Dto { }

            public static class FactoryContainer
            {
                [IocGenericFactory(
                    typeof(IRequestHandler<Task<int>, List<decimal>>),
                    typeof(int),
                    typeof(decimal))]
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>()
                    => throw new NotImplementedException();
            }

            public class Handler<T1, T2> : IRequestHandler<Task<T1>, List<T2>> { }

            [IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithGenericFactory_WithServiceProvider_GeneratesProviderParameter()
    {
        // Generic factory that receives IServiceProvider
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Entity { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>(IServiceProvider sp)
                    => throw new NotImplementedException();
            }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithGenericFactory_ReversedTypeParameterMapping_GeneratesCorrectOrder()
    {
        // Reversed mapping: First placeholder (decimal) -> T1, second placeholder (int) -> T2
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            public class Entity { }
            public class Dto { }

            public static class FactoryContainer
            {
                // Reversed: decimal -> T1, int -> T2
                [IocGenericFactory(
                    typeof(IRequestHandler<Task<int>, List<decimal>>),
                    typeof(decimal),
                    typeof(int))]
                public static IRequestHandler<Task<T2>, List<T1>> Create<T1, T2>()
                    => throw new NotImplementedException();
            }

            public class Handler<T1, T2> : IRequestHandler<Task<T2>, List<T1>> { }

            // Discover IRequestHandler<Task<Entity>, List<Dto>>
            // -> Task<Entity> matches Task<int> -> int maps to T2 -> T2 = Entity
            // -> List<Dto> matches List<decimal> -> decimal maps to T1 -> T1 = Dto
            // Result: Create<Dto, Entity>()
            [IocDiscover(typeof(IRequestHandler<Task<Entity>, List<Dto>>))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithGenericFactory_MultipleDiscoveries_GeneratesMultipleFactoryCalls()
    {
        // Multiple discoveries for same open generic with generic factory
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Entity { }
            public class User { }
            public class Order { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>()
                    => throw new NotImplementedException();
            }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            [IocDiscover<IRequestHandler<Task<User>>>]
            [IocDiscover<IRequestHandler<Task<Order>>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithGenericFactory_RegisterImplWin()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create))]

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Entity { }
            public class Entity2 { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>()
                    => throw new NotImplementedException();
            }

            [IocRegister(typeof(IRequestHandler<>))]
            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            [IocDiscover<IRequestHandler<Task<Entity2>>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);        await result.VerifyCompilableAsync();        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithGenericFactory_AlsoSpecifiedImpls()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(TestNamespace.FactoryContainer.Create),
                ImplementationTypes = [typeof(TestNamespace.Handler<TestNamespace.Entity>)])]

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Entity { }
            public class Entity2 { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>()
                    => throw new NotImplementedException();
            }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocDiscover<IRequestHandler<Task<Entity>>>]
            [IocDiscover<IRequestHandler<Task<Entity2>>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
