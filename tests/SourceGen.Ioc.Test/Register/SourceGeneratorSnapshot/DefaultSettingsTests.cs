namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

/// <summary>
/// Tests for IocRegisterDefaults functionality.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.Defaults)]
public class DefaultSettingsTests
{
    [Test]
    public async Task DefaultSettings_AppliesCorrectLifetime()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IBaseService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface ISpecificService : IBaseService { }

            [IocRegister(ServiceTypes = [typeof(ISpecificService)])]
            public class SpecificService : ISpecificService { }
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

            [assembly: IocRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Scoped, ServiceTypes = [typeof(TestNamespace.IBaseService)])]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface IOtherService { }
            public interface IGenericService<T> : IBaseService { }
            public interface IGenericService<T1, T2> : IOtherService { }

            // This should match IGenericService<> default settings (arity 1)
            [IocRegister]
            public class SingleArityService<T> : IGenericService<T> { }

            // This should NOT match IGenericService<> default settings (arity 2 != 1)
            // Should use default lifetime (Singleton) instead
            [IocRegister]
            public class DoubleArityService<T1, T2> : IGenericService<T1, T2> { }
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

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.MyServiceDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Should inherit decorator from default settings
            [IocRegister(ServiceTypes = [typeof(IMyService)])]
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

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.DefaultDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Explicit decorator should override default settings
            [IocRegister(ServiceTypes = [typeof(IMyService)], Decorators = [typeof(ExplicitDecorator)])]
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
    public async Task DefaultSettings_Decorator_WithTypeConstraints_AppliesOnlyMatchingDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
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
            [IocRegister]
            internal sealed class MyCommandHandler : IRequestHandler<MyCommand, bool>
            {
                public bool Handle(MyCommand request) => request.Id > 0;
            }

            // Query handler - should get both decorators
            [IocRegister]
            internal sealed class MyQueryHandler : IRequestHandler<MyQuery, string>
            {
                public string Handle(MyQuery request) => $"Result: {request.Search}";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
