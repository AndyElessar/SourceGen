using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC046: a container partial accessor returns a Func with input parameters that the container does not
/// construct, because a decorator applied to the service type of the Instance registration it resolves has async inject methods.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC046)]
public class SGIOC046Tests
{
    private const string AsyncFeatures = "Register,Container,MethodInject,AsyncMethodInject";

    private static string CreateSource(
        string accessor,
        string decorator = "AsyncDecorator",
        string containerArguments = "IntegrateServiceProvider = false",
        string before = "",
        string after = "",
        string containerAttributes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using SourceGen.Ioc;

        namespace TestNamespace;

        public interface IMyService { }

        {{before}}

        [IocRegister(ServiceTypes = [typeof(IMyService), typeof(MyService)], Instance = nameof(Default), Decorators = [typeof({{decorator}})])]
        public class MyService(string name) : IMyService
        {
            public static readonly MyService Default = new("");
        }

        {{after}}

        public class AsyncDecorator(IMyService inner) : IMyService
        {
            [IocInject]
            public Task InitializeAsync() => Task.CompletedTask;
        }

        public class SyncDecorator(IMyService inner) : IMyService;

        {{containerAttributes}}
        [IocContainer({{containerArguments}})]
        public partial class TestContainer
        {
            {{accessor}}
        }
        """;

    private static Task<ImmutableArray<Diagnostic>> RunAsync<TAnalyzer>(string source, string features = AsyncFeatures)
        where TAnalyzer : DiagnosticAnalyzer, new()
        => SourceGeneratorTestHelper.RunAnalyzerAsync<TAnalyzer>(
            source,
            analyzerConfigOptions: new Dictionary<string, string> { ["build_property.SourceGenIocFeatures"] = features });

    [Test]
    [Arguments("public partial Func<string, IMyService> Make { get; }", "IntegrateServiceProvider = false", "Func<string, IMyService>")]
    [Arguments("public partial Func<string, IMyService> Make { get; }", "IntegrateServiceProvider = true", "Func<string, IMyService>")]
    [Arguments("public partial Func<string, IMyService> Make();", "IntegrateServiceProvider = false", "Func<string, IMyService>")]
    [Arguments("public partial Lazy<Func<string, IMyService>> Make { get; }", "IntegrateServiceProvider = false", "Lazy<Func<string, IMyService>>")]
    [Arguments("public partial Func<Func<int, IMyService>> Make { get; }", "IntegrateServiceProvider = true", "Func<Func<int, IMyService>>")]
    public async Task SGIOC046_FuncWithInputParametersOfInstanceWithAsyncDecorator_ReportsDiagnostic(string accessor, string containerArguments, string returnType)
    {
        var source = CreateSource(accessor, containerArguments: containerArguments);
        var diagnostics = await RunAsync<RegisterAnalyzer>(source);
        var sgioc046 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046).ToList();

        await Assert.That(sgioc046).Count().IsEqualTo(1);
        await Assert.That(sgioc046[0].GetMessage()).Contains("'Make'").And.Contains($"'{returnType}'").And.Contains("'IMyService'");

        // The other partial accessor and dependency diagnostics do not report it again
        var containerDiagnostics = await RunAsync<ContainerAnalyzer>(source);
        await Assert.That(containerDiagnostics.Concat(diagnostics).Where(static d => d.Id is Constants.SGIOC021 or Constants.SGIOC027 or Constants.SGIOC029 or Constants.SGIOC030)).IsEmpty();
    }

    [Test]
    public async Task SGIOC046_LastRegistrationIsInstance_ReportsDiagnostic()
    {
        // The container resolves the last registration of the return type, here the Instance registration
        var diagnostics = await RunAsync<RegisterAnalyzer>(CreateSource(
            "public partial Func<string, IMyService> Make { get; }",
            before: "[IocRegister(ServiceTypes = [typeof(IMyService)])] public class Other(string s) : IMyService;"));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC046_KeyedInstance_ReportsKeyedAccessorOnly()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(Key = "k", ServiceTypes = [typeof(IMyService)], Instance = nameof(Default), Decorators = [typeof(AsyncDecorator)])]
            public class MyService(string name) : IMyService
            {
                public static readonly MyService Default = new("");
            }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer]
            public partial class TestContainer
            {
                [IocInject(Key = "k")]
                public partial Func<string, IMyService> Make { get; }

                public partial Func<string, IMyService> Unkeyed { get; }
            }
            """;

        var diagnostics = await RunAsync<RegisterAnalyzer>(source);
        var sgioc046 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046).ToList();

        await Assert.That(sgioc046).Count().IsEqualTo(1);
        await Assert.That(sgioc046[0].GetMessage()).Contains("'Make'");
    }

    [Test]
    [Arguments("[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.AsyncDecorator)])]", "", "public partial Func<string, IMyService> Make { get; }")]
    [Arguments("", "Key = \"k\", ", "[IocInject(Key = \"k\")] public partial KeyValuePair<string, Func<string, IMyService>> Make { get; }")]
    public async Task SGIOC046_DefaultDecoratorsOrKeyValuePair_ReportsDiagnostic(string assemblyAttribute, string keyArgument, string accessor)
    {
        // The decorators of the best default settings apply to the Instance registration's Func too, and a KeyValuePair is
        // built inline around the Func
        var decorators = assemblyAttribute.Length == 0 ? ", Decorators = [typeof(AsyncDecorator)]" : "";
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            {{assemblyAttribute}}

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister({{keyArgument}}ServiceTypes = [typeof(IMyService)], Instance = nameof(Default){{decorators}})]
            public class MyService(string name) : IMyService
            {
                public static readonly MyService Default = new("");
            }

            public class AsyncDecorator(IMyService inner) : IMyService
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer]
            public partial class TestContainer
            {
                {{accessor}}
            }
            """;

        var diagnostics = await RunAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC046_OpenGenericDecorator_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IGen<T> { }

            [IocRegister(ServiceTypes = [typeof(IGen<int>)], Instance = nameof(Default), Decorators = [typeof(GenDecorator<>)])]
            public class Gen(string name) : IGen<int>
            {
                public static readonly Gen Default = new("");
            }

            public class GenDecorator<T>(IGen<T> inner) : IGen<T>
            {
                [IocInject]
                public Task InitializeAsync() => Task.CompletedTask;
            }

            [IocContainer]
            public partial class TestContainer
            {
                public partial Func<string, IGen<int>> Make { get; }
            }
            """;

        var diagnostics = await RunAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("public partial IMyService Get { get; }", "AsyncDecorator")]
    [Arguments("public partial Lazy<IMyService> Get { get; }", "AsyncDecorator")]
    [Arguments("public partial Func<IMyService> Get { get; }", "AsyncDecorator")]
    [Arguments("public partial Task<IMyService> Get { get; }", "AsyncDecorator")]
    [Arguments("public partial Func<string, MyService> Get { get; }", "AsyncDecorator")]
    [Arguments("public partial IEnumerable<Func<string, IMyService>> Get { get; }", "AsyncDecorator")]
    [Arguments("public partial Func<string, IMyService> Get { get; }", "SyncDecorator")]
    public async Task SGIOC046_AccessorResolvedByTheContainer_NoDiagnostic(string accessor, string decorator)
    {
        // The instance itself, its parameterless wrappers and the Func of the undecorated implementation type are resolved by
        // the container; a collection is resolved from the fallback provider like any collection of Funcs
        var diagnostics = await RunAsync<RegisterAnalyzer>(CreateSource(accessor, decorator, containerArguments: "IntegrateServiceProvider = true"));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).IsEmpty();
    }

    [Test]
    [Arguments("public Func<string, IMyService> Make => null!;")]
    [Arguments("public Func<string, IMyService> Make() => null!;")]
    [Arguments("public static partial Func<string, IMyService> Make { get; }")]
    public async Task SGIOC046_MemberThatIsNotAPartialAccessor_NoDiagnostic(string member)
    {
        // Like the generator's ExtractPartialAccessors, only instance partial methods and properties are accessors
        var diagnostics = await RunAsync<RegisterAnalyzer>(CreateSource(member));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).IsEmpty();
    }

    [Test]
    [Arguments("public partial IMyService Get { get; }", "")]
    [Arguments("public partial Lazy<IMyService> Get { get; }", "")]
    [Arguments("public partial Func<IMyService> Get { get; }", "")]
    [Arguments("public partial Func<string, IMyService> Get { get; }", "")]
    [Arguments("public partial IMyService Get { get; }", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.AsyncDecorator)])]")]
    [Arguments("public partial Func<string, IMyService> Get { get; }", "[assembly: IocRegisterDefaults(typeof(TestNamespace.IMyService), Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, Decorators = [typeof(TestNamespace.AsyncDecorator)])]")]
    public async Task SGIOC046_InstanceWithAsyncDecorator_IsNotAsyncInitForContainerAnalyzer(string accessor, string assemblyAttribute)
    {
        // The instance of an Instance registration is resolved synchronously (the container returns it as is), so the
        // ContainerAnalyzer does not treat its service type as async-init (SGIOC021/027/029), whether the async decorator is the
        // registration's own or that of its default settings
        var source = CreateSource(accessor, containerArguments: "IntegrateServiceProvider = false");
        if(assemblyAttribute.Length > 0)
            source = source.Replace(", Decorators = [typeof(AsyncDecorator)]", "").Replace("using SourceGen.Ioc;", "using SourceGen.Ioc;\n\n" + assemblyAttribute);

        var diagnostics = await RunAsync<ContainerAnalyzer>(source);

        await Assert.That(diagnostics.Where(static d => d.Id is Constants.SGIOC021 or Constants.SGIOC027 or Constants.SGIOC029)).IsEmpty();
    }

    [Test]
    [Arguments("IntegrateServiceProvider = true", "[IocRegister(ServiceTypes = [typeof(IMyService)])] public class Other(string s) : IMyService;", "")]
    [Arguments("IncludeTags = [\"a\"]", "[IocRegister(ServiceTypes = [typeof(IMyService)], Tags = [\"a\"])] public class Other(string s) : IMyService;", "")]
    [Arguments("ExplicitOnly = true", "public class Other(string s) : IMyService;", "[IocRegisterFor(typeof(Other), ServiceTypes = [typeof(IMyService)])]")]
    public async Task SGIOC046_ContainerResolvesAnotherRegistration_NoDiagnostic(string containerArguments, string after, string containerAttributes)
    {
        // The container constructs the Func from the last of its own registrations of the return type, which is not the
        // Instance registration
        var diagnostics = await RunAsync<RegisterAnalyzer>(CreateSource(
            "public partial Func<string, IMyService> Make { get; }",
            containerArguments: containerArguments,
            after: after,
            containerAttributes: containerAttributes));

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).IsEmpty();
    }

    [Test]
    public async Task SGIOC046_AsyncInitRegistration_ReportsSGIOC029Instead()
    {
        // An async-init registration of the return type makes the ContainerAnalyzer report SGIOC029 for the accessor
        var source = CreateSource(
            "public partial Func<string, IMyService> Make { get; }",
            before: "[IocRegister(ServiceTypes = [typeof(IMyService)])] public class Other(string s) : IMyService { [IocInject] public Task InitAsync() => Task.CompletedTask; }");

        var diagnostics = await RunAsync<RegisterAnalyzer>(source);
        var containerDiagnostics = await RunAsync<ContainerAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).IsEmpty();
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(containerDiagnostics, Constants.SGIOC029)).Count().IsEqualTo(1);
    }

    [Test]
    [Arguments("Register,Container,MethodInject")]
    [Arguments("Register,MethodInject,AsyncMethodInject")]
    public async Task SGIOC046_FeatureDisabled_NoDiagnostic(string features)
    {
        // Without AsyncMethodInject the async inject method is ignored, so the container constructs the Func; without
        // Container no container is generated
        var diagnostics = await RunAsync<RegisterAnalyzer>(CreateSource("public partial Func<string, IMyService> Make { get; }"), features);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC046)).IsEmpty();
    }
}
