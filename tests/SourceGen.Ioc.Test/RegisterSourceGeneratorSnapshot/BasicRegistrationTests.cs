namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Basic registration tests for RegisterSourceGenerator.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.BasicRegistration)]
public class BasicRegistrationTests
{
    [Test]
    public async Task SimpleService_Singleton_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
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

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IScopedService)])]
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

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITransientService)])]
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

            [IocRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllInterfaces = true)]
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

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = "myKey")]
            public class KeyedService : IKeyedService { }

            public enum MyEnum
            {
                Key1 = 0,
                Key2 = 1
            }
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = MyEnum.Key1)]
            public class KeyedService2 : IKeyedService { }

            public static class KeyHolder
            {
                public const int IntKey = 42;
            }
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], Key = KeyHolder.IntKey)]
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

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], KeyType = KeyType.Csharp, Key = "KeyHolder.Key")]
            public class KeyedService : IKeyedService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IKeyedService)], KeyType = KeyType.Csharp, Key = nameof(KeyHolder.Key))]
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

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
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

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IRepository<>)])]
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

            [IocRegister(Lifetime = ServiceLifetime.Scoped, RegisterAllInterfaces = true)]
            public class Repository<T> : IRepository<IGeneric<T>> { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IocRegisterForAttribute_OnAssembly_GeneratesRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            [assembly: IocRegisterFor(typeof(TestNamespace.ExternalService), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(TestNamespace.IExternalService)])]

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

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IServiceA)])]
            public class ServiceA : IServiceA { }

            [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IServiceB)])]
            public class ServiceB : IServiceB { }

            [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(IServiceC)])]
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

            [IocRegister(Lifetime = ServiceLifetime.Singleton, RegisterAllBaseClasses = true)]
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

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(ICache<>)], Key = "redis")]
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

                [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(INestedService)])]
                public class NestedService : INestedService { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithCsharpKeyNameofInNestedClass_GeneratesKeyedRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            internal sealed class OuterClass
            {
                public interface INestedService { }

                [IocRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(INestedService)], KeyType = KeyType.Csharp, Key = nameof(Key))]
                internal sealed class NestedServiceImpl : INestedService
                {
                    public const string Key = "NestedKey";
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithCsharpKeyNameofInDeeplyNestedClass_GeneratesKeyedRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            internal sealed class Level1
            {
                internal sealed class Level2
                {
                    public interface IDeepNestedService { }

                    [IocRegister(Lifetime = ServiceLifetime.Scoped, ServiceTypes = [typeof(IDeepNestedService)], KeyType = KeyType.Csharp, Key = nameof(ServiceKey))]
                    internal sealed class DeepNestedServiceImpl : IDeepNestedService
                    {
                        public const string ServiceKey = "DeepKey";
                    }
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithCsharpKeyNameofExternalMember_GeneratesKeyedRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public static class Keys
            {
                public const string SharedKey = "Shared";
            }

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], KeyType = KeyType.Csharp, Key = nameof(Keys.SharedKey))]
            public class MyServiceImpl : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceTypes_AsConstructorArguments_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IBasic { }
            public interface IBasic2 { }

            [IocRegister(ServiceLifetime.Transient, typeof(IBasic), typeof(IBasic2))]
            public class Basic2 : IBasic, IBasic2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceTypes_AsConstructorArguments_WithoutLifetime_GeneratesCorrectRegistration()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService1 { }
            public interface IService2 { }

            [IocRegister(typeof(IService1), typeof(IService2))]
            public class MultiService : IService1, IService2 { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceTypes_MixedConstructorAndNamedArguments_NamedArgumentTakesPrecedence()
    {
        // When both constructor arguments and named arguments specify service types,
        // the named argument should take precedence (as it is more explicit).
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IPreferred { }
            public interface IIgnored { }

            [IocRegister(ServiceLifetime.Singleton, typeof(IIgnored), ServiceTypes = [typeof(IPreferred)])]
            public class ServiceWithMixedArgs : IPreferred, IIgnored { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
