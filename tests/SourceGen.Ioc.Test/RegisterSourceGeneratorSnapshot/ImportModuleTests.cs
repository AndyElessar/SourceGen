namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for IocImportModuleAttribute functionality.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ImportModule)]
public class ImportModuleTests
{
    [Test]
    public async Task ImportModule_ImportsDefaultSettingsFromReferencedAssembly()
    {
        // First, create a "shared" assembly with IocRegisterDefaults on an interface
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IocRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Tags = ["Mediator"],
                TagOnly = true
            )]
            public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }
            """;

        // Create the shared compilation
        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Now create the main assembly that imports from the shared module
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;

            namespace MainApp;

            [IocImportModule(typeof(IRequestHandler<,>))]
            public sealed class Module;

            public sealed record TestQuery(string Name) : IRequest<TestQuery, string>;

            [IocRegister]
            public sealed class TestQueryHandler : IRequestHandler<TestQuery, string>
            {
                public string Handle(TestQuery request) => $"Hello, {request.Name}!";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ImportModule_LocalDefaultSettingsTakePrecedence()
    {
        // Create a "shared" assembly with default settings
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IService { }

            [IocRegisterDefaults(typeof(IService), ServiceLifetime.Singleton)]
            public class SharedDefaults { }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Create the main assembly that imports and also has local default settings
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;

            namespace MainApp;

            // Import from shared module (Singleton)
            [IocImportModule(typeof(SharedDefaults))]
            public sealed class Module;

            // Local default settings (Scoped) - should take precedence
            [IocRegisterDefaults(typeof(IService), ServiceLifetime.Scoped)]
            public sealed class LocalDefaults;

            [IocRegister]
            public sealed class MyService : IService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ImportModule_MultipleModulesImported()
    {
        // Create first shared assembly
        const string shared1Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule1;

            public interface IService1 { }

            [IocRegisterDefaults(typeof(IService1), ServiceLifetime.Singleton)]
            public class Module1Defaults { }
            """;

        // Create second shared assembly
        const string shared2Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule2;

            public interface IService2 { }

            [IocRegisterDefaults(typeof(IService2), ServiceLifetime.Scoped)]
            public class Module2Defaults { }
            """;

        var shared1Compilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule1", shared1Source);
        var shared2Compilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule2", shared2Source);

        // Create the main assembly that imports from both modules
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule1;
            using SharedModule2;

            namespace MainApp;

            [IocImportModule(typeof(Module1Defaults))]
            [IocImportModule(typeof(Module2Defaults))]
            public sealed class Module;

            [IocRegister]
            public sealed class Service1Impl : IService1 { }

            [IocRegister]
            public sealed class Service2Impl : IService2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            mainSource,
            "MainApp",
            [shared1Compilation.ToMetadataReference(), shared2Compilation.ToMetadataReference()]);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ImportModule_WithDecorators_AppliesImportedDecorators()
    {
        // Create shared assembly with decorators
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IHandler<T> { void Handle(T request); }

            public class LoggingDecorator<T>(IHandler<T> inner) : IHandler<T>
            {
                public void Handle(T request) => inner.Handle(request);
            }

            [IocRegisterDefaults(
                typeof(IHandler<>),
                ServiceLifetime.Transient,
                Decorators = [typeof(LoggingDecorator<>)]
            )]
            public interface IHandlerMarker { }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Create the main assembly
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;

            namespace MainApp;

            [IocImportModule(typeof(IHandlerMarker))]
            public sealed class Module;

            public record MyCommand(string Data);

            [IocRegister]
            public sealed class MyCommandHandler : IHandler<MyCommand>
            {
                public void Handle(MyCommand request) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ImportModule_WithOpenGenericRegistration_GeneratesClosedTypes()
    {
        // Create a shared module with open generic registration using IocRegisterFor
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IHandler<TRequest, TResponse>
            {
                TResponse Handle(TRequest request);
            }

            public class GenericHandler<TRequest, TResponse> : IHandler<TRequest, TResponse>
            {
                public TResponse Handle(TRequest request) => default!;
            }

            [IocRegisterFor(typeof(GenericHandler<,>), ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler<,>)])]
            [IocContainer(ExplicitOnly = true)]
            public sealed partial class SharedOpenGenericModule;
            """;

        // Create the shared compilation and run generator to produce SharedOpenGenericModule.Container
        var sharedResult = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(sharedSource, "SharedModule");
        var sharedCompilation = sharedResult.OutputCompilation;

        // Create the main assembly that imports the module and uses closed types
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;

            namespace MainApp;

            public record Request1(string Value);
            public record Response1(string Result);

            // Service that depends on closed generic type - should trigger discovery
            [IocRegister(ServiceLifetime.Transient)]
            public sealed class Consumer(IHandler<Request1, Response1> handler)
            {
                public IHandler<Request1, Response1> Handler => handler;
            }

            [IocImportModule<SharedOpenGenericModule>]
            [IocContainer]
            public sealed partial class MainContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "MainContainer.Container");

        await Verify(generatedSource);
    }
}
