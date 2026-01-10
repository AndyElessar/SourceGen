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

    [Test]
    public async Task DiscoverAttribute_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IoCRegister]
            public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>
            {
                public List<T> Handle(TestRequest<T> request) => [];
            }

            // Using DiscoverAttribute to discover closed generic type
            public class ViewModel
            {
                [Discover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
                public void DoAction()
                {
                    // Mediator.Send(new TestRequest<string>());
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_MultipleDiscoverAttributes_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>;

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IoCRegister]
            public class TestRequestHandler<T> : IRequestHandler<TestRequest<T>, List<T>>
            {
                public List<T> Handle(TestRequest<T> request) => [];
            }

            public class Entity1 { }
            public class Entity2 { }

            // Using multiple DiscoverAttribute to discover different closed generic types
            [Discover(typeof(IRequestHandler<TestRequest<Entity1>, List<Entity1>>))]
            [Discover(typeof(IRequestHandler<TestRequest<Entity2>, List<Entity2>>))]
            [Discover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_WithDecorators_GeneratesFactoryWithDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IoCRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.LoggingDecorator<,>)])]

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

            public sealed record TestRequest<T> : IRequest<TestRequest<T>, List<T>>;

            [IoCRegister]
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

            // Using DiscoverAttribute to discover closed generic type
            [Discover(typeof(IRequestHandler<TestRequest<string>, List<string>>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_OnMethod_GeneratesFactoryRegistration()
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

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public T? Get(int id) => default;
            }

            public class Entity { }

            // DiscoverAttribute on method
            public class Service
            {
                [Discover(typeof(IRepository<Entity>))]
                public void Process()
                {
                    // Some indirect usage
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_OnAssembly_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: Discover(typeof(TestNamespace.IRepository<TestNamespace.Entity1>))]
            [assembly: Discover(typeof(TestNamespace.IRepository<TestNamespace.Entity2>))]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? Get(int id);
            }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T>
            {
                public T? Get(int id) => default;
            }

            public class Entity1 { }
            public class Entity2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_IgnoresNonGenericTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class Service : IService { }

            // DiscoverAttribute with non-generic type should be ignored
            [Discover(typeof(IService))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_IgnoresOpenGenericTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }

            // DiscoverAttribute with open generic type should be ignored (if even possible)
            // Note: typeof(IRepository<>) in attribute is open generic
            [Discover(typeof(IRepository<>))]
            public class DiscoverMarker { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests DiscoverAttribute with nested open generic service interface (e.g., IRequestHandler&lt;GenericRequest&lt;T&gt;, List&lt;T&gt;&gt;).
    /// This is the scenario from IocSample where GenericRequestHandler2 implements IRequestHandler&lt;GenericRequest2&lt;T&gt;, List&lt;T&gt;&gt;.
    /// </summary>
    [Test]
    public async Task DiscoverAttribute_WithNestedOpenGenericServiceInterface_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

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
            [IoCRegister]
            internal sealed class GenericRequestHandler2<T> : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest2<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity2 { }

            // Using DiscoverAttribute to discover the closed generic service type
            // IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>
            internal sealed class ViewModel2
            {
                [Discover(typeof(IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>))]
                public void SendRequest2()
                {
                    // Some indirect usage via mediator pattern
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in [Inject] method parameters are discovered.
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

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRequestHandler<,>), ServiceLifetime.Singleton)]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record GenericRequest<T>(int Count) : IRequest<GenericRequest<T>, List<T>> where T : new();

            [IoCRegister]
            internal sealed class GenericRequestHandler<T> : IRequestHandler<GenericRequest<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity { }

            // This class uses [Inject] method with closed generic parameter
            // The generator should discover IRequestHandler<GenericRequest<Entity>, List<Entity>>
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel
            {
                private IRequestHandler<GenericRequest<Entity>, List<Entity>>? handler;

                [Inject]
                public void Initialize(IRequestHandler<GenericRequest<Entity>, List<Entity>> handler)
                {
                    this.handler = handler;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in [Inject] property are discovered.
    /// </summary>
    [Test]
    public async Task InjectProperty_ClosedGenericType_GeneratesFactoryRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IoCRegister]
            internal sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            internal class User { }

            // This class uses [Inject] property with closed generic type
            // The generator should discover IRepository<User>
            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            internal sealed class UserService
            {
                [Inject]
                public IRepository<User> Repository { get; init; } = null!;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that multiple closed generic dependencies in [Inject] method parameters are all discovered.
    /// </summary>
    [Test]
    public async Task InjectMethod_MultipleClosedGenericParameters_GeneratesAllFactoryRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IoCRegister]
            internal sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            internal class User { }
            internal class Order { }
            internal class Product { }

            // This class uses [Inject] method with multiple closed generic parameters
            // The generator should discover all three: IRepository<User>, IRepository<Order>, IRepository<Product>
            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            internal sealed class DashboardService
            {
                private IRepository<User>? userRepo;
                private IRepository<Order>? orderRepo;
                private IRepository<Product>? productRepo;

                [Inject]
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that closed generic dependencies in [Inject] method parameters with decorators work correctly.
    /// </summary>
    [Test]
    public async Task InjectMethod_ClosedGenericParameter_WithDecorators_GeneratesFactoryWithDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;
            using System.Linq;

            [assembly: IoCRegisterDefaults(
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

            [IoCRegister]
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

            // This class uses [Inject] method with closed generic parameter
            // The decorator should be applied to the closed generic registration
            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            internal sealed class ViewModel
            {
                private IRequestHandler<TestRequest<Entity>, List<Entity>>? handler;

                [Inject]
                public void Initialize(IRequestHandler<TestRequest<Entity>, List<Entity>> handler)
                {
                    this.handler = handler;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests combined scenario: constructor with closed generic + [Inject] method with different closed generic.
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

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IRepository<>), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IRepository<T>
            {
                T? GetById(int id);
            }

            [IoCRegister]
            internal sealed class Repository<T> : IRepository<T>
            {
                public T? GetById(int id) => default;
            }

            internal class User { }
            internal class Order { }

            // This class has closed generic in constructor and different closed generic in [Inject] method
            // Both IRepository<User> and IRepository<Order> should be discovered
            [IoCRegister(Lifetime = ServiceLifetime.Scoped)]
            internal sealed class OrderService(IRepository<User> userRepo)
            {
                private readonly IRepository<User> userRepo = userRepo;
                private IRepository<Order>? orderRepo;

                [Inject]
                public void SetOrderRepository(IRepository<Order> orderRepo)
                {
                    this.orderRepo = orderRepo;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
