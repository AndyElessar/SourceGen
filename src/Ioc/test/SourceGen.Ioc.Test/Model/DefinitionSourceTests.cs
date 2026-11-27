using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Test.Model;

/// <summary>The source of an instance definition (Analysis.Model.spec, "Identity" and "Ordering").</summary>
public class DefinitionSourceTests
{
    private static readonly NamedTypeRef Factories = new("App", "App.Factories", null, true);
    private static readonly NamedTypeRef Clock = new("App", "App.IClock", null, true);

    [Test]
    public async Task SortName_Factory_HasArityAndRefKinds()
    {
        DefinitionSource source = new(SourceKind.Factory, Factories, "Create", [new SourceParameter(Clock, "ref "), new SourceParameter(Clock, string.Empty)], 1);

        await Assert.That(source.SortName).IsEqualTo("App.Factories.Create`1(ref App.IClock,App.IClock)");
        await Assert.That(source.CanonicalString).IsEqualTo("[App]App.Factories.Create`1(ref [App]App.IClock,[App]App.IClock)");
    }

    [Test]
    public async Task With_AfterTheStringsAreRead_BuildsTheStringsOfTheCopy()
    {
        DefinitionSource source = new(SourceKind.Factory, Factories, "Create", []);
        _ = source.SortName;
        _ = source.CanonicalString;

        DefinitionSource copy = source with { Member = "Build" };

        await Assert.That(copy.SortName).IsEqualTo("App.Factories.Build()");
        await Assert.That(copy.CanonicalString).IsEqualTo("[App]App.Factories.Build()");
        await Assert.That(copy.Equals(source)).IsFalse();
        await Assert.That(copy.Equals(new DefinitionSource(SourceKind.Factory, Factories, "Build", []))).IsTrue();
    }

    [Test]
    public async Task Equals_CachedStrings_AreNotPartOfTheEquality()
    {
        DefinitionSource read = new(SourceKind.Instance, Factories, "Clock", []);
        DefinitionSource unread = new(SourceKind.Instance, Factories, "Clock", []);
        _ = read.SortName;

        await Assert.That(read.Equals(unread)).IsTrue();
        await Assert.That(read.GetHashCode()).IsEqualTo(unread.GetHashCode());
    }
}
