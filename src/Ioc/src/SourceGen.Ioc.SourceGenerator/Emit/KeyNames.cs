using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Emit;

/// <summary>Writes keys as C# expressions and in messages, and names closed definitions in messages.</summary>
internal static class KeyNames
{
    /// <summary>
    /// The C# expression of a key (Module.Output.spec, "Key"): a value key as a literal whose boxed type is the type of the
    /// key, an enum as a cast, and a symbol key as the member access.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The expression.</returns>
    public static string Expression(KeyRef key)
    {
        string type = TypeNames.CSharp(key.Type);
        return key.Literal switch
        {
            KeyLiteral.None => $"{type}.{Identifier(key.Text)}",
            KeyLiteral.Boolean => key.Text,
            KeyLiteral.String => SymbolDisplay.FormatLiteral(key.Text, quote: true),
            KeyLiteral.Single => Floating(key.Text, "global::System.Single", "F"),
            KeyLiteral.Double => Floating(key.Text, "global::System.Double", "D"),
            _ => $"({type})({key.Text})",
        };
    }

    /// <summary>The key in a message: a literal for a value key, <c>Type.Member</c> for a symbol key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text.</returns>
    public static string Display(KeyRef key) => key.Literal switch
    {
        KeyLiteral.None => $"{TypeNames.Display(key.Type)}.{key.Text}",
        KeyLiteral.String => SymbolDisplay.FormatLiteral(key.Text, quote: true),
        KeyLiteral.Char => SymbolDisplay.FormatLiteral((char)int.Parse(key.Text, System.Globalization.CultureInfo.InvariantCulture), quote: true),
        KeyLiteral.Enum => $"({TypeNames.Display(key.Type)}){key.Text}",
        _ => key.Text,
    };

    /// <summary>
    /// The name of a closed definition in a message: the sort name, then <c> (Key: {Key})</c> when it has a key
    /// (Diagnostics.spec, "Names in messages").
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="key">The key, or <see langword="null"/>.</param>
    /// <returns>The name.</returns>
    public static string Definition(DefinitionSource source, KeyRef? key)
        => key is null ? source.SortName : $"{source.SortName} (Key: {Display(key)})";

    /// <summary>The name of a closed definition in a message.</summary>
    /// <param name="definition">The closed definition.</param>
    /// <returns>The name.</returns>
    public static string Definition(ClosedDefinition definition) => Definition(definition.Source, definition.Key);

    private static string Floating(string text, string type, string suffix) => text switch
    {
        "NaN" => $"{type}.NaN",
        "Infinity" => $"{type}.PositiveInfinity",
        "-Infinity" => $"{type}.NegativeInfinity",
        _ => $"({text}{suffix})",
    };

    private static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : $"@{name}";
}
