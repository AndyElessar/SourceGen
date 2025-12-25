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
    public static readonly Guid Key = Guid.NewGuid();
}
[IoCRegister(Key = "TestExtensions.Key", KeyType = KeyType.Csharp)]
internal sealed class TestKeyTypeCsharp : ITest2;

[IoCRegister(Key = nameof(TestExtensions.Key), KeyType = KeyType.Csharp)]
internal sealed class TestKeyTypeCsharp2 : ITest2;


public interface IGenericTest<T> : ITest1, ITest2;

[IoCRegister]
public sealed class GenericTest<T> : IGenericTest<T>;

public interface IGenericTest<T1, T2> : ITest1, ITest2;

[IoCRegister]
public sealed class GenericTest<T1, T2> : IGenericTest<T1, T2>;

[IoCRegister]
public sealed class ClosedGenericTest : IGenericTest<int>;

[IoCRegister]
public sealed class ClosedGenericTest2 : IGenericTest<int, string>;

public sealed class TestFor : IGenericTest<string>, ITest2;

//[IoCRegister(Lifetime = ServiceLifetime.Transient)]
public sealed class TestInterfaces/*(TestClosed2 testClosed2)*/ : IGenericTest<decimal>, ITest2
{
    //private readonly TestClosed2 _testClosed2 = testClosed2;
}

public interface IGenericTest2<T>;

[IoCRegister(Lifetime = ServiceLifetime.Singleton)]
public sealed class TestClosed2(TestInterfaces testInterfaces) : IGenericTest<IGenericTest2<int>>
{
    private readonly TestInterfaces _testInterfaces = testInterfaces;
}

[IoCRegister]
public sealed class TestOpenGeneric2<T> : IGenericTest2<IGenericTest2<T>>;

internal abstract class GenericTest3<T>;

[IoCRegister(RegisterAllInterfaces = true)]
internal sealed class TestOpenGeneric3<T> : GenericTest3<T>;

//[IoCRegister]
public abstract class AbstractTest : ITest1
{
    //[IoCRegister]
    private class ErrorTest : ITest1;
}