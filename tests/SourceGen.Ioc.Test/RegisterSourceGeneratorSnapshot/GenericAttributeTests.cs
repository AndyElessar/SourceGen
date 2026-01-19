namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for generic attribute variants (IoCRegisterAttribute&lt;T&gt;, IocRegisterForAttribute&lt;T&gt;, etc.).
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.BasicRegistration)]
public class GenericAttributeTests
{
    #region IoCRegisterAttribute<T> variants

    [Test]
    public async Task IoCRegisterAttribute_T1_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterAttribute_T1_WithKey_GeneratesKeyedRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister<IMyService>(ServiceLifetime.Singleton, Key = "mykey")]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region IocRegisterForAttribute<T>

    [Test]
    public async Task IocRegisterForAttribute_T1_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor<TestNamespace.ExternalService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IExternalService)])]

            namespace TestNamespace;

            public interface IExternalService { }
            public class ExternalService : IExternalService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocRegisterForAttribute_T1_OnClass_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IExternalService { }
            public class ExternalService : IExternalService { }

            [IocRegisterFor<ExternalService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IExternalService)])]
            public sealed class Module;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region IocRegisterDefaultsAttribute<T>

    [Test]
    public async Task IocRegisterDefaultsAttribute_T1_AppliesCorrectLifetime()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IBaseService>(ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IBaseService { }

            [IocRegister]
            public class MyService : IBaseService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocRegisterDefaultsAttribute_T1_WithDecorators_AppliesDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterDefaults<TestNamespace.IMyService>(
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.MyServiceDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region IocImportModuleAttribute<T>

    [Test]
    public async Task IocImportModuleAttribute_T1_ImportsDefaultSettings()
    {
        // Create a "shared" assembly with IocRegisterDefaults on an interface
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            [IocRegisterDefaults(typeof(ISharedService), ServiceLifetime.Scoped)]
            public interface ISharedService { }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Now create the main assembly that imports from the shared module using generic attribute
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;

            namespace MainApp;

            [IocImportModule<ISharedService>]
            public sealed class Module;

            [IocRegister]
            public sealed class MyService : ISharedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region IocDiscoverAttribute<T>

    [Test]
    public async Task IocDiscoverAttribute_T1_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class GenericHandler<T> : IHandler<T> { }

            public class TestEntity { }

            // Using generic IocDiscoverAttribute
            [IocDiscover<IHandler<TestEntity>>]
            public sealed class Startup;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_T1_OnMethod_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class GenericHandler<T> : IHandler<T> { }

            public class TestEntity { }

            public class Startup
            {
                [IocDiscover<IHandler<TestEntity>>]
                public void Configure() { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocDiscoverAttribute_T1_MultipleIocDiscoverAttributes_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class GenericHandler<T> : IHandler<T> { }

            public class Entity1 { }
            public class Entity2 { }
            public class Entity3 { }

            [IocDiscover<IHandler<Entity1>>]
            [IocDiscover<IHandler<Entity2>>]
            [IocDiscover<IHandler<Entity3>>]
            public sealed class Startup;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region Mixed generic and non-generic

    [Test]
    public async Task MixedAttributes_GenericAndNonGeneric_BothWork()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            // Non-generic version
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFirst)])]
            public class Service1 : IFirst { }

            // Generic version with 1 type parameter
            [IocRegister<ISecond>(ServiceLifetime.Scoped)]
            public class Service2 : ISecond { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion
}
