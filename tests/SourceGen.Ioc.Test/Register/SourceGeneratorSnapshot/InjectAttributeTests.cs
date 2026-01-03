namespace SourceGen.Ioc.Test.Register.SourceGeneratorSnapshot;

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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [Inject]
                public IDependency1 Dependency1 { get; init; }

                [Inject]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency1? Dep1 { get; private set; }
                public IDependency2? Dep2 { get; private set; }

                [Inject]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency1 : IDependency1 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency3 : IDependency3 { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService(IDependency1 dep1) : IMyService
            {
                private readonly IDependency1 _dep1 = dep1;

                [Inject]
                public IDependency2 Dep2 { get; init; }

                [Inject]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [Inject(Key = "special")]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
            public class SpecialDependency : IDependency
            {
                public const string Key = "special";
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [Inject(KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class DefaultDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([Inject(Key = "special")] IDependency dep) : IMyService
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
            public class SpecialDependency : IDependency
            {
                public const string Key = "special";
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([Inject(KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))] IDependency dep) : IMyService
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class DefaultDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency? Dependency { get; private set; }

                [Inject]
                public void Initialize([Inject(Key = "special")] IDependency dep)
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))]
            public class SpecialDependency : IDependency
            {
                public const string Key = "special";
            }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency? Dependency { get; private set; }

                [Inject]
                public void Initialize([Inject(KeyType = KeyType.Csharp, Key = nameof(SpecialDependency.Key))] IDependency dep)
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [Inject]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [Inject(Key = "special")]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService([Inject(Key = "special")] IDependency? optionalDep = null) : IMyService
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                public IDependency? OptionalDependency { get; private set; }

                [Inject]
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

            [IoCRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IoCRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService : IMyService
            {
                [Inject(Key = "TestKey")]
                public IDependency Dependency { get; set; }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
