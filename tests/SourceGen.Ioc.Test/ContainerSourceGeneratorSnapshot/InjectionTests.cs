namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for property and method injection container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.InjectAttribute)]
public class InjectionTests
{
    [Test]
    public async Task Container_WithPropertyInjection_GeneratesPropertyAssignment()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public ILogger Logger { get; set; } = default!;
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
    public async Task Container_WithMethodInjection_GeneratesMethodCall()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public ILogger? Logger { get; private set; }

                [IocInject]
                public void Initialize(ILogger logger)
                {
                    Logger = logger;
                }
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
    public async Task Container_WithFieldInjection_GeneratesFieldAssignment()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public ILogger _logger;
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_ContainerFeatureDisabled_DoesNotGenerateContainerOutput()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Logger { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,PropertyInject,FieldInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).IsNull();
    }

    [Test]
    public async Task Container_FieldInjectFeatureDisabled_IgnoresFieldInjection()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILogger)])]
            public class Logger : ILogger { }

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public ILogger _logger;
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.SourceGenIocFeatures"] = "Register,Container,PropertyInject,MethodInject"
        };

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            analyzerConfigOptions: analyzerConfigOptions);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

}
