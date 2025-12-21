namespace IocSample;

public interface ITest1;

[IoCRegister(Lifetime = ServiceLifetime.Transient, ServiceTypes = [typeof(ITest1)])]
internal class Test : ITest1;

[IoCRegister]
internal sealed class TestSelf : Test;

internal sealed class TestNone : ITest1;

public interface ITest2 : ITest1;

[IoCRegister]
internal sealed class TestDefaultSettings : ITest2;

[IoCRegister(Key = 10)]
internal sealed class TestKey : ITest2;
[IoCRegister(Key = "Test")]
internal sealed class TestKey2 : ITest2;
[IoCRegister(Key = true)]
internal sealed class TestKey3 : ITest2;

public enum TestEnum
{
    None = 0,
    T1 = 1,
    T2 = 2,
}
[IoCRegister(Key = TestEnum.T1)]
internal sealed class TestKeyEnum : ITest2;

public static class TestExtensions
{
    public const string Key = "TestKey";
}
[IoCRegister(Key = "TestExtensions.Key", KeyType = KeyType.Csharp)]
internal sealed class TestKeyTypeCsharp : ITest2;

public interface IGenericTest<T> : ITest1, ITest2;

[IoCRegister]
public sealed class GenericTest<T> : IGenericTest<T>;

[IoCRegister]
public sealed class ClosedGenericTest : IGenericTest<int>;

public sealed class TestFor : IGenericTest<string>, ITest2;

[IoCRegister]
public sealed class TestInterfaces : IGenericTest<decimal>, ITest2;