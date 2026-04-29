namespace SourceGen.Ioc.SourceGenerator;

internal static partial class RoslynExtensions
{
    /// <summary>
    /// Converts a string to a safe C# namespace name.
    /// Replaces invalid characters (like '-') with underscores, preserving '.' as namespace separator.
    /// </summary>
    /// <param name="name">The namespace name to convert.</param>
    /// <returns>A safe C# namespace name.</returns>
    public static string GetSafeNamespace(string name)
    {
        if(string.IsNullOrWhiteSpace(name))
            return "Generated";

        ReadOnlySpan<char> nameSpan = name.AsSpan();

        // Check if first char is digit (needs underscore prefix)
        var needsPrefix = nameSpan.Length > 0 && char.IsDigit(nameSpan[0]);
        var maxLength = nameSpan.Length + (needsPrefix ? 1 : 0);

        // Use stackalloc for small strings (up to 256 chars), otherwise use array pool
        const int StackAllocThreshold = 256;
        Span<char> buffer = maxLength <= StackAllocThreshold
            ? stackalloc char[StackAllocThreshold]
            : new char[maxLength];

        var writeIndex = 0;

        if(needsPrefix)
        {
            buffer[writeIndex++] = '_';
        }

        for(var i = 0; i < nameSpan.Length; i++)
        {
            var ch = nameSpan[i];
            // Allow letters, digits, underscore, and dot (namespace separator)
            buffer[writeIndex++] = char.IsLetterOrDigit(ch) || ch is '_' or '.' ? ch : '_';
        }

        return buffer[..writeIndex].ToString();
    }

    /// <summary>
    /// Converts a string to a safe C# identifier.
    /// Removes all <c>global::</c> prefixes and replaces non-identifier characters with underscores.
    /// Uses stack allocation for small strings to reduce heap allocations.
    /// </summary>
    /// <param name="name">The name to convert.</param>
    /// <param name="fallback">The fallback value if name is null or whitespace. Default is "Generated".</param>
    /// <returns>A safe C# identifier.</returns>
    public static string GetSafeIdentifier(string name, string fallback = "Generated")
    {
        if(string.IsNullOrWhiteSpace(name))
            return fallback;

        // Remove all global:: prefixes (not just the first one, e.g., in generic types)
        var processedName = name.Replace("global::", "");

        ReadOnlySpan<char> nameSpan = processedName.AsSpan();

        // Check if first char is digit (needs underscore prefix)
        var needsPrefix = nameSpan.Length > 0 && char.IsDigit(nameSpan[0]);
        var maxLength = nameSpan.Length + (needsPrefix ? 1 : 0);

        // Use stackalloc for small strings (up to 256 chars), otherwise use array pool
        const int StackAllocThreshold = 256;
        Span<char> buffer = maxLength <= StackAllocThreshold
            ? stackalloc char[StackAllocThreshold]
            : new char[maxLength];

        var writeIndex = 0;

        if(needsPrefix)
        {
            buffer[writeIndex++] = '_';
        }

        for(var i = 0; i < nameSpan.Length; i++)
        {
            var ch = nameSpan[i];
            buffer[writeIndex++] = char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_';
        }

        return buffer[..writeIndex].ToString();
    }
}