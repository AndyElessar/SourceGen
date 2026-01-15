namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

/// <summary>
/// Tests for the SourceGenIocName MSBuild property feature.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.CustomIocName)]
public class CustomIocNameTests
{
    [Test]
    public async Task CustomIocName_GeneratesCustomMethodName()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocName"] = "CustomName"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task CustomIocName_WithTags_GeneratesCustomMethodNamesForTags()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface ITaggedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Scoped, 
                ServiceTypes = [typeof(ITaggedService)],
                Tags = ["Feature1", "Feature2"])]
            public class TaggedService : ITaggedService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocName"] = "MyModule"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task CustomIocName_WithSpecialCharacters_GeneratesSafeMethodName()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocName"] = "My-Special.Name 123"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NoCustomIocName_UsesAssemblyName()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            source,
            assemblyName: "MyProject.Services");
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task CustomIocName_EmptyOrWhitespaceValue_UsesAssemblyName(string iocNameValue)
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocName"] = iocNameValue
        };

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(
            source,
            assemblyName: "FallbackAssembly",
            analyzerConfigOptions: analyzerConfigOptions);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource).UseParameters(iocNameValue);
    }
}
