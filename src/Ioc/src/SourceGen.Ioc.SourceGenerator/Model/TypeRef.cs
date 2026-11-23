namespace SourceGen.Ioc.Model;

/// <summary>
/// A type in the analysis model (Analysis.Model.spec, "Type model"). It holds no Roslyn symbol and compares by value.
/// </summary>
internal closed record TypeRef
{
    /// <summary>Initializes a new instance of the <see cref="TypeRef"/> class.</summary>
    protected TypeRef()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeRef"/> class for a <c>with</c> expression. It copies nothing: the
    /// cached strings belong to the original, and the copy can have other members, so it builds its own.
    /// </summary>
    /// <param name="original">The original.</param>
#pragma warning disable IDE0060 // A record copy constructor must take the original, and this one copies nothing from it
    protected TypeRef(TypeRef original)
    {
    }
#pragma warning restore IDE0060

    /// <summary>
    /// Gets a value indicating whether each node of the type is accessible from another assembly. A type parameter is
    /// always accessible.
    /// </summary>
    public abstract bool IsFullyAccessible { get; }

    /// <summary>Gets the canonical string, with the assembly of each named type. It is valid only inside one compilation.</summary>
    /// <remarks>The string is built on the first read and cached. The cache is not part of the equality.</remarks>
    public string CanonicalString => field ??= Build(withAssembly: true);

    /// <summary>Gets the canonical string without the <c>[{Assembly}]</c> parts, which the ordering compares first.</summary>
    /// <remarks>The string is built on the first read and cached. The cache is not part of the equality.</remarks>
    public string SortName => field ??= Build(withAssembly: false);

    /// <summary>Appends the canonical string.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="withAssembly">Whether each named type starts with <c>[{Assembly}]</c>.</param>
    internal abstract void Append(StringBuilder builder, bool withAssembly);

    /// <summary>
    /// Compares only the kind of the type. The derived records compare their own members. This replaces the synthesized
    /// equality, which would also compare the cached strings.
    /// </summary>
    /// <param name="other">The other type.</param>
    /// <returns>Whether the kinds are the same.</returns>
    public virtual bool Equals(TypeRef? other) => other is not null && EqualityContract == other.EqualityContract;

    /// <inheritdoc/>
    public override int GetHashCode() => EqualityContract.GetHashCode();

    /// <inheritdoc/>
    public sealed override string ToString() => CanonicalString;

    private string Build(bool withAssembly)
    {
        StringBuilder builder = new();
        Append(builder, withAssembly);
        return builder.ToString();
    }
}

/// <summary>A named type: a class, an interface, a struct, an enum, or a delegate.</summary>
/// <param name="Assembly">The simple name of the assembly that defines the type.</param>
/// <param name="MetadataName">The full metadata name. Nested types are separated by <c>+</c>, and generic names end with <c>`n</c>.</param>
/// <param name="Arguments">
/// The type arguments, flat: those of the outer types first. <see langword="null"/> for a non-generic type and for an
/// unbound generic. It is never empty.
/// </param>
/// <param name="IsAccessible">Whether this node is accessible from another assembly. The type arguments do not count.</param>
internal sealed record NamedTypeRef(string Assembly, string MetadataName, ImmutableEquatableArray<TypeRef>? Arguments, bool IsAccessible) : TypeRef
{
    /// <inheritdoc/>
    public override bool IsFullyAccessible => IsAccessible && (Arguments is null || Arguments.All(static a => a.IsFullyAccessible));

    /// <summary>Gets a value indicating whether the type is generic and has no type arguments.</summary>
    public bool IsUnboundGeneric => Arguments is null && MetadataName.IndexOf('`') >= 0;

    /// <summary>Gets the same type without its type arguments: the original definition.</summary>
    public NamedTypeRef Definition => Arguments is null ? this : this with { Arguments = null };

    /// <inheritdoc/>
    internal override void Append(StringBuilder builder, bool withAssembly)
    {
        if(withAssembly)
        {
            builder.Append('[').Append(Assembly).Append(']');
        }

        builder.Append(MetadataName);
        if(Arguments is null)
        {
            return;
        }

        builder.Append('<');
        for(int i = 0; i < Arguments.Length; i++)
        {
            if(i > 0)
            {
                builder.Append(',');
            }

            Arguments[i].Append(builder, withAssembly);
        }

        builder.Append('>');
    }
}

/// <summary>An array type.</summary>
/// <param name="Element">The element type.</param>
/// <param name="Rank">The number of dimensions, 1 at least.</param>
internal sealed record ArrayTypeRef(TypeRef Element, int Rank) : TypeRef
{
    /// <inheritdoc/>
    public override bool IsFullyAccessible => Element.IsFullyAccessible;

    /// <inheritdoc/>
    internal override void Append(StringBuilder builder, bool withAssembly)
    {
        Element.Append(builder, withAssembly);
        builder.Append('[').Append(',', Rank - 1).Append(']');
    }
}

/// <summary>A type parameter of the declaration that owns it: an open implementation type, a generic factory method, or a decorator.</summary>
/// <param name="Index">The index of the type parameter in the flat list of its declaration: those of the outer types first.</param>
internal sealed record TypeParameterRef(int Index) : TypeRef
{
    /// <inheritdoc/>
    public override bool IsFullyAccessible => true;

    /// <inheritdoc/>
    internal override void Append(StringBuilder builder, bool withAssembly) => builder.Append('!').Append(Index);
}
