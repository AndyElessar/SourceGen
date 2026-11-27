using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Test.Model;

/// <summary>A key (Annotations.spec, "Canonical form of a key").</summary>
public class KeyRefTests
{
    private static readonly NamedTypeRef String = new("System.Runtime", "System.String", null, true);

    [Test]
    public async Task With_AfterTheCanonicalStringIsRead_BuildsTheStringOfTheCopy()
    {
        KeyRef key = new(KeyKind.Value, String, "a", String, KeyLiteral.String);
        _ = key.Canonical;

        KeyRef copy = key with { Text = "b" };

        await Assert.That(copy.Canonical).IsEqualTo("V|[System.Runtime]System.String|b");
        await Assert.That(copy.Equals(key)).IsFalse();
    }

    [Test]
    public async Task Equals_CachedString_IsNotPartOfTheEquality()
    {
        KeyRef read = new(KeyKind.Value, String, "a", String, KeyLiteral.String);
        KeyRef unread = new(KeyKind.Value, String, "a", String, KeyLiteral.String);
        _ = read.Canonical;

        await Assert.That(read.Equals(unread)).IsTrue();
        await Assert.That(read.GetHashCode()).IsEqualTo(unread.GetHashCode());
    }
}
