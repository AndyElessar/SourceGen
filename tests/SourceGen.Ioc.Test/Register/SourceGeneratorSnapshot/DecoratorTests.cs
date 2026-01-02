namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

partial class RegisterSourceGeneratorSnapshotTests
{
    [Test]
    [Category(Constants.Decorator)]
    public async Task Decorator_SingleDecorator_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    public async Task Decorator_MultipleDecorators_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    public async Task Decorator_WithKeyedService_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    public async Task Decorator_ImplementationTypeOnly_NoDecoratorApplied()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            // When registering implementation type itself (no explicit ServiceTypes),
            // decorators should not be applied to the implementation type registration
            [IoCRegister(
                Lifetime = ServiceLifetime.Singleton,
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    public async Task Decorator_WithMultipleServiceTypes_GeneratesCorrectRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IMyOtherService { }

            [IoCRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(IMyService), typeof(IMyOtherService)],
                Decorators = [typeof(MyServiceDecorator)])]
            public class MyService : IMyService, IMyOtherService { }

            public class MyServiceDecorator(IMyService inner) : IMyService, IMyOtherService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    [Category(Constants.Defaults)]
    public async Task DefaultSettings_Decorator_AppliesDecoratorFromDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.MyServiceDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Should inherit decorator from default settings
            [IoCRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    [Category(Constants.Defaults)]
    public async Task DefaultSettings_Decorator_ExplicitDecoratorOverridesDefaultSettings()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.DefaultDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Explicit decorator should override default settings
            [IoCRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(ExplicitDecorator)])]
            public class MyService : IMyService { }

            public class DefaultDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }

            public class ExplicitDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    [Category(Constants.Defaults)]
    public async Task Decorator_GenericDecorator_GeneratesClosedGenericType()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IoCRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(HandlerDecorator<,>)])]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public sealed record TestRequest(int Key) : IRequest<TestRequest, string>;

            [IoCRegister]
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
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

            [IoCRegister(
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
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

            [IoCRegister(
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
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
            [IoCRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IRequestHandler<TestCommand, string>)],
                Decorators = [typeof(LoggingDecorator<,>), typeof(QueryDecorator<,>)])]
            internal sealed class TestCommandHandler : IRequestHandler<TestCommand, string>
            {
                public string Handle(TestCommand request) => $"Command: {request.Key}";
            }

            // Query handler - should get both LoggingDecorator and QueryDecorator (TestQuery implements IQuery)
            [IoCRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(IRequestHandler<TestQuery, int>)],
                Decorators = [typeof(LoggingDecorator<,>), typeof(QueryDecorator<,>)])]
            internal sealed class TestQueryHandler : IRequestHandler<TestQuery, int>
            {
                public int Handle(TestQuery request) => request.Filter.Length;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
    [Category(Constants.Defaults)]
    public async Task Decorator_WithTypeConstraints_DefaultSettingsAppliesOnlyMatchingDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.BaseDecorator<,>), typeof(TestNamespace.QueryOnlyDecorator<,>)])]

            namespace TestNamespace;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;
            public interface IQuery<TSelf, TResponse> : IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            // Base decorator without constraint
            internal sealed class BaseDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            // Query-only decorator with IQuery constraint
            internal sealed class QueryOnlyDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IQuery<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => inner.Handle(request);
            }

            public sealed record MyCommand(int Id) : IRequest<MyCommand, bool>;
            public sealed record MyQuery(string Search) : IQuery<MyQuery, string>;

            // Command handler - should only get BaseDecorator
            [IoCRegister]
            internal sealed class MyCommandHandler : IRequestHandler<MyCommand, bool>
            {
                public bool Handle(MyCommand request) => request.Id > 0;
            }

            // Query handler - should get both decorators
            [IoCRegister]
            internal sealed class MyQueryHandler : IRequestHandler<MyQuery, string>
            {
                public string Handle(MyQuery request) => $"Result: {request.Search}";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Decorator)]
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

            [IoCRegister(
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

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
