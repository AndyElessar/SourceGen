using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for factory and instance registration container generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
[Category(Constants.FactoryAndInstance)]
public class FactoryAndInstanceContainerTests
{
    [Test]
    [Arguments("UnmappedGenericOverload", "public static IMyService Create<T>(T value) => new MyService();", "public static IMyService Create(IDependency dependency) => new MyService();", "IDependency")]
    [Arguments("UnmappedGenericOverloadWithServiceProvider", "public static IMyService Create<T>(T value) => new MyService();", "public static IMyService Create(IServiceProvider sp) => new MyService();", "IServiceProvider")]
    [Arguments("VoidOverload", "public static void Create(Dependency dependency) { }", "public static IMyService Create(IDependency dependency) => new MyService();", "IDependency")]
    [Arguments("ObjectKeyOverload", "public static IMyService Create<T>(T value) => new MyService();", "public static IMyService Create([ServiceKey] object key) => new MyService();", "Object")]
    public async Task Container_WithOverloadedFactory_CallsTheUsableOverload(string scenario, string unusableOverload, string usableOverload, string parameterType)
    {
        // The generated arguments (this, a concrete service, a key literal) MUST NOT make C# bind another overload than the
        // single usable one the generator resolved
        var registration = parameterType == "Object"
            ? "[assembly: IocRegisterFor<TestNamespace.MyService>(ServiceTypes = [typeof(TestNamespace.IMyService)], Key = \"main\", Factory = nameof(TestNamespace.MyServiceFactory.Create))]"
            : "[assembly: IocRegisterFor<TestNamespace.MyService>(ServiceTypes = [typeof(TestNamespace.IMyService)], Factory = nameof(TestNamespace.MyServiceFactory.Create))]";
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            {{registration}}

            namespace TestNamespace;

            public interface IDependency { }
            public interface IMyService { }

            [IocRegister<IDependency>]
            public class Dependency : IDependency { }

            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                {{unusableOverload}}
                {{usableOverload}}
            }

            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();

        var calls = result.OutputCompilation.SyntaxTrees
            .Where(static tree => tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(static invocation => invocation.Expression.ToString().EndsWith("MyServiceFactory.Create", StringComparison.Ordinal))
                .Select(invocation => result.OutputCompilation.GetSemanticModel(tree).GetSymbolInfo(invocation).Symbol as IMethodSymbol))
            .ToList();

        // Called by both the Register and the Container output
        await Assert.That(calls).Count().IsEqualTo(2).Because(scenario);
        foreach(var call in calls)
        {
            await Assert.That(call).IsNotNull().Because(scenario);
            await Assert.That(call!.IsGenericMethod).IsFalse().Because(scenario);
            await Assert.That(call.Parameters.Single().Type.Name).IsEqualTo(parameterType).Because(scenario);
        }
    }

    [Test]
    public async Task Container_WithFactoryRegistration_UsesFactoryMethod()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConnection { }

            public class Connection : IConnection
            {
                public Connection(string connectionString) { }
            }

            public static class ConnectionFactory
            {
                public static IConnection Create(IServiceProvider sp)
                {
                    return new Connection("test-connection-string");
                }
            }

            [IocRegisterFor(typeof(Connection), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IConnection)], Factory = nameof(ConnectionFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithFactoryRegisteredForServiceTypeOnly_ResolvesFactoryResultAsServiceType()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConnection { }

            public class PooledConnection : IConnection { }

            public class DirectConnection : IConnection { }

            public static class ConnectionFactory
            {
                public static IConnection Create(IServiceProvider sp) =>
                    Environment.ProcessorCount > 1 ? new PooledConnection() : new DirectConnection();
            }

            [IocRegisterFor<IConnection>(ServiceLifetime.Singleton, Factory = nameof(ConnectionFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithInstanceRegistration_UsesStaticInstance()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IConfiguration { }

            public class Configuration : IConfiguration
            {
                public static readonly Configuration Default = new();
            }

            [IocRegisterFor(typeof(Configuration), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IConfiguration)], Instance = nameof(Configuration.Default))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithFactoryServiceProviderAndKeyAfterDependencies_PassesArgumentsInDeclarationOrder()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface ILogger { }

            [IocRegister<ILogger>(ServiceLifetime.Singleton)]
            public class Logger : ILogger { }

            public interface IConnection { }

            public class Connection : IConnection { }

            public static class ConnectionFactory
            {
                public static IConnection Create(ILogger logger, IServiceProvider sp, [ServiceKey] string key)
                    => new Connection();
            }

            [IocRegisterFor(typeof(Connection), Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IConnection)], Key = "main", Factory = nameof(ConnectionFactory.Create))]
            [IocContainer]
            public partial class TestContainer { }
            """;

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }
}
