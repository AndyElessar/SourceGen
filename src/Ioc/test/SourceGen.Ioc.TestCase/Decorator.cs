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

/// <summary>Service decorated as <see cref="IInnerExposingService"/> and also resolved by its implementation type.</summary>
public interface IInnerExposingService
{
    /// <summary>The wrapped service, or <see langword="null"/> for the undecorated implementation.</summary>
    IInnerExposingService? Inner { get; }
}

public sealed class InnerExposingService : IInnerExposingService
{
    public IInnerExposingService? Inner => null;
}

public sealed class InnerExposingDecorator(IInnerExposingService inner) : IInnerExposingService
{
    public IInnerExposingService? Inner => inner;
}

/// <summary>Async-init service decorated as <see cref="IAsyncInnerExposingService"/>.</summary>
public interface IAsyncInnerExposingService
{
    IAsyncInnerExposingService? Inner { get; }
    bool IsInitialized { get; }
}

public sealed class AsyncInnerExposingService : IAsyncInnerExposingService
{
    public IAsyncInnerExposingService? Inner => null;
    public bool IsInitialized { get; private set; }

    [IocInject]
    public async Task InitializeAsync()
    {
        await Task.Yield();
        IsInitialized = true;
    }
}

public sealed class AsyncInnerExposingDecorator(IAsyncInnerExposingService inner) : IAsyncInnerExposingService
{
    public IAsyncInnerExposingService? Inner => inner;
    public bool IsInitialized => inner.IsInitialized;
}

[IocRegisterFor<InnerExposingService>(
    ServiceLifetime.Scoped,
    ServiceTypes = [typeof(IInnerExposingService)],
    Decorators = [typeof(InnerExposingDecorator)])]
[IocRegisterFor<AsyncInnerExposingService>(
    ServiceLifetime.Singleton,
    ServiceTypes = [typeof(IAsyncInnerExposingService)],
    Decorators = [typeof(AsyncInnerExposingDecorator)])]
[IocContainer(ExplicitOnly = true)]
public sealed partial class DecoratorImplementationTypeModule;
