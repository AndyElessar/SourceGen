namespace SourceGen.Ioc.Test.Analyzer;

/// <summary>
/// Tests for SGIOC016: Factory Method is unmatched - Generic factory method does not have [IocGenericFactory] attribute.
/// </summary>
[Category(Constants.Analyzer)]
[Category(Constants.SGIOC016)]
public class SGIOC016Tests
{
    [Test]
    public async Task SGIOC016_GenericFactory_WithoutIocGenericFactoryAttribute_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Generic factory method without [IocGenericFactory] attribute - should report SGIOC016
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create").And.Contains("[IocGenericFactory]");
    }

    [Test]
    [Arguments("AttributeSameTypeArguments", "[IocGenericFactory(typeof(IPair<int, int>), typeof(int))]", "", "IPair<string, string>", true)]
    [Arguments("TypeMappingSameTypeArguments", "", ", GenericFactoryTypeMapping = [typeof(IPair<int, int>), typeof(int)]", "IPair<string, string>", true)]
    [Arguments("AttributeDifferentTypeArguments", "[IocGenericFactory(typeof(IPair<int, int>), typeof(int))]", "", "IPair<string, int>", false)]
    [Arguments("TypeMappingDifferentTypeArguments", "", ", GenericFactoryTypeMapping = [typeof(IPair<int, int>), typeof(int)]", "IPair<string, int>", false)]
    public async Task SGIOC016_PlaceholderRepeatedInTemplate_IsValidAndMatchesSameTypeArguments(string scenario, string methodAttribute, string mapping, string target, bool matches)
    {
        // A placeholder may appear more than once; a service type matches only when it takes the same type argument at each position
        var source = $$"""
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;
            using TestNamespace;

            [assembly: IocRegisterFor(typeof({{target}}), ServiceLifetime.Singleton, Factory = nameof(FactoryContainer.Create){{mapping}})]

            namespace TestNamespace;

            public interface IPair<T1, T2>;

            public static class FactoryContainer
            {
                {{methodAttribute}}
                public static IPair<T, T> Create<T>() => throw new NotImplementedException();
            }

            [IocContainer]
            public partial class TestContainer;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);

        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016")).IsEmpty();
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039).Count()).IsEqualTo(matches ? 0 : 1);

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(source);
        await result.VerifyCompilableAsync();
        foreach(var output in new[] { SourceGeneratorTestHelper.GetGeneratedSource(result, "ServiceRegistration") ?? "", SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs") ?? "" })
        {
            await Assert.That(output.Contains("global::TestNamespace.FactoryContainer.Create<string>()")).IsEqualTo(matches);
            await Assert.That(output).DoesNotContain("FactoryContainer.Create<int>");
        }
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithIocGenericFactoryAttribute_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(int))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC016_NonGenericFactory_NoDiagnostic()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IMyService { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IMyService)],
                Factory = nameof(MyServiceFactory.Create))]
            public class MyService : IMyService { }

            public static class MyServiceFactory
            {
                // Non-generic factory - no [IocGenericFactory] required
                public static IMyService Create(IServiceProvider sp) => new MyService();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_OnIocRegister_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegister(
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IRequestHandler<>)],
                Factory = nameof(FactoryContainer.Create))]
            public class Handler<T> : IRequestHandler<Task<T>> { }

            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory] - should report SGIOC016
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_OnIocRegisterFor_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocRegisterFor(
                typeof(Handler<>),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IRequestHandler<>)],
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory] - should report SGIOC016
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_TwoTypeParameters_WithoutAttribute_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                // Two type parameter generic factory without [IocGenericFactory] - should report SGIOC016
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create");
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithGenericFactoryTypeMappingOnAttribute_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(int)])]
            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory] but GenericFactoryTypeMapping is on the attribute
                // SGIOC016 should NOT fire
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithGenericFactoryTypeMappingOnIocRegisterFor_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public class Handler<T> : IRequestHandler<Task<T>> { }

            [IocRegisterFor(
                typeof(Handler<>),
                Lifetime = ServiceLifetime.Singleton,
                ServiceTypes = [typeof(IRequestHandler<>)],
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(int)])]
            public static class FactoryContainer
            {
                // Generic factory without [IocGenericFactory] but GenericFactoryTypeMapping on attribute
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016");

        await Assert.That(sgioc016).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithDuplicateGenericFactoryTypeMappingOnAttribute_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(int), typeof(int)])]
            public static class FactoryContainer
            {
                // Generic factory with duplicate placeholders in GenericFactoryTypeMapping - SGIOC016 should still fire
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create");
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithInsufficientGenericFactoryTypeMapping_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>, List<string>>), typeof(int)])]
            public static class FactoryContainer
            {
                // 2 type params but mapping only provides 1 placeholder - SGIOC016 should fire
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create");
    }

    [Test]
    public async Task SGIOC016_GenericFactory_WithExcessiveGenericFactoryTypeMapping_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(int), typeof(string)])]
            public static class FactoryContainer
            {
                // 1 type param but mapping provides 2 placeholders - SGIOC016 should fire
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create");
    }

    [Test]
    public async Task SGIOC016_IocGenericFactory_PlaceholderCountMismatch_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TRequest, TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<,>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create))]
            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>, List<int>>), typeof(int))]
                public static IRequestHandler<Task<T1>, List<T2>> Create<T1, T2>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("Create").And.Contains("1 placeholder type(s)").And.Contains("2 type parameter(s)");
    }

    [Test]
    public async Task SGIOC016_IocGenericFactory_TemplateOnly_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("at least one placeholder type");
    }

    [Test]
    public async Task SGIOC016_IocGenericFactory_PlaceholderNotInTemplate_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            public static class FactoryContainer
            {
                [IocGenericFactory(typeof(IRequestHandler<Task<int>>), typeof(decimal))]
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("'decimal'").And.Contains("does not appear");
    }

    [Test]
    public async Task SGIOC016_PlaceholderOnlyInContainingType_ReportsOnlySgioc016WithContainingType()
    {
        // TypeData of Outer<int>.Inner does not carry the type arguments of Outer<int> (issue #187), so the placeholder is
        // not in the template; the registration is not analyzed by SGIOC039, which only reports service types of valid mappings
        const string source = """
            using System;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo<T>;
            public class Outer<T> { public class Inner; }
            public class Entity;

            public static class Factories
            {
                [IocGenericFactory(typeof(IFoo<Outer<int>.Inner>), typeof(int))]
                public static IFoo<Outer<T>.Inner> Create<T>() => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof(IFoo<Outer<Entity>.Inner>)], Factory = nameof(Factories.Create))]
            public sealed class Impl : IFoo<Outer<Entity>.Inner>;
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC016).ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("'int'").And.Contains("'IFoo<Outer<int>.Inner>'");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039)).IsEmpty();
    }

    [Test]
    [Arguments("Generic", "IFoo<List<int>>", "List<int>", "IFoo<List<Entity>>")]
    [Arguments("Nullable", "IFoo<int?>", "int?", "IFoo<Entity?>")]
    [Arguments("Tuple", "IFoo<(int, string)>", "(int, string)", "IFoo<(Entity, string)>")]
    [Arguments("Array", "IFoo<int[]>", "int[]", "IFoo<Entity[]>")]
    public async Task SGIOC016_PlaceholderWithTypeArguments_ReportsOnlySgioc016(string scenario, string template, string placeholder, string serviceType)
    {
        // The generator only takes types without type arguments as placeholders, so it cannot call the factory
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IFoo<T>;
            public struct Entity;

            public static class Factories
            {
                [IocGenericFactory(typeof({{template}}), typeof({{placeholder}}))]
                public static object Create<T>() => throw new NotImplementedException();
            }

            [IocRegister(ServiceTypes = [typeof({{serviceType}})], Factory = nameof(Factories.Create))]
            public sealed class Impl : {{serviceType}};
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC016).ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("has type arguments or is an array");
        await Assert.That(SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, Constants.SGIOC039)).IsEmpty();
    }

    [Test]
    public async Task SGIOC016_GenericFactoryTypeMapping_PlaceholderNotInTemplate_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IRequestHandler<TResponse> { }

            [IocRegisterDefaults(typeof(IRequestHandler<>),
                ServiceLifetime.Singleton,
                Factory = nameof(FactoryContainer.Create),
                GenericFactoryTypeMapping = [typeof(IRequestHandler<Task<int>>), typeof(decimal)])]
            public static class FactoryContainer
            {
                public static IRequestHandler<Task<T>> Create<T>() => throw new NotImplementedException();
            }
            """;

        var diagnostics = await SourceGeneratorTestHelper.RunAnalyzerAsync<RegisterAnalyzer>(source);
        var sgioc016 = SourceGeneratorTestHelper.GetDiagnosticsById(diagnostics, "SGIOC016").ToList();

        await Assert.That(sgioc016).Count().IsEqualTo(1);
        await Assert.That(sgioc016[0].GetMessage()).Contains("does not appear");
    }
}
