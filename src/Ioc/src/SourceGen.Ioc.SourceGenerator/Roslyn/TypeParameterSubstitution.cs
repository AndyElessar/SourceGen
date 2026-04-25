namespace SourceGen.Ioc.SourceGenerator.Roslyn;

internal static class TypeParameterSubstitution
{
    public static string SubstituteTypeArguments(string typeName, TypeArgMap typeArgMap)
    {
        if(typeArgMap.IsEmpty)
        {
            return typeName;
        }

        // Fast path: check if any substitution is needed
        var typeNameSpan = typeName.AsSpan();
        bool needsSubstitution = false;
        foreach(var (key, _) in typeArgMap)
        {
            if(ContainsTypeParameter(typeNameSpan, key.AsSpan()))
            {
                needsSubstitution = true;
                break;
            }
        }

        if(!needsSubstitution)
        {
            return typeName;
        }

        return SubstituteTypeArgumentsCore(typeNameSpan, typeArgMap.AsSpan());
    }

    public static string ReplaceTypeParameter(string typeName, string typeParam, string actualArg)
    {
        var typeNameSpan = typeName.AsSpan();

        // Fast path: check if substitution is needed
        if(!ContainsTypeParameter(typeNameSpan, typeParam.AsSpan()))
        {
            return typeName;
        }

        // Delegate to core implementation with single-element span
        Span<TypeArgEntry> singleEntry = [new(typeParam, actualArg)];
        return SubstituteTypeArgumentsCore(typeNameSpan, singleEntry);
    }

    private static string SubstituteTypeArgumentsCore(
        ReadOnlySpan<char> typeNameSpan,
        ReadOnlySpan<TypeArgEntry> sortedEntries)
    {
        var result = new StringBuilder(typeNameSpan.Length + 32);
        int i = 0;

        while(i < typeNameSpan.Length)
        {
            // Check if current position is a valid identifier start
            bool isValidStart = i == 0 || !IsIdentifierChar(typeNameSpan[i - 1]);

            if(isValidStart && TryMatchTypeParameter(typeNameSpan, i, sortedEntries, out var match, out int matchLength))
            {
                result.Append(match);
                i += matchLength;
            }
            else
            {
                result.Append(typeNameSpan[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static bool TryMatchTypeParameter(
        ReadOnlySpan<char> typeNameSpan,
        int position,
        ReadOnlySpan<TypeArgEntry> sortedEntries,
        [NotNullWhen(true)] out string? replacement,
        out int matchLength)
    {
        foreach(var (key, value) in sortedEntries)
        {
            var typeParamSpan = key.AsSpan();
            int paramLength = typeParamSpan.Length;

            if(position + paramLength <= typeNameSpan.Length &&
               typeNameSpan.Slice(position, paramLength).SequenceEqual(typeParamSpan))
            {
                // Check if it's a whole word (ends at identifier boundary)
                bool isEnd = position + paramLength == typeNameSpan.Length
                                || !IsIdentifierChar(typeNameSpan[position + paramLength]);

                if(isEnd)
                {
                    replacement = value;
                    matchLength = paramLength;
                    return true;
                }
            }
        }

        replacement = null;
        matchLength = 0;
        return false;
    }

    private static bool ContainsTypeParameter(ReadOnlySpan<char> typeName, ReadOnlySpan<char> typeParam)
    {
        int index = 0;
        while(index <= typeName.Length - typeParam.Length)
        {
            int pos = typeName[index..].IndexOf(typeParam, StringComparison.Ordinal);
            if(pos < 0)
            {
                return false;
            }

            int absolutePos = index + pos;
            bool isStart = absolutePos == 0
                            || !IsIdentifierChar(typeName[absolutePos - 1]);
            bool isEnd = absolutePos + typeParam.Length == typeName.Length
                            || !IsIdentifierChar(typeName[absolutePos + typeParam.Length]);

            if(isStart && isEnd)
            {
                return true;
            }

            index = absolutePos + 1;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}