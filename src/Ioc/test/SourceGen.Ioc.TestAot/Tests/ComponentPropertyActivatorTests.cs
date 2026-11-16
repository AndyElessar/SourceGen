#if NET11_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests for the generated <see cref="IComponentPropertyActivator"/> of unregistered components,
/// which finds inject attributes by type and reads their keys without reflection on the attribute.
/// </summary>
public sealed class ComponentPropertyActivatorTests
{
    [Test]
    public async Task GetActivator_InjectProperty_InjectsService()
    {
        using var container = new ComponentPropertyActivatorContainer();
        var component = new InjectComponent();

        Activate(container, component);

        await Assert.That(component.Dependency).IsSameReferenceAs(container.GetRequiredService<IComponentDependency>());
    }

    [Test]
    public async Task GetActivator_KeyedInjectProperty_InjectsKeyedService()
    {
        using var container = new ComponentPropertyActivatorContainer();
        var component = new KeyedInjectComponent();

        Activate(container, component);

        await Assert.That(component.Dependency).IsSameReferenceAs(container.GetRequiredKeyedService<IComponentDependency>("inject-key"));
        await Assert.That(component.Dependency).IsNotSameReferenceAs(container.GetRequiredService<IComponentDependency>());
    }

    [Test]
    public async Task GetActivator_KeyedIocInjectProperty_InjectsKeyedService()
    {
        using var container = new ComponentPropertyActivatorContainer();
        var component = new KeyedIocInjectComponent();

        Activate(container, component);

        await Assert.That(component.Dependency).IsSameReferenceAs(container.GetRequiredKeyedService<IComponentDependency>("ioc-key"));
        await Assert.That(component.Dependency).IsNotSameReferenceAs(container.GetRequiredService<IComponentDependency>());
    }

    [Test]
    public async Task GetActivator_CsharpKeyIocInjectProperty_SkipsProperty()
    {
        using var container = new ComponentPropertyActivatorContainer();
        var component = new CsharpKeyIocInjectComponent();

        Activate(container, component);

        await Assert.That(component.Dependency).IsNull();
    }

    [Test]
    public async Task GetActivator_InjectAndIocInjectProperty_UsesInjectKey()
    {
        using var container = new ComponentPropertyActivatorContainer();
        var component = new BothInjectAttributesComponent();

        Activate(container, component);

        await Assert.That(component.Dependency).IsSameReferenceAs(container.GetRequiredKeyedService<IComponentDependency>("inject-key"));
    }

    [Test]
    public async Task GetActivator_UnrelatedAttributeNamedInject_SkipsProperty()
    {
        using var container = new ComponentPropertyActivatorContainer();
        var component = new UnrelatedInjectAttributeComponent();

        Activate(container, component);

        await Assert.That(component.Dependency).IsNull();
    }

    private static void Activate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(ComponentPropertyActivatorContainer container, TComponent component)
        where TComponent : IComponent
        => ((IComponentPropertyActivator)container).GetActivator(typeof(TComponent))(container, component);
}
#endif
