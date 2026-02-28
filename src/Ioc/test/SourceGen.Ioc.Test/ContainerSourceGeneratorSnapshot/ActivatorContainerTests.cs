namespace SourceGen.Ioc.Test.ContainerSourceGeneratorSnapshot;

/// <summary>
/// Snapshot tests for optional activator interface generation.
/// </summary>
[Category(Constants.SourceGeneratorSnapshot)]
[Category(Constants.ContainerGeneration)]
public class ActivatorContainerTests
{
    /// <summary>
    /// Suppressed diagnostics for initial compilation: CS0535 (interface member not implemented).
    /// This is expected because the source generator provides the explicit interface implementations.
    /// </summary>
    private static readonly IReadOnlySet<string> SuppressedInitialDiagnosticIds = new HashSet<string>(["CS0535"]);

    [Test]
    public async Task Container_WithIControllerActivator_GeneratesControllerActivatorImplementation()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer : global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator { }
            """;

        var mvcReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Mvc.Core", MvcAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [mvcReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithIComponentActivator_GeneratesComponentActivatorImplementation()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer : global::Microsoft.AspNetCore.Components.IComponentActivator { }
            """;

        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithBothActivators_GeneratesBothImplementations()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer :
                global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator,
                global::Microsoft.AspNetCore.Components.IComponentActivator
            { }
            """;

        var mvcReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Mvc.Core", MvcAbstractionsSource)
            .ToMetadataReference();
        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [mvcReference, componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithIComponentPropertyActivator_GeneratesPropertyActivatorImplementation()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer :
                global::Microsoft.AspNetCore.Components.IComponentActivator,
                global::Microsoft.AspNetCore.Components.IComponentPropertyActivator
            { }
            """;

        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithIComponentPropertyActivatorOnly_GeneratesReflectionFallback()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer : global::Microsoft.AspNetCore.Components.IComponentPropertyActivator { }
            """;

        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithAllActivators_GeneratesAllImplementations()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer]
            public partial class TestContainer :
                global::Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator,
                global::Microsoft.AspNetCore.Components.IComponentActivator,
                global::Microsoft.AspNetCore.Components.IComponentPropertyActivator
            { }
            """;

        var mvcReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Mvc.Core", MvcAbstractionsSource)
            .ToMetadataReference();
        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [mvcReference, componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithIComponentPropertyActivator_SwitchMode_GeneratesArrayExistsCheck()
    {
        const string source = """
            using SourceGen.Ioc;

            namespace TestNamespace;

            [IocContainer(UseSwitchStatement = true)]
            public partial class TestContainer :
                global::Microsoft.AspNetCore.Components.IComponentActivator,
                global::Microsoft.AspNetCore.Components.IComponentPropertyActivator
            { }
            """;

        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    [Test]
    public async Task Container_WithRegisteredComponent_GeneratesComponentWithPropertyInjection()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;
            using SourceGen.Ioc;

            namespace TestNamespace;

            public interface IDataService { }

            [IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IDataService)])]
            public class DataService : IDataService { }

            [IocRegister(Lifetime = ServiceLifetime.Transient)]
            public class MyComponent : global::Microsoft.AspNetCore.Components.IComponent
            {
                [global::Microsoft.AspNetCore.Components.Inject]
                public IDataService DataService { get; set; } = default!;
            }

            [IocContainer]
            public partial class TestContainer :
                global::Microsoft.AspNetCore.Components.IComponentActivator,
                global::Microsoft.AspNetCore.Components.IComponentPropertyActivator
            { }
            """;

        var componentsReference = SourceGeneratorTestHelper.CreateCompilation("Microsoft.AspNetCore.Components", ComponentsAbstractionsSource)
            .ToMetadataReference();

        var result = SourceGeneratorTestHelper.RunGenerator<IocSourceGenerator>(
            source,
            additionalReferences: [componentsReference],
            suppressedInitialDiagnosticIds: SuppressedInitialDiagnosticIds);
        await result.VerifyCompilableAsync();
        var generatedSource = SourceGeneratorTestHelper.GetGeneratedSource(result, "Container.g.cs");

        await Verify(generatedSource);
    }

    private const string MvcAbstractionsSource = """
        namespace Microsoft.AspNetCore.Mvc.Abstractions
        {
            public class ActionDescriptor
            {
                public global::System.Reflection.TypeInfo ControllerTypeInfo { get; set; } = null!;
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class ControllerContext
            {
                public global::Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor ActionDescriptor { get; set; } = new();
            }
        }

        namespace Microsoft.AspNetCore.Mvc.Controllers
        {
            public interface IControllerActivator
            {
                object Create(global::Microsoft.AspNetCore.Mvc.ControllerContext controllerContext);
                void Release(global::Microsoft.AspNetCore.Mvc.ControllerContext context, object controller);
                global::System.Threading.Tasks.ValueTask ReleaseAsync(global::Microsoft.AspNetCore.Mvc.ControllerContext context, object controller);
            }
        }
        """;

    private const string ComponentsAbstractionsSource = """
        namespace Microsoft.AspNetCore.Components
        {
            public interface IComponent
            {
            }

            public interface IComponentActivator
            {
                IComponent CreateInstance([global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] global::System.Type componentType);
            }

            public interface IComponentPropertyActivator
            {
                global::System.Action<global::System.IServiceProvider, IComponent> GetActivator(
                    [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] global::System.Type componentType);
            }

            [global::System.AttributeUsage(global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
            public class InjectAttribute : global::System.Attribute
            {
                public object? Key { get; set; }
            }
        }
        """;
}
