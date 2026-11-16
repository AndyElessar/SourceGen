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

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithSingleRegistrationInjectedAsCollection_GeneratesCollectionResolver()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class PluginHost(IEnumerable<IPlugin> plugins) { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithSingleRegistrationRequestedByServiceProviderInvocation_GeneratesCollectionResolver()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin : IPlugin { }

            public static class PluginRunner
            {
                public static IEnumerable<IPlugin> All(IServiceProvider serviceProvider) => serviceProvider.GetServices<IPlugin>();
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
    public async Task Container_WithSingleRegistrationDiscoveredAsCollection_GeneratesCollectionResolver()
    {
        const string source = """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin : IPlugin { }

            [IocDiscover<IEnumerable<IPlugin>>]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("private global::TestNamespace.IPlugin[] GetAllIPluginArray() =>");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithSingleRegistrationNotRequestedAsCollection_DoesNotGenerateCollectionResolver()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin : IPlugin { }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).DoesNotContain("GetAllIPluginArray");
        await Assert.That(generatedSource).Contains("return __ResolveCollection(serviceType, null) ?? GetFallbackServiceProvider()?.GetService(serviceType);");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithSingleRegistrationNotRequestedAsCollection_UseSwitchStatement_ResolvesCollectionFromLocalResolvers()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)], Key = "keyed")]
            public class KeyedPlugin : IPlugin { }

            [IocContainer(UseSwitchStatement = true)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("foreach(var entry in _localResolvers)");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithSingleRegistrationNotRequestedAsCollection_WithoutServiceProviderIntegration_ResolvesCollectionWithoutFallback()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)], Key = "keyed")]
            public class KeyedPlugin : IPlugin { }

            [IocContainer(IntegrateServiceProvider = false)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains(": __ResolveCollection<T>(serviceKey) ?? [];");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithCollectionResolvers_UseSwitchStatement_DispatchesAllCollectionTypes()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin2 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)], Key = "keyed")]
            public class KeyedPlugin : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(IEnumerable<Lazy<IPlugin>> lazyPlugins, IDictionary<string, IPlugin> keyedPlugins) { }

            [IocContainer(UseSwitchStatement = true)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("if(serviceType == typeof(global::System.Collections.Generic.IEnumerable<global::TestNamespace.IPlugin>)) return __MergeFallbackServices(GetAllIPluginArray(), null);");
        await Verify(generatedSource);
    }

    /// <summary>CS8795: the partial accessor's implementation is generated.</summary>
    private static readonly IReadOnlySet<string> SuppressedPartialDiagnostics = new HashSet<string>(["CS8795"]);

    private const string CollectionDependenciesSource = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Extensions.DependencyInjection;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface IPlugin { }

        [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
        public class Plugin1 : IPlugin { }

        [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
        public class Plugin2 : IPlugin { }

        [IocRegister(Lifetime = ServiceLifetime.Transient)]
        public class Consumer(
            IEnumerable<IPlugin> enumerable,
            IReadOnlyList<IPlugin> list,
            IPlugin[] array,
            Lazy<IEnumerable<IPlugin>> lazyEnumerable,
            IEnumerable<Lazy<IPlugin>> lazyPlugins)
        {
            [IocInject]
            public IEnumerable<IPlugin> Injected { get; set; } = null!;
        }

        [IocContainer(IntegrateServiceProvider = {0})]
        public partial class TestContainer
        {
            public partial IEnumerable<IPlugin> GetPlugins();
        }
        """;

    [Test]
    public async Task Container_WithCollectionDependencies_IntegrateServiceProvider_MergesFallbackServices()
    {
        var source = CollectionDependenciesSource.Replace("{0}", "true");

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.Consumer(__MergeFallbackServices(GetAllIPluginArray(), null), __MergeFallbackServices(GetAllIPluginArray(), null), __MergeFallbackServices(GetAllIPluginArray(), null), new global::System.Lazy<global::System.Collections.Generic.IEnumerable<global::TestNamespace.IPlugin>>(() => __MergeFallbackServices(GetAllIPluginArray(), null)");
        await Assert.That(generatedSource).Contains("__MergeFallbackServices(GetAllLazy_IPlugin_Array(), null)");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithCollectionDependencies_WithoutIntegrateServiceProvider_CallsArrayResolvers()
    {
        var source = CollectionDependenciesSource.Replace("{0}", "false");

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source, suppressedInitialDiagnosticIds: SuppressedPartialDiagnostics);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.Consumer(GetAllIPluginArray(), GetAllIPluginArray(), GetAllIPluginArray(), new global::System.Lazy<global::System.Collections.Generic.IEnumerable<global::TestNamespace.IPlugin>>(() => GetAllIPluginArray()");
        await Assert.That(generatedSource).DoesNotContain("__MergeFallbackServices");
        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithWrapperEntries_UseSwitchStatement_DispatchesLazyAndFunc()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IPlugin)])]
            public class Plugin1 : IPlugin { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class Consumer(Lazy<IPlugin> lazy, Func<IPlugin> factory) { }

            [IocContainer(UseSwitchStatement = true)]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(generatedSource).Contains("if(serviceType == typeof(global::System.Lazy<global::TestNamespace.IPlugin>)) return _lazy_IPlugin_plugin1;");
        await Assert.That(generatedSource).Contains("if(serviceType == typeof(global::System.Func<global::TestNamespace.IPlugin>)) return _func_IPlugin_plugin1;");
        await Assert.That(generatedSource).Contains("if(serviceType == typeof(global::System.Lazy<global::TestNamespace.IPlugin>)) return true;");
        await Verify(generatedSource);
    }
}