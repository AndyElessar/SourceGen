using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc;

namespace IocRazorSample;

[IocRegisterDefaults<Microsoft.AspNetCore.Components.IComponent>(
    ServiceLifetime.Transient,
    //ImplementationTypes = [typeof(LoadData)],
    Tags = [ComponentTag])]
[IocContainer]
public partial class ComponentContainer : Microsoft.AspNetCore.Components.IComponentActivator
{
    public const string ComponentTag = "Component";
}
