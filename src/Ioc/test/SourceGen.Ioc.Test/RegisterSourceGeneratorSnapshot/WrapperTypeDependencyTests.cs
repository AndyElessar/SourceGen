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
    public async Task LazyAndFuncDependency_WithDecoratedAndKeyedServices_ResolvesServiceRegistrations()
    {
        // Lazy<IDecorated> / Func<IDecorated> must apply the decorator; the keyed registration must not add a non-keyed wrapper.
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.KeyedService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IKeyed)], Key = "keyed")]

            namespace TestNamespace;

            public interface IDecorated;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDecorated)], Decorators = [typeof(Decorator)])]
            public sealed class Decorated : IDecorated;

            public sealed class Decorator(IDecorated inner) : IDecorated;

            public interface IKeyed;

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyed)])]
            public sealed class KeyedService : IKeyed;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class Consumer(
                Lazy<IDecorated> lazy,
                Func<IDecorated> factory,
                IEnumerable<Lazy<IKeyed>> allLazy,
                [FromKeyedServices("keyed")] Lazy<IKeyed> keyedLazy);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

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

    [Test]
    public async Task FuncDependency_TupleElementNamesDiffer_RegistersFuncOnce()
    {
        // Tuple element names are not part of the runtime type, so both Func dependencies are one service type, created
        // from the registration whose tuple elements are named differently
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(ServiceTypes = [typeof(IHandler<(Entity X, string Y)>)])]
            public sealed class NamedHandler : IHandler<(Entity X, string Y)>;

            [IocRegister]
            public sealed class Consumer(Func<IHandler<(Entity A, string B)>> func);

            [IocRegister]
            public sealed class OtherConsumer(Func<IHandler<(Entity, string)>> func);

            // The input argument is passed to the constructor parameter of the same runtime type
            [IocRegister]
            public sealed class Point((int X, int Y) position);

            [IocRegister]
            public sealed class PointConsumer(Func<(int A, int B), Point> create);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration")!;
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        var handlerFuncCount = generatedSource.Split(["services.AddTransient<global::System.Func<global::TestNamespace.IHandler<"], StringSplitOptions.None).Length - 1;
        await Assert.That(handlerFuncCount).IsEqualTo(1);
        await Assert.That(generatedSource).Contains("new global::TestNamespace.Point(arg0)");
        await Assert.That(containerSource).Contains("new global::TestNamespace.Point(arg0)");
    }

    [Test]
    public async Task OpenGenericDependency_TupleElementNamesDiffer_ClosesOnce()
    {
        // Dependencies that differ only in tuple element names are one runtime type, so the open generic registration is
        // closed once for them, and their Lazy and Func are registered once
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler<T>;
            public class Entity;

            [IocRegister(ServiceTypes = [typeof(IHandler<>)])]
            public sealed class Handler<T> : IHandler<T>;

            [IocRegister]
            public sealed class Consumer(Lazy<IHandler<(Entity A, string B)>> lazy, Func<IHandler<(Entity A, string B)>> func);

            [IocRegister]
            public sealed class OtherConsumer(Lazy<IHandler<(Entity, string)>> lazy, Func<IHandler<(Entity, string)>> func);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration")!;
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs")!;

        await Assert.That(Count(generatedSource, "services.AddTransient<global::TestNamespace.Handler<(")).IsEqualTo(1);
        await Assert.That(Count(generatedSource, "services.AddTransient<global::TestNamespace.IHandler<(")).IsEqualTo(1);
        await Assert.That(Count(generatedSource, "services.AddTransient<global::System.Lazy<global::TestNamespace.IHandler<")).IsEqualTo(1);
        await Assert.That(Count(generatedSource, "services.AddTransient<global::System.Func<global::TestNamespace.IHandler<")).IsEqualTo(1);
        await Assert.That(Count(containerSource, "new global::TestNamespace.Handler<(")).IsEqualTo(1);
        await Verify(generatedSource);

        static int Count(string text, string value) => text.Split([value], StringSplitOptions.None).Length - 1;
    }

    #region Explicit wrapper registrations

    private const string ExplicitWrapperHandlers = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface IHandler { }

        [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)], Key = "a")]
        public sealed class Handler : IHandler { }

        public sealed class HandlerDictionary : Dictionary<string, IHandler>
        {
            public static readonly HandlerDictionary Default = new();
            public static readonly HandlerDictionary Keyed = new();
        }

        public static class Wrappers
        {
            public static readonly Lazy<IHandler> Lazy = new(static () => new Handler());
            public static readonly Func<IHandler> Func = static () => new Handler();
            public static readonly Lazy<Func<IHandler>> LazyFunc = new(static () => Func);
        }

        """;

    [Test]
    public async Task ExplicitDictionaryRegistration_IsInjectedInsteadOfBuiltDictionary()
    {
        // Like MS.DI, an injected IReadOnlyDictionary<K, V> registered explicitly is the registration GetService returns,
        // not a dictionary built from the KeyValuePair registrations (GitHub issue #241)
        const string source = ExplicitWrapperHandlers + """
            [IocRegisterFor<HandlerDictionary>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IHandler>)], Instance = "global::TestNamespace.HandlerDictionary.Default")]
            [IocRegisterFor<HandlerDictionary>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IHandler>)], Instance = "global::TestNamespace.HandlerDictionary.Keyed", Key = "k")]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Consumer(
                IReadOnlyDictionary<string, IHandler> handlers,
                [FromKeyedServices("k")] IReadOnlyDictionary<string, IHandler> keyedHandlers,
                IDictionary<string, IHandler> notRegistered)
            {
                [IocInject]
                public IReadOnlyDictionary<string, IHandler>? Optional { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource)
            .Contains("var p0 = sp.GetRequiredService<global::System.Collections.Generic.IReadOnlyDictionary<string, global::TestNamespace.IHandler>>();")
            .And.Contains("var p1 = sp.GetRequiredKeyedService<global::System.Collections.Generic.IReadOnlyDictionary<string, global::TestNamespace.IHandler>>(\"k\");")
            .And.Contains("var s0_p0 = sp.GetService<global::System.Collections.Generic.IReadOnlyDictionary<string, global::TestNamespace.IHandler>>();")
            // A dictionary type that is not registered explicitly is still built from the keyed registrations
            .And.Contains("var p2 = sp.GetServices<global::System.Collections.Generic.KeyValuePair<string, global::TestNamespace.IHandler>>().ToDictionary();");
        await Verify(generatedSource);
    }

    [Test]
    public async Task ExplicitLazyAndFuncRegistrations_AreInjectedAndGenerateNoWrapperRegistrations()
    {
        // Generated Lazy<T> / Func<T> registrations would be written after the explicit ones and win in MS.DI (last wins), so
        // they are not generated; Lazy<Func<T>> registered explicitly is injected instead of a new Lazy over Func<T>
        const string source = ExplicitWrapperHandlers + """
            [IocRegisterFor<Lazy<IHandler>>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.Wrappers.Lazy", Key = "a")]
            [IocRegisterFor<Func<IHandler>>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.Wrappers.Func", Key = "a")]
            [IocRegisterFor<Lazy<Func<IHandler>>>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.Wrappers.LazyFunc", Key = "a")]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Consumer(
                [FromKeyedServices("a")] Lazy<IHandler> lazy,
                [FromKeyedServices("a")] Func<IHandler> func,
                [FromKeyedServices("a")] Lazy<Func<IHandler>> lazyFunc);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource)
            .Contains("var p0 = sp.GetRequiredKeyedService<global::System.Lazy<global::TestNamespace.IHandler>>(\"a\");")
            .And.Contains("var p1 = sp.GetRequiredKeyedService<global::System.Func<global::TestNamespace.IHandler>>(\"a\");")
            .And.Contains("var p2 = sp.GetRequiredKeyedService<global::System.Lazy<global::System.Func<global::TestNamespace.IHandler>>>(\"a\");")
            .And.DoesNotContain("=> new global::System.Lazy<global::TestNamespace.IHandler>(")
            .And.DoesNotContain("=> new global::System.Func<global::TestNamespace.IHandler>(");
        await Verify(generatedSource);
    }

    [Test]
    public async Task ExplicitLazyRegistration_UnderOtherKey_KeepsGeneratedWrapper()
    {
        // Only an explicit registration under the same key replaces the generated Lazy<T> of a registration
        const string source = ExplicitWrapperHandlers + """
            [IocRegisterFor<Lazy<IHandler>>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.Wrappers.Lazy")]
            public static class Registrations;

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Consumer([FromKeyedServices("a")] Lazy<IHandler> lazy);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource)
            .Contains("services.AddKeyedSingleton<global::System.Lazy<global::TestNamespace.IHandler>>(\"a\", (global::System.IServiceProvider sp, object? key) => new global::System.Lazy<global::TestNamespace.IHandler>(");
    }

    [Test]
    public async Task ExplicitDictionaryRegistration_AsyncInit_KeepsBuiltDictionary()
    {
        // An async-init registration is registered as Task<T> only, so the dependency keeps the built dictionary
        const string source = ExplicitWrapperHandlers + """
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IHandler>)])]
            public sealed class AsyncHandlerDictionary : Dictionary<string, IHandler>
            {
                [IocInject]
                public System.Threading.Tasks.Task InitAsync() => System.Threading.Tasks.Task.CompletedTask;
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Consumer(IReadOnlyDictionary<string, IHandler> handlers);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject,AsyncMethodInject"
            });
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource)
            .Contains("var p0 = sp.GetServices<global::System.Collections.Generic.KeyValuePair<string, global::TestNamespace.IHandler>>().ToDictionary();")
            .And.DoesNotContain("sp.GetRequiredService<global::System.Collections.Generic.IReadOnlyDictionary<string, global::TestNamespace.IHandler>>()");
    }

    [Test]
    public async Task ExplicitWrapperRegistrations_WithTags_ResolveOnlyWhenAlwaysRegisteredWithConsumer()
    {
        // A tagged registration is registered when any of its tags is passed, and an untagged one only when no tags are:
        // the explicit registration is used only when it is registered whenever the consumer is
        const string source = ExplicitWrapperHandlers + """
            [IocRegisterFor<HandlerDictionary>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IReadOnlyDictionary<string, IHandler>)], Instance = "global::TestNamespace.HandlerDictionary.Default", Tags = ["A", "B"])]
            [IocRegisterFor<Lazy<IHandler>>(ServiceLifetime.Singleton, Instance = "global::TestNamespace.Wrappers.Lazy", Key = "a", Tags = ["A"])]
            public static class Registrations;

            // Tags covered by the explicit registrations' tags: the explicit dictionary is injected
            [IocRegister(Lifetime = ServiceLifetime.Transient, Tags = ["A"])]
            public sealed class CoveredConsumer(IReadOnlyDictionary<string, IHandler> handlers);

            // "C" does not register the explicit dictionary
            [IocRegister(Lifetime = ServiceLifetime.Transient, Tags = ["A", "C"])]
            public sealed class PartlyCoveredConsumer(IReadOnlyDictionary<string, IHandler> handlers);

            // Untagged registrations are not registered together with tagged ones
            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class UntaggedConsumer(IReadOnlyDictionary<string, IHandler> handlers, [FromKeyedServices("a")] Lazy<IHandler> lazy);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration")!;

        await Assert.That(Count(generatedSource, "sp.GetRequiredService<global::System.Collections.Generic.IReadOnlyDictionary<string, global::TestNamespace.IHandler>>()")).IsEqualTo(1);
        await Assert.That(Count(generatedSource, "sp.GetServices<global::System.Collections.Generic.KeyValuePair<string, global::TestNamespace.IHandler>>().ToDictionary()")).IsEqualTo(2);

        // The untagged Handler's generated Lazy<IHandler> is kept: the tagged explicit Lazy<IHandler> is not registered with it
        await Assert.That(generatedSource).Contains("=> new global::System.Lazy<global::TestNamespace.IHandler>(");
        await Verify(generatedSource);

        static int Count(string text, string value) => text.Split([value], StringSplitOptions.None).Length - 1;
    }

    [Test]
    public async Task MultiParamFuncDependency_WithTaskOfAsyncInitService_ResolvesTaskRegistration()
    {
        // The body of a Func with input parameters resolves Task<T> of an async-init service through its Task<T> registration,
        // like a constructor parameter: the service itself is not registered
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public sealed class AsyncService
            {
                [IocInject]
                public Task InitAsync() => Task.CompletedTask;
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Created(int value, Task<AsyncService> service);

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public sealed class Consumer(Func<int, Created> create);
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject,AsyncMethodInject"
            });
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource)
            .Contains("new global::System.Func<int, global::TestNamespace.Created>(")
            .And.Contains("= sp.GetRequiredService<global::System.Threading.Tasks.Task<global::TestNamespace.AsyncService>>();")
            .And.DoesNotContain("Task.FromResult(sp.GetRequiredService<global::TestNamespace.AsyncService>())");
    }

    #endregion
}
