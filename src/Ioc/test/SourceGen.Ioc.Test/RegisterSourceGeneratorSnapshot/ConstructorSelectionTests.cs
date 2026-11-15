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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }

    [Test]
    [Arguments("Internal", "internal", true)]
    [Arguments("ProtectedInternal", "protected internal", true)]
    [Arguments("Public", "public", false)]
    public async Task ConstructorSelection_WithoutPublicConstructor_ConstructsInRegisterOutput(string scenario, string accessibility, bool constructs)
    {
        // MS.DI activates a type registration with a public constructor only
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService;
            public interface IDep;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IDep)])]
            internal sealed class Dep : IDep;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            internal sealed class MyService : IService
            {
                {{accessibility}} MyService(IDep dep) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource!.Contains("new global::TestNamespace.MyService(")).IsEqualTo(constructs);
    }

    [Test]
    public async Task ConstructorSelection_InternalConstructorSelectedOverPublic_ConstructsInRegisterOutput()
    {
        // MS.DI would call the public constructor instead of the one the container calls
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService;
            public interface IDep;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IDep)])]
            internal sealed class Dep : IDep;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]
            internal sealed class MyService : IService
            {
                public MyService() { }
                internal MyService(IDep dep) { }
            }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Assert.That(generatedSource).Contains("new global::TestNamespace.MyService(p0)");
    }

    [Test]
    [Arguments("NoInternalsVisibleTo", "", false)]
    [Arguments("InternalsVisibleTo", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"MainAssembly\")]", true)]
    public async Task ConstructorSelection_InternalConstructorInReferencedAssembly_SelectedOnlyWhenAccessible(string scenario, string internalsVisibleTo, bool selectsInternal)
    {
        var librarySource = $$"""
            {{internalsVisibleTo}}

            namespace Library;

            public interface IService;
            public interface IDep;

            public sealed class MyService : IService
            {
                public MyService() { }
                protected internal MyService(IDep dep) { }
            }
            """;

        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using Library;

            [assembly: IocRegisterFor<MyService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]

            namespace MainApp;

            [IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IDep)])]
            internal sealed class Dep : IDep;

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithDependencies<IocSourceGenerator>(
            (librarySource, "Library"),
            (mainSource, "MainAssembly"));
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Assert.That(containerSource!.Contains("new global::Library.MyService()")).IsEqualTo(!selectsInternal);
        await Assert.That(containerSource.Contains("new global::Library.MyService(Get")).IsEqualTo(selectsInternal);

        // MS.DI calls the public constructor itself; the internal one is called by generated code
        await Assert.That(generatedSource!.Contains("new global::Library.MyService(p0)")).IsEqualTo(selectsInternal);
    }

    [Test]
    [Arguments("PrivateConstructor", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])] public sealed class MyService : IService { private MyService() { } }")]
    [Arguments("OpenPrivateConstructor", "[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService<>)])] public sealed class MyService<T> : IService<T> { private MyService() { } }")]
    public async Task ConstructorSelection_WithoutAccessibleConstructorOrFactory_DoesNotRegister(string scenario, string registration)
    {
        // Generated code cannot construct the type without a Factory or Instance (SGIOC001)
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IService;
            public interface IService<T>;

            {{registration}}

            [IocRegister]
            internal sealed class Consumer(IService<int>? service = null);

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        foreach(var output in new[] { generatedSource ?? "", containerSource ?? "" })
        {
            await Assert.That(output).DoesNotContain("global::TestNamespace.MyService");
        }
    }

    [Test]
    [Arguments("NoInternalsVisibleTo", "", false)]
    [Arguments("InternalsVisibleTo", "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"MainAssembly\")]", true)]
    public async Task ConstructorSelection_OnlyInternalConstructorInReferencedAssembly_RegistersOnlyWhenAccessible(string scenario, string internalsVisibleTo, bool registers)
    {
        var librarySource = $$"""
            {{internalsVisibleTo}}

            namespace Library;

            public interface IService;

            public sealed class MyService : IService
            {
                internal MyService() { }
            }
            """;

        const string mainSource = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using Library;

            [assembly: IocRegisterFor<MyService>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IService)])]

            namespace MainApp;

            [IocContainer]
            public partial class TestContainer;
            """;

        var result = SourceGeneratorTestHelper.RunGeneratorWithDependencies<IocSourceGenerator>(
            (librarySource, "Library"),
            (mainSource, "MainAssembly"));
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");
        var containerSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        foreach(var output in new[] { generatedSource ?? "", containerSource ?? "" })
        {
            await Assert.That(output.Contains("global::Library.MyService")).IsEqualTo(registers);
        }
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
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

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration");

        await Verify(generatedSource);
    }
}
