namespace SourceGen.Ioc.TestAot.Tests;

/// <summary>
/// Tests that tuple types written with different element names, which are the same runtime type, resolve the same
/// registrations.
/// </summary>
public sealed class TupleElementNamesTests
{
    [Test]
    public async Task TupleElementNames_StandaloneContainer_ResolvesRegistrationsOfSameRuntimeType()
    {
        using var container = new TupleNamesContainer();

        var consumer = container.GetRequiredService<TupleNamesConsumer>();

        await Assert.That(consumer.Handler).IsTypeOf<TupleNamesUnnamedHandler>();
        await Assert.That(string.Join(",", consumer.Handlers.Select(static h => h.GetType().Name).Order(StringComparer.Ordinal))).IsEqualTo($"{nameof(TupleNamesNamedHandler)},{nameof(TupleNamesUnnamedHandler)}");
        await Assert.That(container.GetRequiredService<ITupleNamesHandler<(TupleNamesEntity, string)>>()).IsSameReferenceAs(consumer.Handler);
    }

    [Test]
    public async Task TupleElementNames_MsDi_ResolvesRegistrationsOfSameRuntimeType()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<TupleNamesConsumer>();

        await Assert.That(consumer.Handler).IsTypeOf<TupleNamesUnnamedHandler>();
        await Assert.That(string.Join(",", consumer.Handlers.Select(static h => h.GetType().Name).Order(StringComparer.Ordinal))).IsEqualTo($"{nameof(TupleNamesNamedHandler)},{nameof(TupleNamesUnnamedHandler)}");
    }

    [Test]
    public async Task TupleElementNames_OpenGeneric_MsDi_ClosesOnceForSameRuntimeType()
    {
        var services = new ServiceCollection();
        services.AddSourceGen_Ioc_TestAot();
        await using var provider = services.BuildServiceProvider();

        var consumer = provider.GetRequiredService<TupleNamesGenericConsumer>();
        var other = provider.GetRequiredService<TupleNamesGenericOtherConsumer>();

        // MS.DI also closes the open generic registration for a collection of the service type, which Native AOT cannot
        // do for a value type argument, so only the wrappers are counted
        await Assert.That(provider.GetServices<Lazy<ITupleNamesGenericHandler<(TupleNamesEntity, string)>>>().Count()).IsEqualTo(1);
        await Assert.That(provider.GetServices<Func<ITupleNamesGenericHandler<(TupleNamesEntity, string)>>>().Count()).IsEqualTo(1);
        await Assert.That(other.Lazy).IsSameReferenceAs(consumer.Lazy);
        await Assert.That(other.Func).IsSameReferenceAs(consumer.Func);
    }

    [Test]
    public async Task TupleElementNames_OpenGeneric_StandaloneContainer_ClosesOnceForSameRuntimeType()
    {
        using var container = new TupleNamesContainer();

        var consumer = container.GetRequiredService<TupleNamesGenericConsumer>();
        var other = container.GetRequiredService<TupleNamesGenericOtherConsumer>();

        await Assert.That(container.GetServices<ITupleNamesGenericHandler<(TupleNamesEntity, string)>>().Count()).IsEqualTo(1);
        await Assert.That(other.Lazy).IsSameReferenceAs(consumer.Lazy);
        await Assert.That(other.Func).IsSameReferenceAs(consumer.Func);
    }
}
