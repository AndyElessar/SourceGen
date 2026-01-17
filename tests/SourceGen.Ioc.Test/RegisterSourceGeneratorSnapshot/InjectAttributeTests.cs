namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for InjectAttribute functionality.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.InjectAttribute)]
public class InjectAttributeTests
{
    [Test]
    public async Task InjectAttribute_PropertyInjection_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency1 { }
            public interface IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IDependency1 Dependency1 { get; init; }

                [IocInject]
                public IDependency2 Dependency2 { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency1 { }
            public interface IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency1? Dep1 { get; private set; }
                public IDependency2? Dep2 { get; private set; }

                [IocInject]
                public void Initialize(IDependency1 dep1, IDependency2 dep2)
                {
                    Dep1 = dep1;
                    Dep2 = dep2;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MixedInjection_GeneratesFactoryMethod()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency1 { }
            public interface IDependency2 { }
            public interface IDependency3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency3 : IDependency3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService(IDependency1 dep1) : IMyService
            {
                private readonly IDependency1 _dep1 = dep1;

                [IocInject]
                public IDependency2 Dep2 { get; init; }

                [IocInject]
                public void Initialize(IDependency3 dep3)
                {
                    // Initialization
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_WithKeyedService_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject(Key = "special")]
                public IDependency Dependency { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_WithCsharpKeyedService_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
            public class SpecialDependency : IDependency
            {
                public const string Key = "special";
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject(KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
                public IDependency Dependency { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_ConstructorParameter_WithKeyedService_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class DefaultDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([IocInject(Key = "special")] IDependency dep) : IMyService
            {
                public IDependency Dependency { get; } = dep;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_ConstructorParameter_WithCsharpKey_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
            public class SpecialDependency : IDependency
            {
                public const string Key = "special";
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([IocInject(KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))] IDependency dep) : IMyService
            {
                public IDependency Dependency { get; } = dep;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodParameter_WithKeyedService_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class DefaultDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency? Dependency { get; private set; }

                [IocInject]
                public void Initialize([IocInject(Key = "special")] IDependency dep)
                {
                    Dependency = dep;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodParameter_WithCsharpKey_GeneratesKeyedServiceResolution()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
            public class SpecialDependency : IDependency
            {
                public const string Key = "special";
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency? Dependency { get; private set; }

                [IocInject]
                public void Initialize([IocInject(KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))] IDependency dep)
                {
                    Dependency = dep;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_OptionalProperty_GeneratesGetService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IDependency? OptionalDependency { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_OptionalPropertyWithKey_GeneratesGetKeyedService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject(Key = "special")]
                public IDependency? OptionalDependency { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_OptionalConstructorParameter_GeneratesGetService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService(IDependency? optionalDep = null) : IMyService
            {
                public IDependency? OptionalDependency { get; } = optionalDep;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_OptionalConstructorParameterWithKey_GeneratesGetKeyedService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([IocInject(Key = "special")] IDependency? optionalDep = null) : IMyService
            {
                public IDependency? OptionalDependency { get; } = optionalDep;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_OptionalMethodParameter_GeneratesGetService()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency? OptionalDependency { get; private set; }

                [IocInject]
                public void Initialize(IDependency? optionalDep = null)
                {
                    OptionalDependency = optionalDep;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceKeyAttribute_MethodParameter_KeyedService_InjectsKey()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "MyKey")]
            public class MyService : IMyService
            {
                public string? Key { get; private set; }

                [IocInject]
                public void Initialize([ServiceKey] string key)
                {
                    Key = key;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceKeyAttribute_InMethodWithExplicitKey_InjectsRegistrationKey()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Key = "dep1")]
            public class Dependency1 : IDependency { }

            [IocRegister(Key = "ServiceKey")]
            public class MyService : IMyService
            {
                public IDependency? Dep { get; private set; }
                public string? Key { get; private set; }

                [IocInject(Key = "dep1")]
                public void Initialize(IDependency dep, [ServiceKey] string key)
                {
                    Dep = dep;
                    Key = key;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ServiceKeyAttribute_MethodParameter_NonKeyedService_InjectsNull()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister]
            public class MyService : IMyService
            {
                public string? Key { get; private set; }

                [IocInject]
                public void Initialize([ServiceKey] string? key)
                {
                    Key = key;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_ThirdPartyAttribute_GeneratesFactoryMethod()
    {
        // Test that InjectAttribute from other libraries (by name only) also works
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            // Simulating Microsoft.AspNetCore.Components.InjectAttribute
            /// <summary>
            /// Indicates that the associated property should have a value injected from the
            /// service provider during initialization.
            /// </summary>
            [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
            public sealed class InjectAttribute : Attribute
            {
                /// <summary>
                /// Gets or sets the object that specifies the key of the service to inject.
                /// </summary>
                public object? Key { get; init; }
            }

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject(Key = "TestKey")]
                public IDependency Dependency { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_PropertyInjection_IListCollection_NotRecognizedAsCollection()
    {
        // IList<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IList<IDependency> Dependencies { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_PropertyInjection_ArrayCollection_GeneratesGetServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IDependency[] Dependencies { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_PropertyInjection_IListCollectionWithKey_NotRecognizedAsCollection()
    {
        // IList<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject(Key = "special")]
                public IList<IDependency> Dependencies { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_IListCollection_NotRecognizedAsCollection()
    {
        // IList<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IList<IDependency>? Dependencies { get; private set; }

                [IocInject]
                public void Initialize(IList<IDependency> dependencies)
                {
                    Dependencies = dependencies;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_IListCollectionWithMethodKey_NotRecognizedAsCollection()
    {
        // IList<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IList<IDependency>? Dependencies { get; private set; }

                [IocInject(Key = "special")]
                public void Initialize(IList<IDependency> dependencies)
                {
                    Dependencies = dependencies;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_ArrayCollectionWithMethodKey_GeneratesGetKeyedServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency[]? Dependencies { get; private set; }

                [IocInject(Key = "special")]
                public void Initialize(IDependency[] dependencies)
                {
                    Dependencies = dependencies;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_PropertyInjection_IEnumerableCollection_GeneratesGetServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IEnumerable<IDependency> Dependencies { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_PropertyInjection_IEnumerableCollectionWithKey_GeneratesGetKeyedServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject(Key = "special")]
                public IEnumerable<IDependency> Dependencies { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_IEnumerableCollection_GeneratesGetServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IEnumerable<IDependency>? Dependencies { get; private set; }

                [IocInject]
                public void Initialize(IEnumerable<IDependency> dependencies)
                {
                    Dependencies = dependencies;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_IEnumerableCollectionWithMethodKey_GeneratesGetKeyedServices()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IEnumerable<IDependency>? Dependencies { get; private set; }

                [IocInject(Key = "special")]
                public void Initialize(IEnumerable<IDependency> dependencies)
                {
                    Dependencies = dependencies;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_PropertyInjection_IReadOnlyListCollection_GeneratesGetServicesToArray()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [IocInject]
                public IReadOnlyList<IDependency> Dependencies { get; init; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task InjectAttribute_MethodInjection_IReadOnlyCollectionWithMethodKey_GeneratesGetKeyedServicesToArray()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using System.Collections.Generic;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency1 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class Dependency2 : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IReadOnlyCollection<IDependency>? Dependencies { get; private set; }

                [IocInject(Key = "special")]
                public void Initialize(IReadOnlyCollection<IDependency> dependencies)
                {
                    Dependencies = dependencies;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
