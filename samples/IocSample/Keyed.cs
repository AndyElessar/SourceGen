namespace IocSample;

public interface IKeyed;

[IocRegister<IKeyed>(Key = "Key")]
internal class Keyed : IKeyed;

public enum KeyEnum
{
    Key0 = 0,
    Key1 = 1
}
[IocRegister<IKeyed>(Key = KeyEnum.Key0)]
internal class KeyedEnum : IKeyed
{
    [IocInject]
    public void Init([ServiceKey] KeyEnum key)
    {

    }
}

public static class KeyedExtensions
{
    public static readonly Guid Key = Guid.CreateVersion7();
}
[IocRegister<IKeyed>(Key = nameof(KeyedExtensions.Key), KeyType = KeyType.Csharp)]
internal class KeyedCsharp([IocInject(Key = "Key")] IKeyed keyed) : IKeyed
{
    private readonly IKeyed keyed = keyed;
}
