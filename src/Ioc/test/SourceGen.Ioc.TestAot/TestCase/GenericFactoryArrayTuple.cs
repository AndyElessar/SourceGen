namespace SourceGen.Ioc.TestAot.TestCase;

/// <summary>Service type whose generic factory mapping has its placeholder in an array or tuple element.</summary>
public interface IArrayTupleHandler<T>
{
    Type CreatedFor { get; }
}

public sealed class ArrayTupleEntity;

public sealed class ArrayTupleHandler<T>(Type createdFor) : IArrayTupleHandler<T>
{
    public Type CreatedFor => createdFor;
}

public static class ArrayTupleHandlerFactory
{
    [IocGenericFactory(typeof(IArrayTupleHandler<int[]>), typeof(int))]
    public static IArrayTupleHandler<T[]> CreateArray<T>() => new ArrayTupleHandler<T[]>(typeof(T));

    [IocGenericFactory(typeof(IArrayTupleHandler<(int, string)>), typeof(int))]
    public static IArrayTupleHandler<(T, string)> CreateTuple<T>() => new ArrayTupleHandler<(T, string)>(typeof(T));
}

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IArrayTupleHandler<ArrayTupleEntity[]>)], Factory = nameof(ArrayTupleHandlerFactory.CreateArray))]
public sealed class ArrayTupleArrayRegistration : IArrayTupleHandler<ArrayTupleEntity[]>
{
    public Type CreatedFor => throw new NotSupportedException();
}

[IocRegister(ServiceLifetime.Singleton, ServiceTypes = [typeof(IArrayTupleHandler<(ArrayTupleEntity, string)>)], Factory = nameof(ArrayTupleHandlerFactory.CreateTuple))]
public sealed class ArrayTupleTupleRegistration : IArrayTupleHandler<(ArrayTupleEntity, string)>
{
    public Type CreatedFor => throw new NotSupportedException();
}

[IocContainer]
public sealed partial class ArrayTupleContainer;
