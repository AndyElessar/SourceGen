namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for factory and instance registration container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.FactoryAndInstance)]
public class FactoryAndInstanceContainerTests
{
    [Test]
    public async Task Container_WithFactoryRegistration_UsesFactoryMethod()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConnection { }

            public class Connection : IConnection
            {
                public Connection(string connectionString) { }
            }

            public static class ConnectionFactory
            {
                public static IConnection Create(IServiceProvider sp)
                {
                    return new Connection("test-connection-string");
                }
            }

            [IocRegisterFor(typeof(Connection), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IConnection)], Factory = nameof(ConnectionFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithInstanceRegistration_UsesStaticInstance()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConfiguration { }

            public class Configuration : IConfiguration
            {
                public static readonly Configuration Default = new();
            }

            [IocRegisterFor(typeof(Configuration), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IConfiguration)], Instance = nameof(Configuration.Default))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
