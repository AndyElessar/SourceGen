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
        await result.VerifyCompilableAsync();
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
        await result.VerifyCompilableAsync();
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
        await result.VerifyCompilableAsync();
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
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithIncludeTags_OnlyIncludesMatchingTaggedServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFeature1Service { }
            public interface IFeature2Service { }
            public interface IFeature3Service { }
            public interface INoTagService { }

            // This should be included (has matching tag "Feature1")
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFeature1Service)], Tags = ["Feature1"])]
            public class Feature1Service : IFeature1Service { }

            // This should be included (has matching tag "Feature2")
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFeature2Service)], Tags = ["Feature2", "Feature3"])]
            public class Feature2Service : IFeature2Service { }

            // This should NOT be included (no matching tag)
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFeature3Service)], Tags = ["Feature3"])]
            public class Feature3Service : IFeature3Service { }

            // This should NOT be included (no tags defined)
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(INoTagService)])]
            public class NoTagService : INoTagService { }

            [IocContainer(IncludeTags = ["Feature1", "Feature2"])]
            public partial class FeatureContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithExplicitOnlyMode_GenericAttributeWorks()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            // This should be included (explicit on container via generic IocRegisterFor<T>)
            public class MyService : IMyService { }

            [IocContainer(ExplicitOnly = true)]
            [IocRegisterFor<MyService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public partial class ExplicitContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithExplicitOnlyAndIncludeTags_ExplicitOnlyTakesPrecedence()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IExplicitService { }
            public interface ITaggedService { }

            // This should NOT be included (not explicitly on container, even though it has a matching tag)
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ITaggedService)], Tags = ["Feature1"])]
            public class TaggedService : ITaggedService { }

            // This should be included (explicit on container)
            public class ExplicitService : IExplicitService { }

            [IocContainer(ExplicitOnly = true, IncludeTags = ["Feature1"])]
            [IocRegisterFor(typeof(ExplicitService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IExplicitService)])]
            public partial class ExplicitContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
