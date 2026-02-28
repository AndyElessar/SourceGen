namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for module import container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.ImportModule)]
public class ModuleImportContainerTests
{
    [Test]
    public async Task Container_WithImportedModule_CombinesServices()
    {
        // Dependency assembly (SharedLib) - simulates a separate NuGet package or class library
        const string sharedLibSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedLib;

            public interface ISharedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISharedService)])]
            public class SharedService : ISharedService { }

            [IocContainer]
            public partial class SharedModule { }
            """;

        // Main assembly (MainApp) - imports the SharedModule from the dependency
        const string mainAppSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace MainApp;

            public interface ILocalService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILocalService)])]
            public class LocalService : ILocalService { }

            [IocImportModule<SharedLib.SharedModule>]
            [IocContainer]
            public partial class AppContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithDependencies<IocSourceGenerator>(
            (sharedLibSource, "SharedLib"), (mainAppSource, "MainApp"));
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithMultipleImportedModules_CombinesAllServices()
    {
        // First dependency assembly (SharedLib1)
        const string sharedLib1Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedLib1;

            public interface IService1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService1)])]
            public class Service1 : IService1 { }

            [IocContainer]
            public partial class SharedModule1 { }
            """;

        // Second dependency assembly (SharedLib2)
        const string sharedLib2Source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedLib2;

            public interface IService2 { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IService2)])]
            public class Service2 : IService2 { }

            [IocContainer]
            public partial class SharedModule2 { }
            """;

        // Main assembly (MainApp) - imports both modules
        const string mainAppSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace MainApp;

            public interface ILocalService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILocalService)])]
            public class LocalService : ILocalService { }

            [IocImportModule<SharedLib1.SharedModule1>]
            [IocImportModule<SharedLib2.SharedModule2>]
            [IocContainer]
            public partial class AppContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithDependencies<IocSourceGenerator>(
            (sharedLib1Source, "SharedLib1"), (sharedLib2Source, "SharedLib2"), (mainAppSource, "MainApp"));
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithNestedModuleImports_ThreeLevelHierarchy()
    {
        // Level C - deepest dependency (no imports)
        const string levelCSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace LevelC;

            public interface IServiceC { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IServiceC)])]
            public class ServiceC : IServiceC { }

            [IocContainer]
            public partial class ModuleC { }
            """;

        // Level B - imports from Level C
        const string levelBSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace LevelB;

            public interface IServiceB { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB(LevelC.IServiceC serviceC) : IServiceB { }

            [IocImportModule<LevelC.ModuleC>]
            [IocContainer]
            public partial class ModuleB { }
            """;

        // Level A (Main) - imports from Level B
        const string levelASource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace LevelA;

            public interface IServiceA { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA(LevelB.IServiceB serviceB) : IServiceA { }

            [IocImportModule<LevelB.ModuleB>]
            [IocContainer]
            public partial class AppContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithDependencies<IocSourceGenerator>(
            (levelCSource, "LevelC"), (levelBSource, "LevelB"), (levelASource, "LevelA"));
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithImportedModule_UseSwitchStatementIgnored_UsesFrozenDictionary()
    {
        // Dependency assembly (SharedLib) - simulates a separate NuGet package or class library
        const string sharedLibSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedLib;

            public interface ISharedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ISharedService)])]
            public class SharedService : ISharedService { }

            [IocContainer]
            public partial class SharedModule { }
            """;

        // Main assembly (MainApp) - imports the SharedModule with UseSwitchStatement = true
        // UseSwitchStatement should be ignored because there are imported modules
        const string mainAppSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace MainApp;

            public interface ILocalService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ILocalService)])]
            public class LocalService : ILocalService { }

            [IocImportModule<SharedLib.SharedModule>]
            [IocContainer(UseSwitchStatement = true)]
            public partial class AppContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithDependencies<IocSourceGenerator>(
            (sharedLibSource, "SharedLib"), (mainAppSource, "MainApp"));
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
