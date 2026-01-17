namespace SourceGen.Ioc.Test.RegisterSourceGeneratorSnapshot;

/// <summary>
/// Tests for constructor selection order:
/// 1. [IocInject] marked constructor
/// 2. Primary constructor
/// 3. Constructor with most parameters
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.InjectAttribute)]
public class ConstructorSelectionTests
{
    [Test]
    public async Task ConstructorSelection_InjectAttributeMarkedConstructor_TakesPrecedence()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IDep1 { }
            public interface IDep2 { }
            public interface IDep3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep3 : IDep3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService : IService
            {
                // Constructor with most parameters (3) - should NOT be used
                public MyService(IDep1 dep1, IDep2 dep2, IDep3 dep3) { }

                // [IocInject] marked constructor (1 parameter) - should be used
                [IocInject]
                public MyService(IDep1 dep1) { }

                // Constructor with 2 parameters - should NOT be used
                public MyService(IDep1 dep1, IDep2 dep2) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ConstructorSelection_PrimaryConstructor_TakesPrecedenceOverMostParameters()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IDep1 { }
            public interface IDep2 { }
            public interface IDep3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep3 : IDep3 { }

            // Primary constructor with 1 parameter - should be used
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService(IDep1 dep1) : IService
            {
                // Constructor with most parameters (3) - should NOT be used
                public MyService(IDep1 dep1, IDep2 dep2, IDep3 dep3) : this(dep1) { }

                // Constructor with 2 parameters - should NOT be used
                public MyService(IDep1 dep1, IDep2 dep2) : this(dep1) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ConstructorSelection_MostParameters_WhenNoInjectOrPrimary()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IDep1 { }
            public interface IDep2 { }
            public interface IDep3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep3 : IDep3 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService : IService
            {
                // Constructor with 1 parameter - should NOT be used
                public MyService(IDep1 dep1) { }

                // Constructor with 2 parameters - should NOT be used
                public MyService(IDep1 dep1, IDep2 dep2) { }

                // Constructor with most parameters (3) - should be used
                public MyService(IDep1 dep1, IDep2 dep2, IDep3 dep3) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ConstructorSelection_InjectMarkedOverridesPrimary()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IDep1 { }
            public interface IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            // Primary constructor with 1 parameter - should NOT be used
            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService(IDep1 dep1) : IService
            {
                // [IocInject] marked constructor with 2 parameters - should be used
                [IocInject]
                public MyService(IDep1 dep1, IDep2 dep2) : this(dep1) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ConstructorSelection_InternalConstructorWithInject_IsUsed()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IDep1 { }
            public interface IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService : IService
            {
                // Public constructor with 1 parameter - should NOT be used
                public MyService(IDep1 dep1) { }

                // Internal constructor with [IocInject] - should be used
                [IocInject]
                internal MyService(IDep1 dep1, IDep2 dep2) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    public async Task ConstructorSelection_PrivateConstructorWithInject_IsIgnored()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService { }
            public interface IDep1 { }
            public interface IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep1 : IDep1 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton)]
            public class Dep2 : IDep2 { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            public class MyService : IService
            {
                // Public constructor with 1 parameter - should be used (most parameters among accessible)
                public MyService(IDep1 dep1) { }

                // Private constructor with [IocInject] - should be ignored (not accessible)
                [IocInject]
                private MyService(IDep1 dep1, IDep2 dep2) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<RegisterSourceGenerator>(source);
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
