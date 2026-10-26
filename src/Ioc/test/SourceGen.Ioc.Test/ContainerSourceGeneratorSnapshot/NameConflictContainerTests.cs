namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests verifying the generated container compiles when the container's namespace
/// declares types named like the BCL / DI types the generated code uses.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
public class NameConflictContainerTests
{
    private const string Features = "Register,Container,PropertyInject,FieldInject,MethodInject,AsyncMethodInject";

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace Services
        {
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDependency)])]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDependency)], Key = "keyed")]
            public class KeyedDependency : IDependency { }

            public interface IScoped { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScoped)])]
            public class Scoped : IScoped, IDisposable
            {
                public void Dispose() { }
            }

            public interface IAsyncService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IAsyncService)])]
            public class AsyncService : IAsyncService
            {
                [IocInject]
                public Task InitAsync(IDependency dependency) => Task.CompletedTask;
            }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(
                Lazy<IDependency> lazy,
                Func<IDependency> factory,
                IEnumerable<IDependency> all,
                IReadOnlyDictionary<string, IDependency> keyed,
                Task<IAsyncService> asyncService) { }
        }

        namespace TestNamespace
        {
            // User types named like the types used by generated code
            public class Type { }
            public class Lock { }
            public class SemaphoreSlim { }
            public class SpinLock { }
            public class Interlocked { }
            public class Task { }
            public class Task<T> { }
            public class ValueTask { }
            public class Func<T> { }
            public class Lazy<T> { }
            public class KeyValuePair<TKey, TValue> { }
            public class IEnumerable<T> { }
            public class IReadOnlyCollection<T> { }
            public class Dictionary<TKey, TValue> { }
            public class Exception { }
            public class InvalidOperationException { }
            public class ObjectDisposedException { }
            public class IDisposable { }
            public class IAsyncDisposable { }
            public class FrozenDictionary<TKey, TValue> { }
            public class ServiceIdentifier { }
            public class IIocContainer<T> { }
            public class IServiceProvider { }
            public class IKeyedServiceProvider { }
            public class IServiceProviderIsService { }
            public class IServiceProviderIsKeyedService { }
            public class ISupportRequiredService { }
            public class IServiceScope { }
            public class IServiceScopeFactory { }
            public class IServiceCollection { }
            public class IServiceProviderFactory<T> { }
            public class AsyncServiceScope { }
            public class KeyedService { }
            public class System { }
            public class Microsoft { }
            public class SourceGen { }

            [IocContainer]
            public partial class TestContainer { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SemaphoreSlim, EagerResolveOptions = EagerResolveOptions.None)]
            public partial class SemaphoreContainer { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.SpinLock, EagerResolveOptions = EagerResolveOptions.Scoped)]
            public partial class SpinLockContainer { }

            [IocContainer(ThreadSafeStrategy = ThreadSafeStrategy.CompareExchange, EagerResolveOptions = EagerResolveOptions.None, UseSwitchStatement = true)]
            public partial class SwitchContainer { }
        }
        """;

    [Test]
    public async Task Container_WithConflictingTypeNamesInNamespace_GeneratesCompilableCode()
    {
        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            Source,
            analyzerConfigOptions: new Dictionary<string, string>
            {
                ["build_property.SourceGenIocFeatures"] = Features
            });

        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "TestContainer.Container.g.cs");

        await Verify(generatedSource);
    }
}
