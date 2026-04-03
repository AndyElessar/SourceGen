namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>
/// Feature service interface used to test IocContainer.IncludeTags filtering.
/// Services are defined in the same assembly as the container so the source
/// generator can evaluate their Tags properties at compile time.
/// </summary>
public interface IFeatureService
{
    string FeatureName { get; }
}

// Registered via [IocRegister] so the generator can apply IncludeTags filtering.
[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFeatureService)], Tags = ["featureA"])]
public sealed class FeatureAService : IFeatureService
{
    public string FeatureName => "FeatureA";
}

[IocRegister(Lifetime = ServiceLifetime.Singleton, ServiceTypes = [typeof(IFeatureService)], Tags = ["featureB"])]
public sealed class FeatureBService : IFeatureService
{
    public string FeatureName => "FeatureB";
}

/// <summary>
/// Container that includes only services tagged "featureA".
/// <see cref="FeatureBService"/> must NOT appear in this container's resolver.
/// </summary>
[IocContainer(IncludeTags = ["featureA"])]
public sealed partial class FeatureAContainer;

/// <summary>
/// Container that includes only services tagged "featureB".
/// <see cref="FeatureAService"/> must NOT appear in this container's resolver.
/// </summary>
[IocContainer(IncludeTags = ["featureB"])]
public sealed partial class FeatureBContainer;
