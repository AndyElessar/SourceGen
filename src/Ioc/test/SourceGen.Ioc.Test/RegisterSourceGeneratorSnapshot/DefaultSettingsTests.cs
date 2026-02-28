namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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
            // Should use default lifetime (Transient) instead
            [IocRegister]
            public class DoubleArityService<T1, T2> : IGenericService<T1, T2> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_Factory_AppliesFactoryFromDefaultSettings()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyHandler),
                ServiceLifetime.Scoped,
                Factory = nameof(TestNamespace.HandlerFactory.Create))]

            namespace TestNamespace;

            public interface IMyHandler { void Handle(); }

            public static class HandlerFactory
            {
                public static IMyHandler Create(IServiceProvider sp) => sp.GetRequiredService<MyHandlerImpl>();
            }

            // Should use Factory from DefaultSettings
            [IocRegister]
            public class MyHandlerImpl : IMyHandler
            {
                public void Handle() { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_Factory_ExplicitFactoryOverridesDefaultSettings()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyHandler),
                ServiceLifetime.Scoped,
                Factory = nameof(TestNamespace.DefaultFactory.Create))]

            namespace TestNamespace;

            public interface IMyHandler { void Handle(); }

            public static class DefaultFactory
            {
                public static IMyHandler Create(IServiceProvider sp) => sp.GetRequiredService<DefaultHandler>();
            }

            public static class SpecialFactory
            {
                public static IMyHandler Create(IServiceProvider sp) => new SpecialHandler();
            }

            // Should use Factory from DefaultSettings
            [IocRegister]
            public class DefaultHandler : IMyHandler
            {
                public void Handle() { }
            }

            // Should use explicit Factory, overriding DefaultSettings
            [IocRegister(Factory = nameof(SpecialFactory.Create))]
            public class SpecialHandler : IMyHandler
            {
                public void Handle() { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_Factory_GenericInterface_AppliesFactory()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IRepository<>),
                ServiceLifetime.Scoped,
                Factory = nameof(TestNamespace.RepositoryFactory.Create))]

            namespace TestNamespace;

            public interface IRepository<T> { T Get(int id); }

            public static class RepositoryFactory
            {
                public static object Create(IServiceProvider sp) => throw new NotImplementedException();
            }

            // Open generic registration - Factory from DefaultSettings will be applied
            [IocRegister]
            public class Repository<T> : IRepository<T>
            {
                public T Get(int id) => default!;
            }

            // Entity type for closed generic discovery
            public class Customer { public int Id { get; set; } }

            // This service uses IRepository<Customer>, triggering closed generic registration
            [IocRegister]
            public class CustomerService
            {
                private readonly IRepository<Customer> _repository;

                public CustomerService(IRepository<Customer> repository)
                {
                    _repository = repository;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_ImplementationTypes_RegistersImplementationTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Scoped,
                ImplementationTypes = [typeof(TestNamespace.MyService), typeof(TestNamespace.AnotherService)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Registered directly via ImplementationTypes
            public class MyService : IMyService { }

            // Also registered directly via ImplementationTypes
            public class AnotherService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_ImplementationTypes_WithServiceTypes_RegistersAllServiceTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IBaseService),
                ServiceLifetime.Singleton,
                ServiceTypes = [typeof(TestNamespace.ISecondaryService)],
                ImplementationTypes = [typeof(TestNamespace.MyService)])]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface ISecondaryService { }

            // Registered via ImplementationTypes, should also register ISecondaryService from ServiceTypes
            public class MyService : IBaseService, ISecondaryService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_ImplementationTypes_WithDecorators_AppliesDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Scoped,
                Decorators = [typeof(TestNamespace.MyServiceDecorator)],
                ImplementationTypes = [typeof(TestNamespace.MyService)])]

            namespace TestNamespace;

            public interface IMyService { void DoWork(); }

            // Registered via ImplementationTypes with decorator
            public class MyService : IMyService
            {
                public void DoWork() { }
            }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
                public void DoWork() => _inner.DoWork();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_ImplementationTypes_Generic_RegistersImplementationTypes()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(
                ServiceLifetime.Transient,
                ImplementationTypes = [typeof(TestNamespace.MyService), typeof(TestNamespace.AnotherService)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Registered directly via ImplementationTypes in generic attribute
            public class MyService : IMyService { }

            // Also registered directly via ImplementationTypes in generic attribute
            public class AnotherService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_ImplementationTypes_WithTags_RegistersWithTags()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Scoped,
                Tags = ["Production"],
                ImplementationTypes = [typeof(TestNamespace.ProductionService)])]

            namespace TestNamespace;

            public interface IMyService { }

            // Registered via ImplementationTypes with Production tag
            public class ProductionService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DefaultSettings_ImplementationTypes_WithConstructorDependencies_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults(
                typeof(TestNamespace.IMyService),
                ServiceLifetime.Scoped,
                ImplementationTypes = [typeof(TestNamespace.MyService)])]

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister]
            public class Dependency : IDependency { }

            // Registered via ImplementationTypes with constructor dependency
            public class MyService(IDependency dependency) : IMyService
            {
                private readonly IDependency _dependency = dependency;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
