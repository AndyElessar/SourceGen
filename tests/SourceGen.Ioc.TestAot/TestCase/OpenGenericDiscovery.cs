using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc.TestCase;

namespace SourceGen.Ioc.TestAot.TestCase;

#region Discovery Method 1: Constructor Parameter

/// <summary>
/// Service that discovers IHandler&lt;RequestA, ResponseA&gt; through constructor parameter.
/// </summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class HandlerConsumerA(IHandler<RequestA, ResponseA> handler)
{
    public IHandler<RequestA, ResponseA> Handler => handler;

    public ResponseA Execute(RequestA request) => handler.Handle(request);
}

#endregion

#region Discovery Method 2: IServiceProvider Method Call

/// <summary>
/// Service that discovers IHandler&lt;RequestB, ResponseB&gt; through IServiceProvider.GetRequiredService call.
/// </summary>
[IocRegister(ServiceLifetime.Transient)]
public sealed class ServiceLocatorB(IServiceProvider serviceProvider)
{
    public IHandler<RequestB, ResponseB> GetHandler()
        => serviceProvider.GetRequiredService<IHandler<RequestB, ResponseB>>();

    public ResponseB Execute(RequestB request) => GetHandler().Handle(request);
}

#endregion

#region Discovery Method 3: [IocDiscover] Attribute

/// <summary>
/// Module that explicitly discovers IHandler&lt;RequestC, ResponseC&gt; using [IocDiscover] attribute.
/// </summary>
[IocDiscover<IHandler<RequestC, ResponseC>>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class DiscoveryModuleC;

#endregion

/// <summary>
/// Aggregated module for all open generic discovery test cases.
/// </summary>
[IocImportModule<DiscoveryModuleC>]
[IocContainer(ExplicitOnly = true)]
public sealed partial class OpenGenericDiscoveryModule;
