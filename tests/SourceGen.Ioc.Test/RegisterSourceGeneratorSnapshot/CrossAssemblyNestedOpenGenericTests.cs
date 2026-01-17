namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for cross-assembly scenarios with nested open generic interfaces.
/// These tests ensure that GetRequiredService and IocDiscoverAttribute with interface types
/// correctly generate closed generic registrations when the interface is defined in a different assembly.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ImportModule)]
public class CrossAssemblyNestedOpenGenericTests
{
    [Test]
    public async Task GetRequiredService_WithNestedOpenGenericInterface_CrossAssembly_GeneratesClosedGenericRegistration()
    {
        // Simulate a shared assembly with IocRegisterDefaults on an interface
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IocRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                TagOnly = true,
                Tags = ["Mediator"]
            )]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Main assembly with GenericRequest2 and GetRequiredService call using interface type
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace MainApp;

            [IocImportModule(typeof(IRequestHandler<,>))]
            public sealed class Module;

            // Nested open generic request type
            public sealed record GenericRequest2<T>(int Count) : IRequest<GenericRequest2<T>, List<T>> where T : new();

            // Open generic handler implementing nested open generic service interface
            [IocRegister]
            internal sealed class GenericRequestHandler2<T> : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest2<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity { }

            // This uses GetRequiredService with the service INTERFACE type
            // Should generate closed generic registration for GenericRequestHandler2<Entity>
            [IocRegister]
            internal sealed class CustomMessenger(IServiceProvider serviceProvider)
            {
                private readonly IServiceProvider serviceProvider = serviceProvider;

                public List<Entity> Send()
                {
                    // This should trigger generation of GenericRequestHandler2<Entity> and
                    // IRequestHandler<GenericRequest2<Entity>, List<Entity>>
                    return serviceProvider.GetRequiredService<IRequestHandler<GenericRequest2<Entity>, List<Entity>>>()
                        .Handle(new GenericRequest2<Entity>(5));
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            mainSource,
            "MainApp",
            [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_WithNestedOpenGenericInterface_CrossAssembly_GeneratesClosedGenericRegistration()
    {
        // Simulate a shared assembly with IocRegisterDefaults on an interface
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IocRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                TagOnly = true,
                Tags = ["Mediator"]
            )]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Main assembly with IocDiscoverAttribute on interface type
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace MainApp;

            [IocImportModule(typeof(IRequestHandler<,>))]
            public sealed class Module;

            // Nested open generic request type
            public sealed record GenericRequest2<T>(int Count) : IRequest<GenericRequest2<T>, List<T>> where T : new();

            // Open generic handler implementing nested open generic service interface
            [IocRegister]
            internal sealed class GenericRequestHandler2<T> : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest2<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity2 { }

            // Using IocDiscoverAttribute with the service INTERFACE type
            [IocRegister]
            internal sealed class ViewModel2
            {
                // This should trigger generation of GenericRequestHandler2<Entity2> and
                // IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>
                [IocDiscover(typeof(IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>))]
                public void SendRequest2()
                {
                    // Some indirect usage via mediator pattern
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            mainSource,
            "MainApp",
            [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task GetRequiredService_WithConcreteType_CrossAssembly_GeneratesClosedGenericRegistration()
    {
        // This test verifies that using concrete type (instead of interface) also works
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IocRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                TagOnly = true,
                Tags = ["Mediator"]
            )]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace MainApp;

            [IocImportModule(typeof(IRequestHandler<,>))]
            public sealed class Module;

            public sealed record GenericRequest2<T>(int Count) : IRequest<GenericRequest2<T>, List<T>> where T : new();

            [IocRegister]
            internal sealed class GenericRequestHandler2<T> : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest2<T> request)
                {
                    return [.. Enumerable.Range(0, request.Count).Select(_ => new T())];
                }
            }

            internal class Entity { }

            // Using concrete type - this should also work
            [IocRegister]
            internal sealed class CustomMessenger(IServiceProvider serviceProvider)
            {
                private readonly IServiceProvider serviceProvider = serviceProvider;

                public List<Entity> Send()
                {
                    // Using concrete type instead of interface
                    return serviceProvider.GetRequiredService<GenericRequestHandler2<Entity>>()
                        .Handle(new GenericRequest2<Entity>(5));
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            mainSource,
            "MainApp",
            [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task MultipleImplementations_SameInterface_CrossAssembly_GeneratesCorrectRegistrations()
    {
        // This test verifies that multiple implementations of the same interface
        // are correctly handled when using interface-based lookups
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IocRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                TagOnly = true,
                Tags = ["Mediator"]
            )]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;
            using System;
            using System.Collections.Generic;
            using System.Linq;

            namespace MainApp;

            [IocImportModule(typeof(IRequestHandler<,>))]
            public sealed class Module;

            // First request/handler pair
            public sealed record GenericRequest1<T>(int Count) : IRequest<GenericRequest1<T>, List<T>> where T : new();

            [IocRegister]
            internal sealed class GenericRequestHandler1<T> : IRequestHandler<GenericRequest1<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest1<T> request) => [];
            }

            // Second request/handler pair
            public sealed record GenericRequest2<T>(int Count) : IRequest<GenericRequest2<T>, List<T>> where T : new();

            [IocRegister]
            internal sealed class GenericRequestHandler2<T> : IRequestHandler<GenericRequest2<T>, List<T>> where T : new()
            {
                public List<T> Handle(GenericRequest2<T> request) => [];
            }

            internal class Entity1 { }
            internal class Entity2 { }

            // Service that uses both handlers via interface types
            [IocRegister]
            internal sealed class Mediator(IServiceProvider sp)
            {
                private readonly IServiceProvider sp = sp;

                public List<Entity1> Send1() =>
                    sp.GetRequiredService<IRequestHandler<GenericRequest1<Entity1>, List<Entity1>>>()
                        .Handle(new GenericRequest1<Entity1>(5));

                public List<Entity2> Send2() =>
                    sp.GetRequiredService<IRequestHandler<GenericRequest2<Entity2>, List<Entity2>>>()
                        .Handle(new GenericRequest2<Entity2>(5));
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            mainSource,
            "MainApp",
            [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
