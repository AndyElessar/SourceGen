namespace SourceGen.Ioc;

internal static class TypeParameterSubstitution
{
    /// <summary>
    /// Replaces the type parameters in a fully qualified type name with their type arguments.
    /// </summary>
    /// <remarks>
    /// All type parameters are replaced in one pass, so a type argument that contains another type parameter's name is not
    /// replaced again. Only identifiers that can be type parameters are replaced: identifiers in a qualified name, such as
    /// <c>T</c> in <c>global::Ns.Keys.T</c>, and tuple element names, such as <c>T</c> in <c>(int T, string Name)</c>,
    /// are kept.
    /// </remarks>
    /// <param name="typeName">The type name, as written by <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/>.</param>
    /// <param name="typeArguments">The type argument of each type parameter, by type parameter name.</param>
    public static string SubstituteTypeArguments(string typeName, Dictionary<string, string>? typeArguments)
    {
        if(typeArguments is null || typeArguments.Count == 0)
        {
            return typeName;
        }

        StringBuilder? result = null;
        int segmentStart = 0;
        int i = 0;

        while(i < typeName.Length)
        {
            if(!IsIdentifierChar(typeName[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while(i < typeName.Length && IsIdentifierChar(typeName[i]))
            {
                i++;
            }

            if(!CanBeTypeParameter(typeName, start, i) || FindTypeArgument(typeName, start, i - start, typeArguments) is not { } typeArgument)
            {
                continue;
            }

            // Flush the unchanged segment before the type parameter in one append
            result ??= new StringBuilder(typeName.Length + 32);
            result.Append(typeName, segmentStart, start - segmentStart);
            result.Append(typeArgument);
            segmentStart = i;
        }

        if(result is null)
        {
            return typeName;
        }

        result.Append(typeName, segmentStart, typeName.Length - segmentStart);
        return result.ToString();
    }

    /// <summary>
    /// Determines whether the identifier at <paramref name="start"/>..<paramref name="end"/> is in a position where a type
    /// parameter can be written.
    /// </summary>
    private static bool CanBeTypeParameter(string typeName, int start, int end)
    {
        // Namespaces, containing types and nested types of a qualified name
        if(end < typeName.Length && typeName[end] == '.')
        {
            return false;
        }

        if(start == 0)
        {
            return true;
        }

        return typeName[start - 1] switch
        {
            '.' or ':' => false,
            // A space follows a separator ("<T, U>", "(T a, U b)") or a tuple element type ("(T a, U b)"); only the former
            // starts a type
            ' ' => start >= 2 && typeName[start - 2] == ',',
            _ => true,
        };
    }

    private static string? FindTypeArgument(string typeName, int start, int length, Dictionary<string, string> typeArguments)
    {
        // Type parameter maps have a few entries, so comparing in place avoids allocating a key for every identifier
        foreach(var entry in typeArguments)
        {
            if(entry.Key.Length == length && string.CompareOrdinal(typeName, start, entry.Key, 0, length) == 0)
            {
                return entry.Value;
            }
        }

        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
