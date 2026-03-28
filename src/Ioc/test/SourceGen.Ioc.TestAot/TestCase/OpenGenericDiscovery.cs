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
/// Discovers IHandler&lt;RequestC, ResponseC&gt; using [IocDiscover] attribute.
/// </summary>
[IocDiscover<IHandler<RequestC, ResponseC>>]
public sealed class Marker;

#endregion
