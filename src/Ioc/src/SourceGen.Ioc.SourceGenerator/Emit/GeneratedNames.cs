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
        string metadataName = type.MetadataName;
        int start = Math.Max(metadataName.LastIndexOf('.'), metadataName.LastIndexOf('+')) + 1;
        int tick = metadataName.IndexOf('`', start);
        string shortName = tick >= 0 ? metadataName[start..tick] : metadataName[start..];
        string identity = definition.Key is { } key ? $"{type.CanonicalString}|{key.Canonical}" : type.CanonicalString;
        return $"{prefix}{shortName}_{Fnv1a(identity):x8}";
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
