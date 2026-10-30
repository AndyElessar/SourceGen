namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Tests for wrapper type dependency resolution in container generation (Lazy, Func, KeyValuePair, Dictionary).
/// These tests verify that the source generator correctly generates resolution code
/// for wrapper types in constructor parameters within the container.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.WrapperType)]
public class WrapperTypeDependencyTests
{
    [Test]
    public async Task LazyDependency_GeneratesLazyResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Lazy<IMyService> lazyService)
            {
                public Lazy<IMyService> LazyService { get; } = lazyService;
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
    public async Task LazyDependency_OnTransientService_CreatesLazyPerConsumer()
    {
        // A shared Lazy field would hand every consumer the same transient instance.
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class SingletonService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Lazy<MyService> lazy, IEnumerable<Lazy<IMyService>> all);

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task LazyAndFuncDependency_WithKeyedAndNonKeyedRegistrationsOfSameImplementation_GeneratesDistinctFields()
    {
        // The keyed registration gets its own Lazy/Func fields and is not part of the non-keyed collections.
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.MyService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IMyService)], Key = "keyed")]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(
                Lazy<IMyService> lazy,
                [FromKeyedServices("keyed")] Lazy<IMyService> keyedLazy,
                Func<IMyService> factory,
                [FromKeyedServices("keyed")] Func<IMyService> keyedFactory,
                IEnumerable<Lazy<IMyService>> allLazy,
                IEnumerable<Func<IMyService>> allFactories);

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task MultiParamFuncDependency_WithDecoratedAndKeyedServices_AppliesDecoratorsAndKeys()
    {
        // Each call creates a new implementation from the arguments and wraps it in the registration's decorators;
        // the keyed registration is resolved through its own keyed Func and gets its key as [ServiceKey].
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.Plain), Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(TestNamespace.IPlain)], Key = "keyed")]

            namespace TestNamespace;

            public interface IDep;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDep)])]
            public sealed class Dep : IDep;

            public interface IDecorated;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDecorated)], Decorators = [typeof(Decorator)])]
            public sealed class Decorated(string name, IDep dep) : IDecorated;

            public sealed class Decorator(IDecorated inner, IDep dep) : IDecorated;

            public interface IPlain;

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IPlain)])]
            public sealed class Plain(string name, [ServiceKey] object? key) : IPlain;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(
                Func<string, IDecorated> decorated,
                Func<string, IPlain> plain,
                [FromKeyedServices("keyed")] Func<string, IPlain> keyedPlain);
            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_GeneratesFuncResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<IMyService> serviceFactory)
            {
                public Func<IMyService> ServiceFactory { get; } = serviceFactory;
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
    public async Task FuncDependency_WithSingleInputParameter_MatchesConstructorParameterByType()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public class MyService(string name, ILogger logger) : IService
            {
                public string Name { get; } = name;
                public ILogger Logger { get; } = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<string, IService> serviceFactory)
            {
                public Func<string, IService> ServiceFactory { get; } = serviceFactory;
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
    public async Task FuncDependency_WithMultiParamFunc_AndMultiParameterInjectionMethod_ResolvesEachParameter()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface ILogger { }
            public interface IClock { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IClock)])]
            public class Clock : IClock { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public class MyService(string name) : IService
            {
                public string Name { get; } = name;

                [IocInject]
                public void Initialize(ILogger logger, IClock clock) { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<string, IService> serviceFactory)
            {
                public Func<string, IService> ServiceFactory { get; } = serviceFactory;
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
    public async Task FuncDependency_WithMultipleInputParameters_UsesFirstUnusedTypeMatches()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public class MyService(string name, int count, string alias, ILogger logger) : IService
            {
                public string Name { get; } = name;
                public int Count { get; } = count;
                public string Alias { get; } = alias;
                public ILogger Logger { get; } = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<string, int, IService> serviceFactory)
            {
                public Func<string, int, IService> ServiceFactory { get; } = serviceFactory;
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
    public async Task FuncDependency_WithUnmatchedInputType_IgnoresInputAndResolvesFromDi()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public sealed class SomeType;

            public interface IService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public class MyService(ILogger logger) : IService
            {
                public ILogger Logger { get; } = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<SomeType, IService> serviceFactory)
            {
                public Func<SomeType, IService> ServiceFactory { get; } = serviceFactory;
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
    public async Task KeyValuePairDependency_GeneratesKvpResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class Service1 : IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(KeyValuePair<string, IService> entry)
            {
                public KeyValuePair<string, IService> Entry { get; } = entry;
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
    public async Task DictionaryDependency_GeneratesDictionaryResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
            public class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class HandlerRegistry(IDictionary<string, IHandler> handlers)
            {
                public IDictionary<string, IHandler> Handlers { get; } = handlers;
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
    public async Task ReadOnlyDictionaryDependency_GeneratesDictionaryResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
            public class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class HandlerRegistry(IReadOnlyDictionary<string, IHandler> handlers)
            {
                public IReadOnlyDictionary<string, IHandler> Handlers { get; } = handlers;
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
    public async Task NestedEnumerableLazy_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class PluginManager(IEnumerable<Lazy<IPlugin>> lazyPlugins)
            {
                public IEnumerable<Lazy<IPlugin>> LazyPlugins { get; } = lazyPlugins;
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
    public async Task NestedEnumerableFunc_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class PluginManager(IEnumerable<Func<IPlugin>> funcPlugins)
            {
                public IEnumerable<Func<IPlugin>> FuncPlugins { get; } = funcPlugins;
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
    public async Task NestedLazyFunc_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Lazy<Func<IMyService>> lazyFactory)
            {
                public Lazy<Func<IMyService>> LazyFactory { get; } = lazyFactory;
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
    public async Task NestedFuncLazy_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<Lazy<IMyService>> funcLazy)
            {
                public Func<Lazy<IMyService>> FuncLazy { get; } = funcLazy;
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
    public async Task NestedLazyEnumerable_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Lazy<IEnumerable<IPlugin>> lazyPlugins)
            {
                public Lazy<IEnumerable<IPlugin>> LazyPlugins { get; } = lazyPlugins;
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
    public async Task CollectionWrapperKind_HasCorrectWrapperKindOnCollectionTypes()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class ServiceA : IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class ServiceB : IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(
                IEnumerable<IService> enumerable,
                IReadOnlyCollection<IService> readOnlyCollection,
                IReadOnlyList<IService> readOnlyList)
            {
                public IEnumerable<IService> Enumerable { get; } = enumerable;
                public IReadOnlyCollection<IService> ReadOnlyCollection { get; } = readOnlyCollection;
                public IReadOnlyList<IService> ReadOnlyList { get; } = readOnlyList;
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
    public async Task KeyValuePairDependency_WithKeyedServices_GeneratesKvpResolvers()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "handler1")]
            public class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "handler2")]
            public class Handler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(KeyValuePair<string, IHandler> entry)
            {
                public KeyValuePair<string, IHandler> Entry { get; } = entry;
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
    public async Task DictionaryDependency_WithKeyedServices_GeneratesKvpResolvers()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "h1")]
            public class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "h2")]
            public class Handler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class HandlerRegistry(IDictionary<string, IHandler> handlers)
            {
                public IDictionary<string, IHandler> Handlers { get; } = handlers;
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
    public async Task KeyValuePairDependency_WithMixedKeyTypes_OnlyGeneratesMatchingKvpResolvers()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyed { }

            public enum KeyEnum { Key0, Key1 }

            public static class KeyedExtensions
            {
                public static readonly Guid Key = Guid.Parse("12345678-1234-1234-1234-123456789012");
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyed)], Key = "Key")]
            public class Keyed : IKeyed { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyed)], Key = KeyEnum.Key0)]
            public class KeyedEnum : IKeyed { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyed)], Key = nameof(KeyedExtensions.Key), KeyType = KeyType.Csharp)]
            public class KeyedCsharp : IKeyed { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class StringConsumer(KeyValuePair<string, IKeyed> entry)
            {
                public KeyValuePair<string, IKeyed> Entry { get; } = entry;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class EnumConsumer(KeyValuePair<KeyEnum, IKeyed> entry)
            {
                public KeyValuePair<KeyEnum, IKeyed> Entry { get; } = entry;
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
    public async Task FuncDependency_WithMultiParamFunc_InExplicitOnlyContainer_WhenReturnTypeNotRegistered_FallsBackToServiceProvider()
    {
        // Regression test: when ExplicitOnly container has a consumer depending on Func<string, IService>
        // and IService is NOT explicitly registered, the generator must NOT recurse into
        // BuildWrapperExpressionForContainer via BuildServiceResolutionCallForContainer.
        // Expected: generated code uses GetRequiredService(typeof(Func<string, IService>)) fallback.
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            // IService is intentionally NOT registered — not via [IocRegister] and not via [IocRegisterFor]
            // Consumer is registered via [IocRegisterFor] on the ExplicitOnly container
            public class Consumer(Func<string, IService> serviceFactory)
            {
                public Func<string, IService> ServiceFactory { get; } = serviceFactory;
            }

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor(typeof(Consumer), Lifetime = ServiceLifetime.Singleton)]
            public partial class ExplicitContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_WithMultiParamFunc_InExplicitOnlyContainer_WithKeyedService_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            public class Consumer([IocInject(Key = "myKey")] Func<string, IService> serviceFactory)
            {
                public Func<string, IService> ServiceFactory { get; } = serviceFactory;
            }

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor(typeof(Consumer), Lifetime = ServiceLifetime.Singleton)]
            public partial class ExplicitContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_WithMultiParamFunc_InExplicitOnlyContainer_WhenOptional_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }

            public class Consumer(Func<string, IService>? serviceFactory)
            {
                public Func<string, IService>? ServiceFactory { get; } = serviceFactory;
            }

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor(typeof(Consumer), Lifetime = ServiceLifetime.Singleton)]
            public partial class ExplicitContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task TaskLazy_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(System.Threading.Tasks.Task<Lazy<IMyService>> service)
            {
                public System.Threading.Tasks.Task<Lazy<IMyService>> Service { get; } = service;
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
    public async Task LazyTask_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Lazy<System.Threading.Tasks.Task<IMyService>> service)
            {
                public Lazy<System.Threading.Tasks.Task<IMyService>> Service { get; } = service;
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
    public async Task TaskFunc_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(System.Threading.Tasks.Task<Func<IMyService>> service)
            {
                public System.Threading.Tasks.Task<Func<IMyService>> Service { get; } = service;
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
    public async Task FuncTask_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<System.Threading.Tasks.Task<IMyService>> service)
            {
                public Func<System.Threading.Tasks.Task<IMyService>> Service { get; } = service;
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
    public async Task TaskEnumerable_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(System.Threading.Tasks.Task<IEnumerable<IMyService>> service)
            {
                public System.Threading.Tasks.Task<IEnumerable<IMyService>> Service { get; } = service;
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
    public async Task EnumerableTask_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(IEnumerable<System.Threading.Tasks.Task<IMyService>> service)
            {
                public IEnumerable<System.Threading.Tasks.Task<IMyService>> Service { get; } = service;
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
    public async Task TripleNested_LazyFuncLazy_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Lazy<Func<Lazy<IMyService>>> triple)
            {
                public Lazy<Func<Lazy<IMyService>>> Triple { get; } = triple;
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
    public async Task TripleNested_EnumerableLazyFunc_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(IEnumerable<Lazy<Func<IMyService>>> triple)
            {
                public IEnumerable<Lazy<Func<IMyService>>> Triple { get; } = triple;
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
    public async Task TripleNested_FuncLazyEnumerable_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Consumer(Func<Lazy<IEnumerable<IMyService>>> triple)
            {
                public Func<Lazy<IEnumerable<IMyService>>> Triple { get; } = triple;
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
