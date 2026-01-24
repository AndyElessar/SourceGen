namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for decorator container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.Decorator)]
public class DecoratorContainerTests
{
    [Test]
    public async Task Container_WithDecorators_GeneratesDecoratedResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { void Handle(); }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Decorators = [typeof(LoggingDecorator), typeof(CachingDecorator)])]
            public class Handler : IHandler
            {
                public void Handle() { }
            }

            public class LoggingDecorator(IHandler inner) : IHandler
            {
                public void Handle() => inner.Handle();
            }

            public class CachingDecorator(IHandler inner) : IHandler
            {
                public void Handle() => inner.Handle();
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithDecorators_HavingClosedGenericDependency_GeneratesDirectResolverCalls()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.HandlerDecorator<,>)])]

            namespace TestNamespace;

            public interface ILogger<T> { void Log(string msg); }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger<>)])]
            public class Logger<T> : ILogger<T>
            {
                public void Log(string msg) => System.Console.WriteLine(msg);
            }

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public record TestRequest(int Key) : IRequest<TestRequest, List<string>>;

            // Decorator with closed generic dependency on ILogger<HandlerDecorator<...>>
            public class HandlerDecorator<TRequest, TResponse>(
                IRequestHandler<TRequest, TResponse> inner,
                ILogger<HandlerDecorator<TRequest, TResponse>> logger
            ) : IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request)
                {
                    logger.Log("Handling");
                    return inner.Handle(request);
                }
            }

            [IocRegister]
            public class TestHandler : IRequestHandler<TestRequest, List<string>>
            {
                public List<string> Handle(TestRequest request) => [];
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        // The decorator's ILogger<HandlerDecorator<...>> dependency should be resolved
        // using direct method call instead of GetRequiredService
        await Verify(generatedSource);
    }

    /// <summary>
    /// Tests that decorator injection members (properties and methods with [IocInject]) are properly generated in Container mode.
    /// </summary>
    [Test]
    public async Task Container_WithDecoratorInjectionMembers_GeneratesPropertyAndMethodInjection()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache { void Set(string key, object value); }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ICache)])]
            public class MemoryCache : ICache
            {
                public void Set(string key, object value) { }
            }

            public interface ILogger { void Log(string msg); }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger
            {
                public void Log(string msg) { }
            }

            public interface IHandler { void Handle(); }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Decorators = [typeof(CachingDecorator)])]
            public class Handler : IHandler
            {
                public void Handle() { }
            }

            // Decorator with [IocInject] property and method
            public class CachingDecorator(IHandler inner) : IHandler
            {
                [IocInject]
                public ICache? Cache { get; set; }

                private ILogger? _logger;

                [IocInject]
                public void SetLogger(ILogger logger)
                {
                    _logger = logger;
                }

                public void Handle()
                {
                    _logger?.Log("Before");
                    Cache?.Set("key", "value");
                    inner.Handle();
                }
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
