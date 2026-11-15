namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that a generic factory is called with the type argument of a placeholder in an array or tuple element.
/// </summary>
public sealed class GenericFactoryArrayTupleTests
{
    [Test]
    public async Task GenericFactoryArrayTuple_StandaloneContainer_CallsFactoryWithElementTypeArgument()
    {
        using var container = new ArrayTupleContainer();

        await Assert.That(container.GetRequiredService<IArrayTupleHandler<ArrayTupleEntity[]>>().CreatedFor).IsEqualTo(typeof(ArrayTupleEntity));
        await Assert.That(container.GetRequiredService<IArrayTupleHandler<(ArrayTupleEntity, string)>>().CreatedFor).IsEqualTo(typeof(ArrayTupleEntity));
    }

    [Test]
    public async Task GenericFactoryArrayTuple_MsDi_CallsFactoryWithElementTypeArgument()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<IArrayTupleHandler<ArrayTupleEntity[]>>().CreatedFor).IsEqualTo(typeof(ArrayTupleEntity));
        await Assert.That(provider.GetRequiredService<IArrayTupleHandler<(ArrayTupleEntity, string)>>().CreatedFor).IsEqualTo(typeof(ArrayTupleEntity));
    }
}
