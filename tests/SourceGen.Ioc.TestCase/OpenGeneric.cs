using Microsoft.Extensions.DependencyInjection;
using SourceGen.Ioc;

[assembly: IocRegisterDefaults(
    typeof(SourceGen.Ioc.TestCase.IHandler<,>),
    ServiceLifetime.Transient)]

namespace SourceGen.Ioc.TestCase;

#region Generic Handler Interface

/// <summary>Generic request handler interface.</summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public interface IHandler<in TRequest, out TResponse>
{
    TResponse Handle(TRequest request);
}

#endregion

#region Request/Response Types

/// <summary>Request type A for constructor parameter discovery.</summary>
public sealed record RequestA(string Value);

/// <summary>Response type A.</summary>
public sealed record ResponseA(string Result);

/// <summary>Request type B for IServiceProvider method call discovery.</summary>
public sealed record RequestB(int Value);

/// <summary>Response type B.</summary>
public sealed record ResponseB(int Result);

/// <summary>Request type C for [IocDiscover] attribute discovery.</summary>
public sealed record RequestC(bool Value);

/// <summary>Response type C.</summary>
public sealed record ResponseC(bool Result);

#endregion

#region Open Generic Handler Implementation

/// <summary>Generic handler that can handle any request/response pair.</summary>
/// <remarks>
/// This class must be public for cross-assembly open generic registration.
/// See ContainerModule.cs in TestAot for usage example.
/// </remarks>
public sealed class GenericHandler<TRequest, TResponse> : IHandler<TRequest, TResponse>
    where TRequest : notnull
{
    public TResponse Handle(TRequest request)
    {
        // Create response based on type - for testing purposes
        if (typeof(TResponse) == typeof(ResponseA) && request is RequestA reqA)
        {
            return (TResponse)(object)new ResponseA($"Handled: {reqA.Value}");
        }

        if (typeof(TResponse) == typeof(ResponseB) && request is RequestB reqB)
        {
            return (TResponse)(object)new ResponseB(reqB.Value * 2);
        }

        if (typeof(TResponse) == typeof(ResponseC) && request is RequestC reqC)
        {
            return (TResponse)(object)new ResponseC(!reqC.Value);
        }

        throw new NotSupportedException($"Cannot handle {typeof(TRequest).Name} -> {typeof(TResponse).Name}");
    }
}

#endregion

[IocRegisterFor(typeof(GenericHandler<,>), ServiceLifetime.Transient, ServiceTypes = [typeof(IHandler<,>)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class OpenGenericModule;
