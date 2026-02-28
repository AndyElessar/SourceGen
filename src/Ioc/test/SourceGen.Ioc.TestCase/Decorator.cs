using Microsoft.Extensions.DependencyInjection;

namespace SourceGen.Ioc.TestCase;

/// <summary>
/// Interface for decorated services.
/// DecoratorCount tracks how many decorators wrap the core service.
/// </summary>
public interface IDecoratedService
{
    int DecoratorCount { get; }
    string GetMessage();
}

/// <summary>Core service implementation with no decorators.</summary>
internal sealed class CoreDecoratedService : IDecoratedService
{
    public int DecoratorCount => 0;
    public string GetMessage() => "Core";
}

/// <summary>First decorator that wraps the inner service.</summary>
internal sealed class Decorator1(IDecoratedService inner) : IDecoratedService
{
    public int DecoratorCount => inner.DecoratorCount + 1;
    public string GetMessage() => $"Decorator1({inner.GetMessage()})";
}

/// <summary>Second decorator that wraps the inner service.</summary>
internal sealed class Decorator2(IDecoratedService inner) : IDecoratedService
{
    public int DecoratorCount => inner.DecoratorCount + 1;
    public string GetMessage() => $"Decorator2({inner.GetMessage()})";
}

[IocRegisterFor<CoreDecoratedService>(
    ServiceLifetime.Scoped,
    ServiceTypes = [typeof(IDecoratedService)],
    Decorators = [typeof(Decorator1), typeof(Decorator2)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class DecoratorModule;
