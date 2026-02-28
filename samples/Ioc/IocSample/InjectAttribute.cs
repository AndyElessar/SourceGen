namespace IocSample;

public interface IDependency;

[IocRegister<IDependency>(Key = "1")]
internal class Dependency : IDependency;

[IocRegister<IDependency>(Key = "2")]
internal class Dependency2 : IDependency;

[IocRegister<IDependency>(Key = "3")]
internal class Dependency3 : IDependency;

[IocRegister]
internal class DependentClass([IocInject("1")] IDependency dependency)
{
    private readonly IDependency dependency = dependency;

    [IocInject("2")]
    public IDependency Dependency2 { get; init; } = null!;

    private IDependency dependency3 = null!;
    [IocInject]
    public void Initialize([IocInject("3")] IDependency dependency)
    {
        dependency3 = dependency;
    }

    //[IocInject]
    public static IDependency StaticDependency { get; set; } = null!;
}

[IocRegister]
internal class DependentClass2(IDependency dependency1, IDependency dependency2)
{
    private readonly IDependency dependency = dependency1;
    private readonly IDependency dependency2 = dependency2;

    [IocInject]
    internal DependentClass2([FromKeyedServices("1")] IDependency dependency1)
        : this(dependency1, new Dependency3())
    {
    }
}
