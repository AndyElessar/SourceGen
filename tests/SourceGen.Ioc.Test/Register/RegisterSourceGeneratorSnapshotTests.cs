using SourceGen.Ioc.Test.Helpers;

namespace SourceGen.Ioc.Test.Register;

/// <summary>
/// Snapshot tests for RegisterSourceGenerator.
/// </summary>
public class RegisterSourceGeneratorSnapshotTests
{
    [Test]
    public async Task SimpleService_Singleton_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task SimpleService_Scoped_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task SimpleService_Transient_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ITransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
            public class TransientService : ITransientService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task RegisterAllInterfaces_GeneratesMultipleRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllInterfaces = true)]
            public class MultiInterfaceService : IFirst, ISecond { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithStringKey_GeneratesKeyedRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "myKey")]
            public class KeyedService : IKeyedService { }

            public enum MyEnum
            {
                Key1 = 0,
                Key2 = 1
            }
            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = MyEnum.Key1)]
            public class KeyedService2 : IKeyedService { }

            public static class KeyHolder
            {
                public const int IntKey = 42;
            }
            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = KeyHolder.IntKey)]
            public class KeyedService3 : IKeyedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithCsharpKey_GeneratesKeyedRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyedService { }

            public class KeyHolder
            {
                public static readonly Guid Key = Guid.NewGuid();
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], KeyType = KeyType.Csharp, Key = "KeyHolder.Key")]
            public class KeyedService : IKeyedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], KeyType = KeyType.Csharp, Key = nameof(KeyHolder.Key))]
            public class KeyedService2 : IKeyedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task OpenGenericService_GeneratesTypeOfRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task OpenGenericService_DoNotGenerateNestedOpenGeneric()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            public interface IGeneric<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<IGeneric<T>> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task OpenGenericService_DoNotGenerateNestedOpenGeneric_WithRegisterAllInterfaces()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            public interface IGeneric<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, RegisterAllInterfaces = true)]
            public class Repository<T> : IRepository<IGeneric<T>> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterForAttribute_OnAssembly_GeneratesRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterFor(typeof(TestNamespace.ExternalService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IExternalService)])]

            namespace TestNamespace;

            public interface IExternalService { }
            public class ExternalService : IExternalService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task MultipleServices_GeneratesAllRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }
            public interface IServiceC { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IServiceC)])]
            public class ServiceC : IServiceC { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NoServices_GeneratesNoOutput()
    {
        const string source = """
            namespace TestNamespace;

            public interface IMyService { }
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task RegisterAllBaseClasses_GeneratesBaseClassRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class BaseService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllBaseClasses = true)]
            public class DerivedService : BaseService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedOpenGeneric_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ICache<>)], Key = "redis")]
            public class RedisCache<T> : ICache<T> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_AppliesCorrectLifetime()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IBaseService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface ISpecificService : IBaseService { }

            [IoCRegister(ServiceTypes = [typeof(ISpecificService)])]
            public class SpecificService : ISpecificService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NestedClass_GeneratesCorrectFullyQualifiedName()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                public interface INestedService { }

                [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(INestedService)])]
                public class NestedService : INestedService { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_GenericArity_ShouldNotMatchDifferentArity()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Scoped, ServiceTypes = [typeof(TestNamespace.IBaseService)])]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface IOtherService { }
            public interface IGenericService<T> : IBaseService { }
            public interface IGenericService<T1, T2> : IOtherService { }

            // This should match IGenericService<> default settings (arity 1)
            [IoCRegister]
            public class SingleArityService<T> : IGenericService<T> { }

            // This should NOT match IGenericService<> default settings (arity 2 != 1)
            // Should use default lifetime (Singleton) instead
            [IoCRegister]
            public class DoubleArityService<T1, T2> : IGenericService<T1, T2> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
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
