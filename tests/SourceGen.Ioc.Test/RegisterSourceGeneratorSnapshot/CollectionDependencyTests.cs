namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for collection dependency resolution.
/// When an open generic registration exists and a class depends on IEnumerable&lt;T&gt;, T[], etc.,
/// the generator should automatically create factory registrations for the element type.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.Collection)]
public class CollectionDependencyTests
{
    [Test]
    public async Task IEnumerableDependency_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => System.Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<IHandler<TestEntity>> handlers)
            {
                private readonly IEnumerable<IHandler<TestEntity>> handlers = handlers;
                public void Process(TestEntity entity)
                {
                    foreach (var handler in handlers) handler.Handle(entity);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IListDependency_RecognizedAsCollection()
    {
        // IList<T> is recognized as a collection type and resolved via GetServices<T>().ToArray()
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => System.Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IList<IHandler<TestEntity>> handlers)
            {
                private readonly IList<IHandler<TestEntity>> handlers = handlers;
                public void Process(TestEntity entity)
                {
                    foreach (var handler in handlers) handler.Handle(entity);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ArrayDependency_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => System.Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IHandler<TestEntity>[] handlers)
            {
                private readonly IHandler<TestEntity>[] handlers = handlers;
                public void Process(TestEntity entity)
                {
                    foreach (var handler in handlers) handler.Handle(entity);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IReadOnlyListDependency_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => System.Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IReadOnlyList<IHandler<TestEntity>> handlers)
            {
                private readonly IReadOnlyList<IHandler<TestEntity>> handlers = handlers;
                public void Process(TestEntity entity)
                {
                    foreach (var handler in handlers) handler.Handle(entity);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GetServiceWithEnumerable_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IServiceProvider sp)
            {
                private readonly IServiceProvider sp = sp;
                public void Process(TestEntity entity)
                {
                    var handlers = sp.GetRequiredService<IEnumerable<IHandler<TestEntity>>>();
                    foreach (var handler in handlers) handler.Handle(entity);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task MultipleCollectionTypes_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public interface IValidator<T>
            {
                bool Validate(T item);
            }

            public class Entity1 { }
            public class Entity2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => System.Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IValidator<>)])]
            public sealed class Validator<T> : IValidator<T>
            {
                public bool Validate(T item) => item is not null;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(
                IEnumerable<IHandler<Entity1>> handlers1,
                IHandler<Entity2>[] handlers2,
                IReadOnlyList<IValidator<Entity1>> validators)
            {
                private readonly IEnumerable<IHandler<Entity1>> handlers1 = handlers1;
                private readonly IHandler<Entity2>[] handlers2 = handlers2;
                private readonly IReadOnlyList<IValidator<Entity1>> validators = validators;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NestedGenericInCollection_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IRequest<TResponse> { }
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public record GetUserRequest(int Id) : IRequest<User>;
            public record User(int Id, string Name);

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class GetUserHandler : IRequestHandler<GetUserRequest, User>
            {
                public User Handle(GetUserRequest request) => new(request.Id, "Test");
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IRequestHandler<,>)])]
            public sealed class GenericHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public TResponse Handle(TRequest request) => default!;
            }

            public record ListUsersRequest() : IRequest<List<User>>;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<IRequestHandler<ListUsersRequest, List<User>>> handlers)
            {
                private readonly IEnumerable<IRequestHandler<ListUsersRequest, List<User>>> handlers = handlers;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NullableIEnumerableDependency_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => System.Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<IHandler<TestEntity>>? handlers)
            {
                private readonly IEnumerable<IHandler<TestEntity>> handlers = handlers;
                public void Process(TestEntity entity)
                {
                    foreach (var handler in handlers) handler.Handle(entity);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
