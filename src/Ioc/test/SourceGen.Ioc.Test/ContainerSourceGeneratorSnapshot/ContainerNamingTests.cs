namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for the names of generated container fields and resolver methods.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.BasicContainer)]
public class ContainerNamingTests
{
    [Test]
    public async Task Container_WithSameSimpleNameInDifferentNamespaces_AppendsHashToCollidingNames()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace First
            {
                public interface IService;

                [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
                public class Service : IService;
            }

            namespace Second
            {
                public interface IService;

                [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
                public class Service : IService;

                [IocRegister(Lifetime = ServiceLifetime.Transient)]
                public class Consumer(First.Service first, Second.Service second, IEnumerable<First.IService> firstAll, IEnumerable<Second.IService> secondAll);
            }

            namespace TestNamespace
            {
                [IocContainer]
                public partial class TestContainer { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithNamesTakenByUserOrContainerMembers_AppendsResolveSuffix()
    {
        // Scope would generate CreateScope (IServiceScopeFactory), Logger collides with the user's GetLogger and _logger.
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Scope;

            [IocRegister(Lifetime = ServiceLifetime.Scoped)]
            public class Logger;

            [IocContainer]
            public partial class TestContainer
            {
                private readonly object _logger = new();

                public string GetLogger() => _logger.ToString()!;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithDerivedNamesTakenByUserMembers_AppendsResolveSuffix()
    {
        // Each user member is a name the generator would derive: an Async resolver, an async Create method,
        // or a synchronization field of a service field.
        const string source = """
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Cache;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Clock;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Queue;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Pool;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Job;

            [IocContainer]
            public partial class TestContainer
            {
                private readonly object _queueLock = new();
                private readonly object _poolSemaphore = new();
                private readonly object _jobSpinLock = new();

                public Task GetCacheAsync() => Task.CompletedTask;

                public Task CreateClockAsync() => Task.CompletedTask;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
