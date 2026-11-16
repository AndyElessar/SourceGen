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
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Container_WithExplicitCollectionRegistration_ExplicitRegistrationWinsOverGeneratedCollection(bool keyed, bool useSwitchStatement)
    {
        // IEnumerable<T> and ICollection<T> registered explicitly next to two registrations of T (under the same key): like
        // MS.DI, which takes the exact registration of IEnumerable<T> first, the explicit registrations win
        var key = keyed ? ", Key = \"a\"" : "";
        var source = $$"""
            using System.Collections;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)]{{key}})]
            public class FirstHandler : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)]{{key}})]
            public class SecondHandler : IHandler { }

            public sealed class HandlerList : List<IHandler>
            {
                public static readonly HandlerList Default = new();
            }

            [IocRegisterFor<HandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IHandler>), typeof(ICollection<IHandler>)], Instance = nameof(HandlerList.Default){{key}})]
            [IocContainer(UseSwitchStatement = {{(useSwitchStatement ? "true" : "false")}})]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource).UseParameters(keyed, useSwitchStatement);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Container_WithExplicitEnumerableUnderAnyKey_GeneratesNoAnyKeyCollection(bool useSwitchStatement)
    {
        // IEnumerable<T> registered explicitly under KeyedService.AnyKey wins over the generated AnyKey collection of T
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "a")]
            public class FirstHandler : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "a")]
            public class SecondHandler : IHandler { }

            public sealed class HandlerList : List<IHandler>
            {
                public static readonly HandlerList Default = new();
            }

            [IocRegisterFor<HandlerList>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IEnumerable<IHandler>)], Instance = nameof(HandlerList.Default), Key = "KeyedService.AnyKey", KeyType = KeyType.Csharp)]
            [IocContainer(UseSwitchStatement = {{(useSwitchStatement ? "true" : "false")}})]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).DoesNotContain("GetAllIHandlerArray_AnyKey");
        await Assert.That(generatedSource).DoesNotContain("__EnumerableResolver<global::TestNamespace.IHandler>(global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey");
        await Assert.That(generatedSource).Contains("HandlerList.Default");
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Arguments(false, false)]
    [Arguments(true, false)]
    public async Task Container_WithTwoRegistrationsUnderOneKey_GeneratesKeyedCollectionResolverOnlyForThatKey(bool useSwitchStatement, bool integrateServiceProvider)
    {
        var source = $$"""
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "a")]
            public class FirstHandler : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "b")]
            public class SingleHandler : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "a")]
            public class SecondHandler : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(
                [IocInject(Key = "a")] IEnumerable<IHandler> many,
                [IocInject(Key = "a")] IReadOnlyList<IHandler> manyList,
                [IocInject(Key = "b")] IEnumerable<IHandler> single) { }

            [IocContainer(UseSwitchStatement = {{(useSwitchStatement ? "true" : "false")}}, IntegrateServiceProvider = {{(integrateServiceProvider ? "true" : "false")}})]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource).UseParameters(useSwitchStatement, integrateServiceProvider);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Container_WithRegistrationsUnderAnyKey_ExcludesThemFromKeyedCollections(bool useSwitchStatement)
    {
        // Like MS.DI, registrations made with KeyedService.AnyKey as their key are not part of the AnyKey collection,
        // and get no keyed collection resolver of their own
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "a")]
            public class KeyedHandler : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey", KeyType = KeyType.Csharp)]
            public class AnyKeyHandler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "global::Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey", KeyType = KeyType.Csharp)]
            public class AnyKeyHandler2 : IHandler { }

            [IocContainer(UseSwitchStatement = {{(useSwitchStatement ? "true" : "false")}})]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource).UseParameters(useSwitchStatement);
    }

    [Test]
    public async Task Container_WithKeyedCollectionsOfDifferentKeyKinds_NamesResolversAfterKeys()
    {
        // Enum and C# expression keys, and string keys whose identifiers collide ("x-y" and "x.y")
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public enum Kind { First }

            public static class Keys
            {
                public const string Name = "name";
            }

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = Kind.First, KeyType = KeyType.Value)]
            public class EnumHandler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = Kind.First, KeyType = KeyType.Value)]
            public class EnumHandler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "global::TestNamespace.Keys.Name", KeyType = KeyType.Csharp)]
            public class CsharpHandler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "global::TestNamespace.Keys.Name", KeyType = KeyType.Csharp)]
            public class CsharpHandler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "x-y")]
            public class DashHandler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "x-y")]
            public class DashHandler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "x.y")]
            public class DotHandler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "x.y")]
            public class DotHandler2 : IHandler { }

            [IocContainer]
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
