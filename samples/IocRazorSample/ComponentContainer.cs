using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc;
using Microsoft.AspNetCore.Components;

namespace IocRazorSample;

[IocRegisterDefaults<IComponent>(
    ServiceLifetime.Transient,
    //ImplementationTypes = [typeof(LoadData)],
    Tags = [ComponentTag])]
[IocContainer]
public partial class ComponentContainer : IComponentActivator
{
    public const string ComponentTag = "Component";
}
