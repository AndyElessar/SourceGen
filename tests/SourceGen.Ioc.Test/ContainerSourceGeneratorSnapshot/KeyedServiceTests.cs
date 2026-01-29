namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for keyed service container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.KeyedService)]
public class KeyedServiceTests
{
    [Test]
    public async Task Container_WithKeyedServices_GeneratesKeyedResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "key1")]
            public class KeyedService1 : IKeyedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "key2")]
            public class KeyedService2 : IKeyedService { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithKeyedServices_UseSwitchStatement()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "key1")]
            public class KeyedService1 : IKeyedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "key2")]
            public class KeyedService2 : IKeyedService { }

            [IocContainer(UseSwitchStatement = true)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithServiceKeyAttribute_InjectsRegistrationKey()
    {
        // When a constructor parameter has [ServiceKey] attribute,
        // the container should inject the registered key for that service
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            public interface IKeyedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "myKey")]
            public class KeyedService([ServiceKey] object key, IDependency dependency) : IKeyedService
            {
                public object Key { get; } = key;
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
    public async Task Container_WithServiceKeyAttribute_NullableKey_InjectsNullForNonKeyedService()
    {
        // When a constructor parameter has [ServiceKey] attribute but the service is not keyed,
        // the container should inject null
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([ServiceKey] object? key) : IMyService
            {
                public object? Key { get; } = key;
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
    public async Task Container_WithServiceKeyAttribute_StringKey_InjectsTypedKey()
    {
        // When a constructor parameter has [ServiceKey] attribute with string type,
        // the container should inject the string key
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "handler-1")]
            public class Handler1([ServiceKey] string key) : IHandler
            {
                public string Key { get; } = key;
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "handler-2")]
            public class Handler2([ServiceKey] string key) : IHandler
            {
                public string Key { get; } = key;
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
    public async Task Container_WithServiceKeyAttribute_MethodInjection_InjectsKey()
    {
        // When a method injection parameter has [ServiceKey] attribute,
        // the container should inject the registered key
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum ServiceKey
            {
                Primary,
                Secondary
            }

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)], Key = ServiceKey.Primary)]
            public class PrimaryService : IService
            {
                public ServiceKey? Key { get; private set; }

                [IocInject]
                public void Initialize([ServiceKey] ServiceKey key)
                {
                    Key = key;
                }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)], Key = ServiceKey.Secondary)]
            public class SecondaryService : IService
            {
                public ServiceKey? Key { get; private set; }

                [IocInject]
                public void Initialize([ServiceKey] ServiceKey key)
                {
                    Key = key;
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

    [Test]
    public async Task Container_WithServiceKeyAttribute_MethodInjection_WithOtherParameters_InjectsCorrectly()
    {
        // When a method injection has both [ServiceKey] and regular service parameters,
        // the container should inject the key and resolve services correctly
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "myHandler")]
            public class MyHandler : IHandler
            {
                public string? Key { get; private set; }
                public ILogger? Logger { get; private set; }

                [IocInject]
                public void Initialize(ILogger logger, [ServiceKey] string key)
                {
                    Logger = logger;
                    Key = key;
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

    [Test]
    public async Task Container_WithServiceKeyAttribute_Factory_InjectsKey()
    {
        // When a factory method parameter has [ServiceKey] attribute,
        // the container should inject the registered key
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            public class KeyedService : IService
            {
                public string Key { get; }

                public KeyedService(string key) => Key = key;
            }

            public static class ServiceFactory
            {
                public static IService Create([ServiceKey] string key) => new KeyedService(key);
            }

            [IocRegisterFor(typeof(KeyedService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)], Key = "factoryKey", Factory = nameof(ServiceFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithServiceKeyAttribute_Factory_WithDependencies_InjectsCorrectly()
    {
        // When a factory method has both [ServiceKey] and regular service parameters,
        // the container should inject the key and resolve services correctly
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            public interface IService { }

            public class KeyedService : IService
            {
                public string Key { get; }
                public ILogger Logger { get; }

                public KeyedService(string key, ILogger logger)
                {
                    Key = key;
                    Logger = logger;
                }
            }

            public static class ServiceFactory
            {
                public static IService Create([ServiceKey] string key, ILogger logger) => new KeyedService(key, logger);
            }

            [IocRegisterFor(typeof(KeyedService), Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)], Key = "myKey", Factory = nameof(ServiceFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
