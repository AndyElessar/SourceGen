namespace IocSample;

public interface IKeyed;

[IoCRegister<IKeyed>(Key = "Key")]
internal class Keyed : IKeyed;

public enum KeyEnum
{
    Key0 = 0,
    Key1 = 1
}
[IoCRegister<IKeyed>(Key = KeyEnum.Key0)]
internal class KeyedEnum : IKeyed;

public static class KeyedExtensions
{
    public static readonly Guid Key = Guid.CreateVersion7();
}
[IoCRegister<IKeyed>(Key = nameof(KeyedExtensions.Key), KeyType = KeyType.Csharp)]
internal class KeyedCsharp : IKeyed;
