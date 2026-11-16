#if NET11_0_OR_GREATER
using Microsoft.AspNetCore.Components;

namespace SourceGen.Ioc.TestAot.TestCase;

public interface IComponentDependency;

internal sealed class ComponentDependency : IComponentDependency;

/// <summary>Base for the components of <see cref="ComponentPropertyActivatorContainer"/>; none of them is registered.</summary>
public abstract class TestComponentBase : IComponent
{
    public void Attach(RenderHandle renderHandle)
    {
    }

    public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
}

public sealed class InjectComponent : TestComponentBase
{
    [Inject]
    public IComponentDependency Dependency { get; set; } = null!;
}

public sealed class KeyedInjectComponent : TestComponentBase
{
    [Inject(Key = "inject-key")]
    public IComponentDependency Dependency { get; set; } = null!;
}

public sealed class KeyedIocInjectComponent : TestComponentBase
{
    [IocInject("ioc-key")]
    public IComponentDependency Dependency { get; set; } = null!;
}

public sealed class CsharpKeyIocInjectComponent : TestComponentBase
{
    [IocInject("nameof(Dependency)", KeyType = KeyType.Csharp)]
    public IComponentDependency? Dependency { get; set; }
}

public sealed class BothInjectAttributesComponent : TestComponentBase
{
    [Inject(Key = "inject-key")]
    [IocInject("ioc-key")]
    public IComponentDependency Dependency { get; set; } = null!;
}

public sealed class UnrelatedInjectAttributeComponent : TestComponentBase
{
    [Unrelated.Inject]
    public IComponentDependency? Dependency { get; set; }
}

/// <summary>
/// Container whose generated <see cref="IComponentPropertyActivator"/> injects the properties of unregistered components.
/// </summary>
[IocRegisterFor<ComponentDependency>(ServiceLifetime.Singleton, ServiceTypes = [typeof(IComponentDependency)])]
[IocRegisterFor<ComponentDependency>(ServiceLifetime.Singleton, Key = "inject-key", ServiceTypes = [typeof(IComponentDependency)])]
[IocRegisterFor<ComponentDependency>(ServiceLifetime.Singleton, Key = "ioc-key", ServiceTypes = [typeof(IComponentDependency)])]
[IocContainer(IntegrateServiceProvider = false, ExplicitOnly = true)]
public sealed partial class ComponentPropertyActivatorContainer : IComponentPropertyActivator;
#endif
