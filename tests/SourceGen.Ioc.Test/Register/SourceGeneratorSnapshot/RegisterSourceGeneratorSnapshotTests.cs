namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for RegisterSourceGenerator.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
public partial class RegisterSourceGeneratorSnapshotTests
{
    [Test]
    public async Task SimpleService_Singleton_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task SimpleService_Scoped_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IScopedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
            public class ScopedService : IScopedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task SimpleService_Transient_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ITransientService { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
            public class TransientService : ITransientService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task RegisterAllInterfaces_GeneratesMultipleRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFirst { }
            public interface ISecond { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllInterfaces = true)]
            public class MultiInterfaceService : IFirst, ISecond { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithStringKey_GeneratesKeyedRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "myKey")]
            public class KeyedService : IKeyedService { }

            public enum MyEnum
            {
                Key1 = 0,
                Key2 = 1
            }
            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = MyEnum.Key1)]
            public class KeyedService2 : IKeyedService { }

            public static class KeyHolder
            {
                public const int IntKey = 42;
            }
            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = KeyHolder.IntKey)]
            public class KeyedService3 : IKeyedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithCsharpKey_GeneratesKeyedRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IKeyedService { }

            public class KeyHolder
            {
                public static readonly Guid Key = Guid.NewGuid();
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], KeyType = KeyType.Csharp, Key = "KeyHolder.Key")]
            public class KeyedService : IKeyedService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], KeyType = KeyType.Csharp, Key = nameof(KeyHolder.Key))]
            public class KeyedService2 : IKeyedService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task OpenGenericService_GeneratesTypeOfRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<T> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task OpenGenericService_DoNotGenerateNestedOpenGeneric()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            public interface IGeneric<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
            public class Repository<T> : IRepository<IGeneric<T>> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task OpenGenericService_DoNotGenerateNestedOpenGeneric_WithRegisterAllInterfaces()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRepository<T> { }

            public interface IGeneric<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, RegisterAllInterfaces = true)]
            public class Repository<T> : IRepository<IGeneric<T>> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IoCRegisterForAttribute_OnAssembly_GeneratesRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterFor(typeof(TestNamespace.ExternalService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IExternalService)])]

            namespace TestNamespace;

            public interface IExternalService { }
            public class ExternalService : IExternalService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task MultipleServices_GeneratesAllRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IServiceA { }
            public interface IServiceB { }
            public interface IServiceC { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA { }

            [IoCRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB { }

            [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IServiceC)])]
            public class ServiceC : IServiceC { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NoServices_GeneratesNoOutput()
    {
        const string source = """
            namespace TestNamespace;

            public interface IMyService { }
            public class MyService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task RegisterAllBaseClasses_GeneratesBaseClassRegistrations()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public abstract class BaseService { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllBaseClasses = true)]
            public class DerivedService : BaseService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedOpenGeneric_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ICache<T> { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ICache<>)], Key = "redis")]
            public class RedisCache<T> : ICache<T> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NestedClass_GeneratesCorrectFullyQualifiedName()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public class OuterClass
            {
                public interface INestedService { }

                [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(INestedService)])]
                public class NestedService : INestedService { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task DefaultSettings_AppliesCorrectLifetime()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IBaseService), ServiceLifetime.Scoped)]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface ISpecificService : IBaseService { }

            [IoCRegister(ServiceTypes = [typeof(ISpecificService)])]
            public class SpecificService : ISpecificService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Category(Constants.Defaults)]
    public async Task DefaultSettings_GenericArity_ShouldNotMatchDifferentArity()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IoCRegisterDefaults(typeof(TestNamespace.IGenericService<>), ServiceLifetime.Scoped, ServiceTypes = [typeof(TestNamespace.IBaseService)])]

            namespace TestNamespace;

            public interface IBaseService { }
            public interface IOtherService { }
            public interface IGenericService<T> : IBaseService { }
            public interface IGenericService<T1, T2> : IOtherService { }

            // This should match IGenericService<> default settings (arity 1)
            [IoCRegister]
            public class SingleArityService<T> : IGenericService<T> { }

            // This should NOT match IGenericService<> default settings (arity 2 != 1)
            // Should use default lifetime (Singleton) instead
            [IoCRegister]
            public class DoubleArityService<T1, T2> : IGenericService<T1, T2> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
