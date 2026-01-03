namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

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
    public async Task ClosedGenericDependency_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public sealed class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public class TestEntity { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class TestHandler<T>(ILogger<TestHandler<T>> logger)
            {
                private readonly ILogger<TestHandler<T>> logger = logger;
                public void Handle() => logger.Log("Handling");
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(TestHandler<TestEntity> handler)
            {
                private readonly TestHandler<TestEntity> handler = handler;
                public void Query() => handler.Handle();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [assembly: IoCRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.HandlerDecorator1<,>), typeof(TestNamespace.HandlerDecorator2<,>)])]

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
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

            [IoCRegister]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(TestHandler<TestEntity> handler)
            {
                private readonly TestHandler<TestEntity> handler = handler;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal class TestHandler<T>
            {
                public void Handle() { }
            }

            // TestHandler<TestEntity> is explicitly registered via a concrete class
            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestHandler<TestEntity>)])]
            internal sealed class TestHandlerTestEntity : TestHandler<TestEntity> { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(TestHandler<TestEntity> handler)
            {
                private readonly TestHandler<TestEntity> handler = handler;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Repository<T>
            {
                public void Save(T entity) { }
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Service1(Repository<Entity1> repo)
            {
                private readonly Repository<Entity1> repo = repo;
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class Service2(Repository<Entity2> repo)
            {
                private readonly Repository<Entity2> repo = repo;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [assembly: IoCRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.HandlerDecorator1<,>), typeof(TestNamespace.HandlerDecorator2<,>)])]

            namespace TestNamespace;

            public interface ILogger<T>
            {
                void Log(string msg);
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
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

            [IoCRegister]
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
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel(IRequestHandler<GenericRequest<Entity>, List<Entity>> handler)
            {
                private readonly IRequestHandler<GenericRequest<Entity>, List<Entity>> handler = handler;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [assembly: IoCRegisterDefaults(
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

            [IoCRegister]
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceProviderInvocation_GetService_GeneratesFactoryRegistration()
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}