namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for Decorator functionality.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.Decorator)]
public class DecoratorTests
{
    [Test]
    public async Task Decorator_SingleDecorator_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_MultipleDecorators_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Decorators = [typeof(MyServiceDecorator), typeof(MyServiceDecorator2)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }

            public class MyServiceDecorator2(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithKeyedService_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IMyService)],
                Key = "myKey",
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithFactory_WrapsFactoryCreatedImplementationInstance()
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
                Factory = nameof(MyServiceFactory.Create),
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithGenericFactory_WrapsServiceTypeFactoryInstance()
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
                Decorators = [typeof(LoggingHandler<>)])]
            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }

            public class LoggingHandler<T>(IRequestHandler<T> inner) : IRequestHandler<T> { }

            [IocRegister]
            public class Handler<T> : IRequestHandler<Task<T>> { }

            public class Entity { }

            [IocRegister]
            public class Consumer(IRequestHandler<Task<Entity>> handler, Lazy<IRequestHandler<Task<Entity>>> lazy, Handler<Entity> implementation);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithInstance_NoDecoratorApplied()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Instance = nameof(Default),
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new();
            }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task WrapperDependencies_OfDecoratorsAndFactories_GenerateWrapperRegistrations()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)])]
            public class Foo : IFoo { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFoo)], Key = "k")]
            public class KeyedFoo : IFoo { }

            public interface IBar { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IBar)], Decorators = [typeof(BarDecorator)])]
            public class Bar : IBar { }

            public class BarDecorator(IBar inner, Lazy<IFoo> lazy, IReadOnlyDictionary<string, IFoo> keyed) : IBar { }

            public interface IBaz { }

            public class Baz : IBaz { }

            [IocRegisterFor(typeof(Baz), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IBaz)], Factory = nameof(BazFactory.Create))]
            public static class BazFactory
            {
                public static IBaz Create(Func<IFoo> foo) => new Baz();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithInstance_LazyAndFuncResolveUndecoratedInstance()
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
                Instance = nameof(Default),
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService
            {
                public static readonly MyService Default = new();
            }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }

            [IocRegister]
            public class Consumer(Lazy<IMyService> lazy, Func<IMyService> factory);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_ImplementationTypeOnly_NoDecoratorApplied()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            // When registering implementation type itself (no explicit ServiceTypes),
            // decorators should not be applied to the implementation type registration
            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithMultipleServiceTypes_GeneratesCorrectRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IMyOtherService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService), typeof(IMyOtherService)],
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService, IMyOtherService { }

            public class MyServiceDecorator(IMyService inner) : IMyService, IMyOtherService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_GenericDecorator_GeneratesClosedGenericType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IocRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(HandlerDecorator<,>)])]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record TestRequest(int Key) : IRequest<TestRequest, string>;

            [IocRegister]
            internal sealed class TestHandler : IRequestHandler<TestRequest, string>
            {
                public string Handle(TestRequest request) => $"Result: {request.Key}";
            }

            internal sealed class HandlerDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_MultipleGenericDecorators_GeneratesCorrectChain()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record MyRequest(int Value);

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IHandler<MyRequest, string>)],
                Decorators = [typeof(LoggingDecorator<,>), typeof(ValidationDecorator<,>)])]
            internal sealed class MyHandler : IHandler<MyRequest, string>
            {
                public string Handle(MyRequest request) => $"Result: {request.Value}";
            }

            internal sealed class LoggingDecorator<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner
            ) : IHandler<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            internal sealed class ValidationDecorator<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner
            ) : IHandler<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_GenericDecoratorWithComplexTypeArgs_GeneratesCorrectType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record ListRequest(int Count);

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IHandler<ListRequest, List<string>>)],
                Decorators = [typeof(CachingDecorator<,>)])]
            internal sealed class ListHandler : IHandler<ListRequest, List<string>>
            {
                public List<string> Handle(ListRequest request) => new();
            }

            internal sealed class CachingDecorator<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner
            ) : IHandler<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_WithTypeConstraints_OnlyAppliesMatchingDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IQuery<TSelf, TResponse> : IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            // A decorator without constraints - should apply to all handlers
            internal sealed class LoggingDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            // A decorator with IQuery constraint - should only apply to query handlers
            internal sealed class QueryDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IQuery<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            // Regular request (not IQuery)
            public sealed record TestCommand(int Key) : IRequest<TestCommand, string>;

            // Query request (implements IQuery)
            public sealed record TestQuery(string Filter) : IQuery<TestQuery, int>;

            // Command handler - should only get LoggingDecorator (TestCommand doesn't implement IQuery)
            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IRequestHandler<TestCommand, string>)],
                Decorators = [typeof(LoggingDecorator<,>), typeof(QueryDecorator<,>)])]
            internal sealed class TestCommandHandler : IRequestHandler<TestCommand, string>
            {
                public string Handle(TestCommand request) => $"Command: {request.Key}";
            }

            // Query handler - should get both LoggingDecorator and QueryDecorator (TestQuery implements IQuery)
            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IRequestHandler<TestQuery, int>)],
                Decorators = [typeof(LoggingDecorator<,>), typeof(QueryDecorator<,>)])]
            internal sealed class TestQueryHandler : IRequestHandler<TestQuery, int>
            {
                public int Handle(TestQuery request) => request.Filter.Length;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_OpenGenericWithExtraParameters_SubstitutesTypeParameters()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public interface ILogger<T>
            {
                void Log(string message);
            }

            public sealed record MyRequest(int Value);

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IHandler<MyRequest, List<string>>)],
                Decorators = [typeof(LoggingDecorator<,>)])]
            internal sealed class MyHandler : IHandler<MyRequest, List<string>>
            {
                public List<string> Handle(MyRequest request) => new();
            }

            internal sealed class LoggingDecorator<TRequest, TResponse>(
                IHandler<TRequest, TResponse> inner,
                ILogger<LoggingDecorator<TRequest, TResponse>> logger
            ) : IHandler<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request)
                {
                    logger.Log("Handling request");
                    return inner.Handle(request);
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_TypeParametersNotInServiceOrder_ClosesFromImplementedServiceType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<TRequest, TResponse>;

            public class Req { }

            public class Res { }

            internal sealed class SwappedDecorator<TResponse, TRequest>(IHandler<TRequest, TResponse> inner) : IHandler<TRequest, TResponse>;

            internal sealed class TextDecorator<TRequest>(IHandler<TRequest, string> inner) : IHandler<TRequest, string>;

            // SwappedDecorator closes as SwappedDecorator<Res, Req>; TextDecorator only decorates IHandler<Req, string>.
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<Req, Res>)], Decorators = [typeof(SwappedDecorator<,>), typeof(TextDecorator<>)])]
            internal sealed class ResponseHandler : IHandler<Req, Res>;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<Req, string>)], Decorators = [typeof(SwappedDecorator<,>), typeof(TextDecorator<>)])]
            internal sealed class TextHandler : IHandler<Req, string>;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_NonGenericServiceTypes_AppliesOnlyToImplementedServiceTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo;

            public interface IBar;

            internal sealed class FooDecorator(IFoo inner) : IFoo;

            // FooDecorator only decorates the IFoo registration, not IBar.
            [IocRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllInterfaces = true, Decorators = [typeof(FooDecorator)])]
            internal sealed class Service : IFoo, IBar;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    private const string DecoratedParameterSource = """
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface IHandler;
        public interface IOther;
        public interface ILogger;

        [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
        public sealed class Logger : ILogger;

        [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IOther)])]
        public sealed class OtherService : IOther;

        [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "other")]
        public sealed class OtherHandler : IHandler;

        [IocRegister(
            Lifetime = ServiceLifetime.Singleton,
            ServiceTypes = [typeof(IHandler)],
            Decorators = [typeof(LoggingDecorator), typeof(PairDecorator), typeof(OtherDecorator), typeof(NoInnerDecorator)])]
        public sealed class Handler : IHandler, IOther;

        // The decorated instance is not the first parameter
        public sealed class LoggingDecorator(ILogger logger, IHandler inner) : IHandler;

        // Only the first IHandler parameter receives the decorated instance; the second is resolved
        public sealed class PairDecorator(IHandler inner, [FromKeyedServices("other")] IHandler other) : IHandler;

        // The implementation implements IOther, but only the service type receives the decorated instance
        public sealed class OtherDecorator(IHandler inner, IOther other) : IHandler;

        // No parameter can receive the decorated instance, so the decorator is not applied
        public sealed class NoInnerDecorator(ILogger logger) : IHandler;
        """;
    [Test]
    public async Task Decorator_DecoratedParameter_IsFirstParameterOfServiceType()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(DecoratedParameterSource);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain("NoInnerDecorator");
        await Assert.That(generatedSource).Contains("sp.GetRequiredKeyedService<global::TestNamespace.IHandler>(\"other\")");
        await Assert.That(generatedSource).Contains("sp.GetRequiredService<global::TestNamespace.IOther>()");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Decorator_ParameterlessOpenGenericDecorator_IsNotApplied()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;

            [IocRegister(ServiceTypes = [typeof(IHandler<int>)], Decorators = [typeof(NoInnerDecorator<>)])]
            public sealed class IntHandler : IHandler<int>;

            public sealed class NoInnerDecorator<T> : IHandler<T>;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).DoesNotContain("NoInnerDecorator");
        await Verify(generatedSource);
    }
}