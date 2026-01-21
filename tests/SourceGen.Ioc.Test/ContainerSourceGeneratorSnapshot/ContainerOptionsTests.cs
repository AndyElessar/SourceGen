namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for container options generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.ContainerOptions)]
public class ContainerOptionsTests
{
    [Test]
    public async Task Container_WithResolveIServiceCollectionFalse_DoesNotGenerateServiceProviderFactory()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer(ResolveIServiceCollection = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithExplicitOnlyMode_OnlyIncludesExplicitRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IExcludedService { }

            // This should be included (explicit on container via IocRegisterFor)
            public class MyService : IMyService { }

            // This should NOT be included in ExplicitOnly mode
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IExcludedService)])]
            public class ExcludedService : IExcludedService { }

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor(typeof(MyService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public partial class ExplicitContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithoutDIPackage_DoesNotGenerateIServiceProviderFactory()
    {
        // When Microsoft.Extensions.DependencyInjection package is not referenced,
        // IServiceProviderFactory should not be generated even if ResolveIServiceCollection = true
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocContainer(ResolveIServiceCollection = true)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithReferences<IocSourceGenerator>(
            source,
            SourceGeneratorTestHelper.BaseReferencesWithoutDI);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithMultipleInterfaceRegistration_GeneratesAllResolutions()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IReader { }
            public interface IWriter { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllInterfaces = true)]
            public class ReadWriteService : IReader, IWriter { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
