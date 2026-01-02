namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

/// <summary>
/// Tests for ImportModuleAttribute functionality.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ImportModule)]
public class ImportModuleTests
{
    [Test]
    public async Task ImportModule_ImportsDefaultSettingsFromReferencedAssembly()
    {
        // First, create a "shared" assembly with IoCRegisterDefaults on an interface
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            public interface IRequest<TSelf, TResponse> where TSelf : IRequest<TSelf, TResponse>;

            [IoCRegisterDefaults(
                typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Tags = ["Mediator"],
                ExcludeFromDefault = true
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

            [ImportModule(typeof(IRequestHandler<,>))]
            public sealed class Module;

            public sealed record TestQuery(string Name) : IRequest<TestQuery, string>;

            [IoCRegister]
            public sealed class TestQueryHandler : IRequestHandler<TestQuery, string>
            {
                public string Handle(TestQuery request) => $"Hello, {request.Name}!";
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
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

            [IoCRegisterDefaults(typeof(IService), ServiceLifetime.Singleton)]
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
            [ImportModule(typeof(SharedDefaults))]
            public sealed class Module;

            // Local default settings (Scoped) - should take precedence
            [IoCRegisterDefaults(typeof(IService), ServiceLifetime.Scoped)]
            public sealed class LocalDefaults;

            [IoCRegister]
            public sealed class MyService : IService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
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

            [IoCRegisterDefaults(typeof(IService1), ServiceLifetime.Singleton)]
            public class Module1Defaults { }
            """;

        // Create second shared assembly
        const string shared2Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule2;

            public interface IService2 { }

            [IoCRegisterDefaults(typeof(IService2), ServiceLifetime.Scoped)]
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

            [ImportModule(typeof(Module1Defaults))]
            [ImportModule(typeof(Module2Defaults))]
            public sealed class Module;

            [IoCRegister]
            public sealed class Service1Impl : IService1 { }

            [IoCRegister]
            public sealed class Service2Impl : IService2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            mainSource,
            "MainApp",
            [shared1Compilation.ToMetadataReference(), shared2Compilation.ToMetadataReference()]);
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

            [IoCRegisterDefaults(
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

            [ImportModule(typeof(IHandlerMarker))]
            public sealed class Module;

            public record MyCommand(string Data);

            [IoCRegister]
            public sealed class MyCommandHandler : IHandler<MyCommand>
            {
                public void Handle(MyCommand request) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
