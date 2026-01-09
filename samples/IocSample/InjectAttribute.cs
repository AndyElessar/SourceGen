namespace IocSample;

public interface IDependency;

[IoCRegister(ServiceTypes = [typeof(IDependency)], Key = "1")]
internal class Dependency : IDependency;

[IoCRegister(ServiceTypes = [typeof(IDependency)], Key = "2")]
internal class Dependency2 : IDependency;

[IoCRegister(ServiceTypes = [typeof(IDependency)], Key = "3")]
internal class Dependency3 : IDependency;

[IoCRegister]
internal class DependentClass([Inject(Key = "1")] IDependency dependency)
{
    private readonly IDependency dependency = dependency;

    [Inject(Key = "2")]
    public IDependency Dependency2 { get; init; } = null!;

    private IDependency dependency3 = null!;
    [Inject]
    public void Initialize([Inject(Key = "3")] IDependency dependency)
    {
        dependency3 = dependency;
    }

    //[Inject]
    public static IDependency StaticDependency { get; set; } = null!;
}

[IoCRegister]
internal class DependentClass2(IDependency dependency1, IDependency dependency2)
{
    private readonly IDependency dependency = dependency1;
    private readonly IDependency dependency2 = dependency2;

    [Inject]
    internal DependentClass2([FromKeyedServices("1")] IDependency dependency1)
        : this(dependency1, new Dependency3())
    {
    }
}