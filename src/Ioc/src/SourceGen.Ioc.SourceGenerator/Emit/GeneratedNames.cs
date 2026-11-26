using SourceGen.Ioc.Analysis;
using SourceGen.Ioc.Model;

namespace SourceGen.Ioc.Emit;

/// <summary>
/// The names of generated members. A name is a prefix, the short type name, and an FNV-1a hash of the sort name and the
/// key (Module.Output.spec, "Construction entry"). The hash is stable across processes, so the output is deterministic.
/// </summary>
internal static class GeneratedNames
{
    /// <summary>Gets the name of a member that belongs to <paramref name="definition"/>.</summary>
    /// <param name="prefix">The prefix, for example <c>__Create_</c>.</param>
    /// <param name="definition">The closed definition.</param>
    /// <returns>The member name.</returns>
    public static string For(string prefix, ClosedDefinition definition)
    {
        NamedTypeRef type = definition.Implementation;
        string identity = definition.Key is { } key ? $"{type.CanonicalString}|{key.Canonical}" : type.CanonicalString;
        return $"{prefix}{ShortName(type)}_{Fnv1a(identity):x8}";
    }

    /// <summary>Gets the name of a member that belongs to a collection.</summary>
    /// <param name="prefix">The prefix, for example <c>__All_</c>.</param>
    /// <param name="collection">The collection.</param>
    /// <returns>The member name.</returns>
    public static string For(string prefix, CollectionPlan collection)
        => $"{prefix}{ShortName(collection.Element)}_{Fnv1a($"{collection.Element.CanonicalString}|{collection.KeyIdentity}"):x8}";

    /// <summary>Gets the name of a member that belongs to a wrapper.</summary>
    /// <param name="prefix">The prefix, for example <c>__Wrap_</c>.</param>
    /// <param name="wrapper">The wrapper.</param>
    /// <returns>The member name.</returns>
    public static string For(string prefix, WrapperPlan wrapper)
        => $"{prefix}{ShortName(wrapper.Type)}{ShortName(wrapper.Request.Core)}_{Fnv1a(wrapper.Identity):x8}";

    /// <summary>
    /// Gets the name of a member that creates a closed definition with the arguments of a <c>Func&lt;A1, ..., An, T&gt;</c>.
    /// The arguments decide the mapping, so <c>Func&lt;A..., T&gt;</c> and <c>Func&lt;A..., Task&lt;T&gt;&gt;</c> share it.
    /// </summary>
    /// <param name="prefix">The prefix, for example <c>__CreateWith_</c>.</param>
    /// <param name="definition">The closed definition.</param>
    /// <param name="arguments">The argument types of the <c>Func</c>.</param>
    /// <returns>The member name.</returns>
    public static string For(string prefix, ClosedDefinition definition, ImmutableEquatableArray<TypeRef> arguments)
    {
        NamedTypeRef type = definition.Implementation;
        string identity = $"{type.CanonicalString}|{KeyRef.CanonicalOf(definition.Key)}|{string.Join(",", arguments.Select(static a => a.CanonicalString))}";
        return $"{prefix}{ShortName(type)}_{Fnv1a(identity):x8}";
    }

    /// <summary>Gets the name of a member that belongs to a <c>KeyValuePair</c>.</summary>
    /// <param name="prefix">The prefix, for example <c>__Pair_</c>.</param>
    /// <param name="pair">The pair.</param>
    /// <returns>The member name.</returns>
    public static string For(string prefix, PairPlan pair)
        => $"{prefix}{ShortName(pair.Request.Core)}_{Fnv1a(pair.Identity):x8}";

    /// <summary>Gets the name of a member that belongs to a collection of pairs or a dictionary.</summary>
    /// <param name="prefix">The prefix, for example <c>__Keyed_</c>.</param>
    /// <param name="keyed">The collection of pairs or the dictionary.</param>
    /// <returns>The member name.</returns>
    public static string For(string prefix, KeyedCollectionPlan keyed)
        => $"{prefix}{ShortName(keyed.Request.Core)}_{Fnv1a(keyed.Identity):x8}";

    /// <summary>The short name of a type: the name of a named type without its namespace and arity.</summary>
    private static string ShortName(TypeRef type)
    {
        switch(type)
        {
            case NamedTypeRef named:
                string metadataName = named.MetadataName;
                int start = Math.Max(metadataName.LastIndexOf('.'), metadataName.LastIndexOf('+')) + 1;
                int tick = metadataName.IndexOf('`', start);
                return tick >= 0 ? metadataName[start..tick] : metadataName[start..];
            case ArrayTypeRef array:
                return ShortName(array.Element) + "Array";
            default:
                return "T";
        }
    }

    /// <summary>The 32-bit FNV-1a hash of the UTF-16 code units of <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The hash.</returns>
    public static uint Fnv1a(string value)
    {
        uint hash = 2166136261;
        foreach(char c in value)
        {
            hash = (hash ^ (byte)c) * 16777619;
            hash = (hash ^ (byte)(c >> 8)) * 16777619;
        }

        return hash;
    }
}
