namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for special parameter handling in factory methods:
/// - IServiceProvider parameters
/// - [FromKeyedServices] attribute
/// - [ServiceKey] attribute
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.SpecialParameter)]
public class SpecialParameterTests
{
    [Test]
    public async Task IServiceProvider_InConstructor_PassesDirectly()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService(IServiceProvider serviceProvider, IDependency dependency) : IMyService
            {
                private readonly IServiceProvider _serviceProvider = serviceProvider;
                private readonly IDependency _dependency = dependency;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IServiceProvider_InMethodInjection_PassesDirectly()
    {
        const string source = """
            using System;
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
                public IServiceProvider? ServiceProvider { get; private set; }
                public IDependency? Dependency { get; private set; }

                [IocInject]
                public void Initialize(IServiceProvider serviceProvider, IDependency dependency)
                {
                    ServiceProvider = serviceProvider;
                    Dependency = dependency;
                }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_InConstructor_UsesSpecifiedKey()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "special")]
            public class SpecialDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "normal")]
            public class NormalDependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService(
                [FromKeyedServices("special")] IDependency specialDep,
                [FromKeyedServices("normal")] IDependency normalDep) : IMyService
            {
                private readonly IDependency _specialDep = specialDep;
                private readonly IDependency _normalDep = normalDep;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithServiceKeyAttribute_PassesKey()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Key = "myServiceKey")]
            public class MyService([ServiceKey] object key, IDependency dependency) : IMyService
            {
                private readonly object _key = key;
                private readonly IDependency _dependency = dependency;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithNullableServiceKeyAttribute_PassesKey()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency : IDependency { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Key = "myServiceKey")]
            public class MyService([ServiceKey] object? serviceKey, IDependency dependency) : IMyService
            {
                private readonly object? _serviceKey = serviceKey;
                private readonly IDependency _dependency = dependency;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task KeyedService_WithUnresolvableObjectParameter_SkipsRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IObjectService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IObjectService)])]
            public class ObjectService : IObjectService { }

            // This service should be skipped because 'object someData' cannot be resolved from DI
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Key = "myServiceKey")]
            public class MyService(object someData) : IMyService
            {
                private readonly object _someData = someData;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task NonKeyedService_WithUnresolvableObjectParameter_SkipsRegistration()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IObjectService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IObjectService)])]
            public class ObjectService : IObjectService { }

            // This service should be skipped because 'object key' without [ServiceKey] cannot be resolved from DI
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)])]
            public class MyService(object key) : IMyService
            {
                private readonly object _key = key;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task CombinedSpecialParameters_AllHandledCorrectly()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface IDependency1 { }
            public interface IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "dep1")]
            public class Dependency1 : IDependency1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dependency2 : IDependency2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Key = "myService")]
            public class MyService(
                IServiceProvider serviceProvider,
                [FromKeyedServices("dep1")] IDependency1 dep1,
                IDependency2 dep2,
                [ServiceKey] object key) : IMyService
            {
                private readonly IServiceProvider _serviceProvider = serviceProvider;
                private readonly IDependency1 _dep1 = dep1;
                private readonly IDependency2 _dep2 = dep2;
                private readonly object _key = key;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task IServiceProvider_InDecorator_PassesDirectly()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class MyServiceImpl : IMyService { }

            public class MyServiceDecorator(IMyService inner, IServiceProvider serviceProvider) : IMyService
            {
                private readonly IMyService _inner = inner;
                private readonly IServiceProvider _serviceProvider = serviceProvider;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class DecoratedService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_InDecorator_UsesSpecifiedKey()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }
            public interface ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, Key = "decoratorLogger")]
            public class DecoratorLogger : ILogger { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class MyServiceImpl : IMyService { }

            public class MyServiceDecorator(IMyService inner, [FromKeyedServices("decoratorLogger")] ILogger logger) : IMyService
            {
                private readonly IMyService _inner = inner;
                private readonly ILogger _logger = logger;
            }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IMyService)], Decorators = [typeof(MyServiceDecorator)])]
            public class DecoratedService : IMyService { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_IEnumerableCollection_InConstructor_HandledByMsDi()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
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
            public class MyService([FromKeyedServices("special")] IEnumerable<IDependency> dependencies) : IMyService
            {
                private readonly IEnumerable<IDependency> _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_IListCollection_InConstructor_NotRecognizedAsCollection()
    {
        // IList<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using System;
            using System.Collections.Generic;
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
            public class MyService([FromKeyedServices("special")] IList<IDependency> dependencies) : IMyService
            {
                private readonly IList<IDependency> _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_IReadOnlyListCollection_InConstructor_UsesGetKeyedServicesToArray()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
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
            public class MyService([FromKeyedServices("special")] IReadOnlyList<IDependency> dependencies) : IMyService
            {
                private readonly IReadOnlyList<IDependency> _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_ArrayCollection_InConstructor_UsesGetKeyedServicesToArray()
    {
        const string source = """
            using System;
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
            public class MyService([FromKeyedServices("special")] IDependency[] dependencies) : IMyService
            {
                private readonly IDependency[] _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_IReadOnlyCollectionCollection_InConstructor_UsesGetKeyedServicesToArray()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
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
            public class MyService([FromKeyedServices("special")] IReadOnlyCollection<IDependency> dependencies) : IMyService
            {
                private readonly IReadOnlyCollection<IDependency> _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_ListCollection_InConstructor_NotRecognizedAsCollection()
    {
        // List<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using System;
            using System.Collections.Generic;
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
            public class MyService([FromKeyedServices("special")] List<IDependency> dependencies) : IMyService
            {
                private readonly List<IDependency> _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task FromKeyedServices_ICollectionCollection_InConstructor_NotRecognizedAsCollection()
    {
        // ICollection<T> is NOT recognized as a collection type for special injection handling
        // Only IEnumerable<T>, IReadOnlyCollection<T>, IReadOnlyList<T>, and T[] are supported
        const string source = """
            using System;
            using System.Collections.Generic;
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
            public class MyService([FromKeyedServices("special")] ICollection<IDependency> dependencies) : IMyService
            {
                private readonly ICollection<IDependency> _dependencies = dependencies;
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
