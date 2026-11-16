namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>Service whose default settings are Scoped, which an <c>Instance</c> registration without Lifetime ignores.</summary>
[IocRegisterDefaults(typeof(IImplicitSingletonInstance), ServiceLifetime.Scoped)]
public interface IImplicitSingletonInstance;

/// <summary>Instance registration without an explicit Lifetime: it is a singleton (issue #207).</summary>
[IocRegister(ServiceTypes = [typeof(IImplicitSingletonInstance)], Instance = nameof(Default))]
public sealed class ImplicitSingletonInstance : IImplicitSingletonInstance
{
    public static readonly ImplicitSingletonInstance Default = new();
}

/// <summary>Singleton consumer of the instance through a shared <see cref="Lazy{T}"/>.</summary>
[IocRegister(ServiceLifetime.Singleton)]
public sealed class ImplicitSingletonInstanceConsumer(Lazy<IImplicitSingletonInstance> instance)
{
    public Lazy<IImplicitSingletonInstance> Instance => instance;
}

[IocContainer]
public sealed partial class ImplicitSingletonInstanceContainer;
