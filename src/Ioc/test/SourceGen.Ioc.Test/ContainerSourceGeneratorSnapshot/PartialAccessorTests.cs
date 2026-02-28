namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for partial accessor (fast-path service resolution) generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.PartialAccessor)]
public class PartialAccessorTests
{
    /// <summary>
    /// Suppressed diagnostics for initial compilation: 
    /// CS8795 (partial method must have implementation) and CS9248 (partial property must have implementation).
    /// These are expected because the source generator provides the implementation.
    /// </summary>
    private static readonly IReadOnlySet<string> SuppressedPartialDiagnostics = new HashSet<string>(["CS8795", "CS9248"]);

    [Test]
    public async Task PartialMethod_ResolvesRegisteredService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial global::TestNamespace.IMyService GetMyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialProperty_ResolvesRegisteredService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IMyService MyService { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_NullableReturnType_ResolvesOptionally()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IMyService? TryGetMyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_WithKeyedService_ResolvesWithKey()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "redis", ServiceTypes = [typeof(ICache)])]
            public class RedisCache : ICache { }

            [IocContainer]
            public partial class TestContainer
            {
                [IocInject("redis")]
                public partial ICache GetRedisCache();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_NamingConflict_RenamesInternalResolver()
    {
        // Without namespace, the generated resolver method name becomes GetMyService(),
        // which conflicts with the user-declared partial method GetMyService().
        // The generator should rename the internal resolver to GetMyService_Resolve().
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IMyService GetMyService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialMethod_NoFallback_UnregisteredService_ThrowsExpression()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IUnknownService { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer
            {
                public partial IUnknownService GetUnknownService();
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialProperty_WithKeyedService_ResolvesWithKey()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "memory", ServiceTypes = [typeof(ICache)])]
            public class MemoryCache : ICache { }

            [IocContainer]
            public partial class TestContainer
            {
                [IocInject("memory")]
                public partial ICache MemoryCache { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task PartialAccessor_MixedMethodsAndProperties()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB { }

            [IocContainer]
            public partial class TestContainer
            {
                public partial IServiceA GetServiceA();
                public partial IServiceB ServiceB { get; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}

