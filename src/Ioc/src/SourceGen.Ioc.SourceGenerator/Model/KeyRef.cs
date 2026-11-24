namespace SourceGen.Ioc.Model;

/// <summary>The two forms of a key that is not "no key" (Annotations.spec, "The three forms of a key").</summary>
internal enum KeyKind
{
    /// <summary>A constant value: <c>KeyType.Value</c>, or <c>KeyType.Csharp</c> that points to a <c>const</c>.</summary>
    Value,

    /// <summary>A static field or property that is not <c>const</c>, named with <c>KeyType.Csharp</c>.</summary>
    Symbol,
}

/// <summary>A key (Annotations.spec, "Keys"). "No key" is <see langword="null"/> where a key is optional.</summary>
/// <param name="Kind">The form of the key.</param>
/// <param name="Type">For a value key, the type of the value. For a symbol key, the type that declares the member.</param>
/// <param name="Text">
/// For a value key, the text of the value (Annotations.spec, "Canonical form of a key"). For a symbol key, the member name.
/// </param>
/// <param name="ValueType">For a symbol key, the declared type of the member: the key type. For a value key, <see cref="Type"/>.</param>
/// <param name="Literal">For a value key, the kind of the literal that the generated code writes.</param>
internal sealed record KeyRef(KeyKind Kind, TypeRef Type, string Text, TypeRef ValueType, KeyLiteral Literal = KeyLiteral.None)
{
    /// <summary>Gets the canonical string: <c>V|{type}|{text}</c> or <c>S|{declaring type}|{member}</c>.</summary>
    public string Canonical => field ??= $"{(Kind == KeyKind.Value ? 'V' : 'S')}|{Type.CanonicalString}|{Text}";

    /// <summary>
    /// The key of a declaration whose key is invalid. The key is unknown, so it equals no key that another declaration
    /// can have, and only that declaration is skipped (Diagnostics.spec, "Consequences"). No value key has a class as its
    /// type, so the implementation type makes it unique.
    /// </summary>
    /// <param name="implementation">The implementation type of the declaration.</param>
    /// <returns>The key.</returns>
    public static KeyRef Invalid(TypeRef implementation) => new(KeyKind.Value, implementation, "?", implementation);

    /// <summary>Gets whether this is the key of a declaration whose key is invalid: every valid value key has a literal kind.</summary>
    public bool IsInvalid => Kind == KeyKind.Value && Literal == KeyLiteral.None;

    /// <summary>Gets the canonical string of an optional key: the empty string for no key.</summary>
    /// <param name="key">The key, or <see langword="null"/>.</param>
    /// <returns>The canonical string.</returns>
    public static string CanonicalOf(KeyRef? key) => key?.Canonical ?? string.Empty;

    /// <summary>Compares by value. The cached canonical string is not part of the equality.</summary>
    /// <param name="other">The other key.</param>
    /// <returns>Whether the keys are equal.</returns>
    public bool Equals(KeyRef? other)
        => other is not null && Kind == other.Kind && Literal == other.Literal && Text == other.Text && Type.Equals(other.Type) && ValueType.Equals(other.ValueType);

    /// <inheritdoc/>
    public override int GetHashCode() => (((int)Kind * 397) ^ Text.GetHashCode()) * 397 ^ Type.GetHashCode();
}

/// <summary>How the generated code writes a value key.</summary>
internal enum KeyLiteral
{
    /// <summary>A symbol key: the member access.</summary>
    None,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A UTF-16 code unit, written as a cast of its decimal value.</summary>
    Char,

    /// <summary>A string literal.</summary>
    String,

    /// <summary>An integer type, written as a cast of the decimal value.</summary>
    Integer,

    /// <summary><c>float</c>.</summary>
    Single,

    /// <summary><c>double</c>.</summary>
    Double,

    /// <summary>An enum, written as a cast of the underlying value.</summary>
    Enum,
}
