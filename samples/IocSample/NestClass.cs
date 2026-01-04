namespace IocSample.TestNest;

internal sealed class TestNestClass
{
    public interface INestInterface;

    [IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(INestInterface)], KeyType = KeyType.Csharp, Key = nameof(Key))]
    internal sealed class NestClassImpl : INestInterface
    {
        public const string Key = "Nest";
    }
}
