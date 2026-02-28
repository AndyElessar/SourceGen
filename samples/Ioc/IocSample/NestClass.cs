namespace IocSample.TestNest;

internal sealed class TestNestClass
{
    public interface INestInterface;

    [IocRegister<INestInterface>(Lifetime = ServiceLifetime.Transient, KeyType = KeyType.Csharp, Key = nameof(Key))]
    internal sealed class NestClassImpl : INestInterface
    {
        public const string Key = "Nest";
    }
}
