namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for Tags functionality.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.Tags)]
public class TagsTests
{
    [Test]
    public async Task Tags_SingleTag_GeneratesTaggedMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyTaggedService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyTaggedService)],
                Tags = ["Tag1"])]
            public class MyTaggedService : IMyTaggedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Tags_MultipleTags_GeneratesAllTaggedMethods()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyTaggedService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyTaggedService)],
                Tags = ["Tag1", "Tag2"])]
            public class MyTaggedService : IMyTaggedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Tags_TagOnly_ExcludesFromDefaultMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyTaggedService { }
            public interface IMyTaggedService2 { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyTaggedService)],
                Tags = ["Tag1", "Tag2"])]
            public class MyTaggedService : IMyTaggedService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyTaggedService2)],
                Tags = ["Tag1"],
                TagOnly = true)]
            public class MyTaggedService2 : IMyTaggedService2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Tags_MixedServicesWithTags_GeneratesCorrectMethods()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface INoTagService { }
            public interface ITag1Service { }
            public interface ITag2Service { }
            public interface IBothTagsService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(INoTagService)])]
            public class NoTagService : INoTagService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped,
                ServiceTypes = [typeof(ITag1Service)],
                Tags = ["Tag1"])]
            public class Tag1Service : ITag1Service { }

            [IocRegister(
                Lifetime = ServiceLifetime.Transient,
                ServiceTypes = [typeof(ITag2Service)],
                Tags = ["Tag2"])]
            public class Tag2Service : ITag2Service { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IBothTagsService)],
                Tags = ["Tag1", "Tag2"])]
            public class BothTagsService : IBothTagsService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
