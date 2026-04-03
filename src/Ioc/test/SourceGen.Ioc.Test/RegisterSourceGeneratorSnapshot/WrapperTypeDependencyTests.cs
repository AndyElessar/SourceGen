namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for wrapper type dependency resolution (Lazy, Func, KeyValuePair, Dictionary).
/// These tests verify that the source generator correctly generates resolution code
/// for wrapper types in constructor parameters and injected members.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.WrapperType)]
public class WrapperTypeDependencyTests
{
    [Test]
    public async Task LazyDependency_GeneratesLazyResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService
            {
                void Execute();
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService
            {
                public void Execute() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<IMyService> lazyService)
            {
                private readonly Lazy<IMyService> _lazyService = lazyService;
                public void DoWork() => _lazyService.Value.Execute();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_GeneratesFuncResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService
            {
                void Execute();
            }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService
            {
                public void Execute() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<IMyService> serviceFactory)
            {
                private readonly Func<IMyService> _factory = serviceFactory;
                public IMyService Create() => _factory();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_WithSingleInputParameter_MatchesConstructorParameterByType()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public sealed class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public sealed class MyService(string name, ILogger logger) : IService
            {
                public string Name { get; } = name;
                public ILogger Logger { get; } = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<string, IService> factory)
            {
                private readonly Func<string, IService> _factory = factory;
                public IService Create(string name) => _factory(name);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_WithMultipleInputParameters_UsesFirstUnusedTypeMatches()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public sealed class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public sealed class MyService(string name, int count, string alias, ILogger logger) : IService
            {
                public string Name { get; } = name;
                public int Count { get; } = count;
                public string Alias { get; } = alias;
                public ILogger Logger { get; } = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<string, int, IService> factory)
            {
                private readonly Func<string, int, IService> _factory = factory;
                public IService Create(string name, int count) => _factory(name, count);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_WithUnmatchedInputType_IgnoresInputAndResolvesFromDi()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public sealed class SomeType;

            public interface IService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(ILogger)])]
            public sealed class Logger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService)])]
            public sealed class MyService(ILogger logger) : IService
            {
                public ILogger Logger { get; } = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<SomeType, IService> factory)
            {
                private readonly Func<SomeType, IService> _factory = factory;
                public IService Create(SomeType value) => _factory(value);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class Service1 : IService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(KeyValuePair<string, IService> entry)
            {
                private readonly KeyValuePair<string, IService> _entry = entry;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class HandlerRegistry(IDictionary<string, IHandler> handlers)
            {
                private readonly IDictionary<string, IHandler> _handlers = handlers;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class HandlerRegistry(IReadOnlyDictionary<string, IHandler> handlers)
            {
                private readonly IReadOnlyDictionary<string, IHandler> _handlers = handlers;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task LazyDependency_WithOpenGeneric_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<IHandler<TestEntity>> lazyHandler)
            {
                private readonly Lazy<IHandler<TestEntity>> _lazyHandler = lazyHandler;
                public void Process(TestEntity entity) => _lazyHandler.Value.Handle(entity);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncDependency_WithOpenGeneric_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>
            {
                void Handle(T item);
            }

            public class TestEntity { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>
            {
                public void Handle(T item) => Console.WriteLine(item?.ToString());
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<IHandler<TestEntity>> handlerFactory)
            {
                private readonly Func<IHandler<TestEntity>> _factory = handlerFactory;
                public void Process(TestEntity entity) => _factory().Handle(entity);
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NullableLazyDependency_GeneratesOptionalResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService
            {
                void Execute();
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService
            {
                public void Execute() { }
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<IMyService>? lazyService = null)
            {
                private readonly Lazy<IMyService>? _lazyService = lazyService;
                public void DoWork() => _lazyService?.Value.Execute();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NestedLazyFunc_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<Func<IMyService>> lazyFactory)
            {
                private readonly Lazy<Func<IMyService>> _lazyFactory = lazyFactory;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NestedFuncLazy_GeneratesNestedResolution()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<Lazy<IMyService>> funcLazy)
            {
                private readonly Func<Lazy<IMyService>> _funcLazy = funcLazy;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public sealed class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class PluginManager(IEnumerable<Lazy<IPlugin>> lazyPlugins)
            {
                private readonly IEnumerable<Lazy<IPlugin>> _lazyPlugins = lazyPlugins;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public sealed class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class PluginManager(IEnumerable<Func<IPlugin>> funcPlugins)
            {
                private readonly IEnumerable<Func<IPlugin>> _funcPlugins = funcPlugins;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public sealed class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<IEnumerable<IPlugin>> lazyPlugins)
            {
                private readonly Lazy<IEnumerable<IPlugin>> _lazyPlugins = lazyPlugins;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyValuePairDependency_WithKeyedServices_GeneratesKvpRegistrations()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "handler1")]
            public sealed class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "handler2")]
            public sealed class Handler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(KeyValuePair<string, IHandler> entry)
            {
                public KeyValuePair<string, IHandler> Entry { get; } = entry;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DictionaryDependency_WithKeyedServices_GeneratesKvpRegistrations()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "h1")]
            public sealed class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IHandler)], Key = "h2")]
            public sealed class Handler2 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class HandlerRegistry(IDictionary<string, IHandler> handlers)
            {
                public IDictionary<string, IHandler> Handlers { get; } = handlers;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyValuePairDependency_WithMixedKeyTypes_OnlyGeneratesMatchingKvpRegistrations()
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
            public sealed class Keyed : IKeyed { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyed)], Key = KeyEnum.Key0)]
            public sealed class KeyedEnum : IKeyed { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyed)], Key = nameof(KeyedExtensions.Key), KeyType = KeyType.Csharp)]
            public sealed class KeyedCsharp : IKeyed { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class StringConsumer(KeyValuePair<string, IKeyed> entry)
            {
                public KeyValuePair<string, IKeyed> Entry { get; } = entry;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class EnumConsumer(KeyValuePair<KeyEnum, IKeyed> entry)
            {
                public KeyValuePair<KeyEnum, IKeyed> Entry { get; } = entry;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task TaskLazy_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(System.Threading.Tasks.Task<Lazy<IMyService>> service)
            {
                public System.Threading.Tasks.Task<Lazy<IMyService>> Service { get; } = service;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task LazyTask_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<System.Threading.Tasks.Task<IMyService>> service)
            {
                public Lazy<System.Threading.Tasks.Task<IMyService>> Service { get; } = service;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task TaskFunc_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(System.Threading.Tasks.Task<Func<IMyService>> service)
            {
                public System.Threading.Tasks.Task<Func<IMyService>> Service { get; } = service;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FuncTask_FallsBackToServiceProvider()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<System.Threading.Tasks.Task<IMyService>> service)
            {
                public Func<System.Threading.Tasks.Task<IMyService>> Service { get; } = service;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(System.Threading.Tasks.Task<IEnumerable<IMyService>> service)
            {
                public System.Threading.Tasks.Task<IEnumerable<IMyService>> Service { get; } = service;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<System.Threading.Tasks.Task<IMyService>> service)
            {
                public IEnumerable<System.Threading.Tasks.Task<IMyService>> Service { get; } = service;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Lazy<Func<Lazy<IMyService>>> triple)
            {
                private readonly Lazy<Func<Lazy<IMyService>>> _triple = triple;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(IEnumerable<Lazy<Func<IMyService>>> triple)
            {
                private readonly IEnumerable<Lazy<Func<IMyService>>> _triple = triple;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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
            public sealed class MyService : IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(Func<Lazy<IEnumerable<IMyService>>> triple)
            {
                private readonly Func<Lazy<IEnumerable<IMyService>>> _triple = triple;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
