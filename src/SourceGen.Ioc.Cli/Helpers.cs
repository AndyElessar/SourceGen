namespace SourceGen.Ioc.Cli;

public static partial class Helpers
{
    // Matches: (public|internal) [modifiers] class ClassName
    // Group 1: access modifier (public|internal)
    // Group 2: class name (only word characters matching the pattern)
    private const string baseClassRegex_1 = @"(public|internal)\s+(?!static\s+)[\w\s]*class\s+(";
    private const string baseClassRegex_2 = @")(?=\s|:|$)";

    public static Regex CreateFullMatchRegex(string fullRegex) =>
        new Regex(
            fullRegex,
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(1000));

    public static Regex CreateClassMatchRegex(string classNameRegex)
    {
        // Replace .* and .+ with \w* and \w+ to ensure class names only contain identifier characters
        var sanitizedPattern = classNameRegex
            .Replace(".*", @"\w*")
            .Replace(".+", @"\w+");

        return new Regex(
            string.Concat(baseClassRegex_1, sanitizedPattern, baseClassRegex_2),
            RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(1000));
    }

    /// <summary>
    /// Extracts class name from a regex match that used CreateClassMatchRegex.
    /// The class name is captured in group 2.
    /// </summary>
    /// <param name="match">The regex match result.</param>
    /// <returns>The class name, or the full match value if group 2 is not available.</returns>
    public static string ExtractClassName(Match match) =>
        match.Groups.Count > 2 && match.Groups[2].Success
            ? match.Groups[2].Value
            : match.Value;

    [GeneratedRegex(
        @"namespace\s+([\w.]+)\s*[;{]",
        RegexOptions.CultureInvariant,
        500)]
    private static partial Regex NamespaceRegex { get; }

    /// <summary>
    /// Extracts the namespace from file content.
    /// </summary>
    /// <param name="fileContent">The C# file content.</param>
    /// <returns>The namespace if found, otherwise null.</returns>
    public static string? ExtractNamespace(string fileContent)
    {
        var match = NamespaceRegex.Match(fileContent);

        return match.Success && match.Groups.Count > 1
            ? match.Groups[1].Value
            : null;
    }
}
