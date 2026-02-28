namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for collection resolution container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.Collection)]
public class CollectionResolutionContainerTests
{
    [Test]
    public async Task Container_WithCollectionResolution_GeneratesEnumerableService()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin3 : IPlugin { }

            [IocDiscover<IEnumerable<IPlugin>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithCollectionResolution_GeneratesReadOnlyCollectionAndArrayServices()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
            public class Handler1 : IHandler { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler)])]
            public class Handler2 : IHandler { }

            [IocDiscover<IEnumerable<IHandler>>]
            [IocDiscover<IReadOnlyCollection<IHandler>>]
            [IocDiscover<IReadOnlyList<IHandler>>]
            [IocDiscover<IHandler[]>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithMultipleInstanceRegistrations_GeneratesEnumerableForSameType()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public static class ConnectionStrings
            {
                public const string Primary = "Server=primary;Database=Db;";
                public const string Secondary = "Server=secondary;Database=Db;";
                public const string Backup = "Server=backup;Database=Db;";
            }

            [IocRegisterFor<string>(ServiceLifetime.Singleton, Instance = nameof(ConnectionStrings.Primary))]
            [IocRegisterFor<string>(ServiceLifetime.Singleton, Instance = nameof(ConnectionStrings.Secondary))]
            [IocRegisterFor<string>(ServiceLifetime.Singleton, Instance = nameof(ConnectionStrings.Backup))]
            public class Marker { }

            [IocDiscover<IEnumerable<string>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
