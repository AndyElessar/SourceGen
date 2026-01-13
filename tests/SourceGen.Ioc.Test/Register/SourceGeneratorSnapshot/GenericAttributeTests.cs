namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

/// <summary>
/// Tests for generic attribute variants (IoCRegisterAttribute&lt;T&gt;, IoCRegisterForAttribute&lt;T&gt;, etc.).
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

            [IoCRegister<IMyService>(ServiceLifetime.Singleton)]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterAttribute_T2_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            [IoCRegister<IFirst, ISecond>(ServiceLifetime.Scoped)]
            public class MultiService : IFirst, ISecond { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterAttribute_T3_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }
            public interface IThird { }

            [IoCRegister<IFirst, ISecond, IThird>(ServiceLifetime.Transient)]
            public class TripleService : IFirst, ISecond, IThird { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterAttribute_T4_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }
            public interface IThird { }
            public interface IFourth { }

            [IoCRegister<IFirst, ISecond, IThird, IFourth>(ServiceLifetime.Singleton)]
            public class QuadService : IFirst, ISecond, IThird, IFourth { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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

            [IoCRegister<IMyService>(ServiceLifetime.Singleton, Key = "mykey")]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region IoCRegisterForAttribute<T>

    [Test]
    public async Task IoCRegisterForAttribute_T1_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterFor<TestNamespace.ExternalService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IExternalService)])]

            namespace TestNamespace;

            public interface IExternalService { }
            public class ExternalService : IExternalService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterForAttribute_T1_OnClass_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IExternalService { }
            public class ExternalService : IExternalService { }

            [IoCRegisterFor<ExternalService>(ServiceLifetime.Scoped, ServiceTypes = [typeof(IExternalService)])]
            public sealed class Module;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region IoCRegisterDefaultsAttribute<T>

    [Test]
    public async Task IoCRegisterDefaultsAttribute_T1_AppliesCorrectLifetime()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults<TestNamespace.IBaseService>(ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IBaseService { }

            [IoCRegister]
            public class MyService : IBaseService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterDefaultsAttribute_T1_WithDecorators_AppliesDecorators()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults<TestNamespace.IMyService>(
                ServiceLifetime.Singleton,
                Decorators = [typeof(TestNamespace.MyServiceDecorator)])]

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }

            public class MyServiceDecorator(IMyService inner) : IMyService
            {
                private readonly IMyService _inner = inner;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region ImportModuleAttribute<T>

    [Test]
    public async Task ImportModuleAttribute_T1_ImportsDefaultSettings()
    {
        // Create a "shared" assembly with IoCRegisterDefaults on an interface
        const string sharedSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace SharedModule;

            [IoCRegisterDefaults(typeof(ISharedService), ServiceLifetime.Scoped)]
            public interface ISharedService { }
            """;

        var sharedCompilation = SourceGeneratorTestHelper.CreateCompilation("SharedModule", sharedSource);

        // Now create the main assembly that imports from the shared module using generic attribute
        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using SharedModule;

            namespace MainApp;

            [ImportModule<ISharedService>]
            public sealed class Module;

            [IoCRegister]
            public sealed class MyService : ISharedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(mainSource, "MainApp", [sharedCompilation.ToMetadataReference()]);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion

    #region DiscoverAttribute<T>

    [Test]
    public async Task DiscoverAttribute_T1_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class GenericHandler<T> : IHandler<T> { }

            public class TestEntity { }

            // Using generic DiscoverAttribute
            [Discover<IHandler<TestEntity>>]
            public sealed class Startup;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_T1_OnMethod_GeneratesClosedGenericRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class GenericHandler<T> : IHandler<T> { }

            public class TestEntity { }

            public class Startup
            {
                [Discover<IHandler<TestEntity>>]
                public void Configure() { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task DiscoverAttribute_T1_MultipleDiscoverAttributes_GeneratesAllFactories()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IHandler<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IHandler<>)])]
            public class GenericHandler<T> : IHandler<T> { }

            public class Entity1 { }
            public class Entity2 { }
            public class Entity3 { }

            [Discover<IHandler<Entity1>>]
            [Discover<IHandler<Entity2>>]
            [Discover<IHandler<Entity3>>]
            public sealed class Startup;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
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
            public interface IThird { }

            // Non-generic version
            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFirst)])]
            public class Service1 : IFirst { }

            // Generic version with 1 type parameter
            [IoCRegister<ISecond>(ServiceLifetime.Scoped)]
            public class Service2 : ISecond { }

            // Generic version with 2 type parameters
            [IoCRegister<IFirst, IThird>(ServiceLifetime.Transient)]
            public class Service3 : IFirst, IThird { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    #endregion
}
