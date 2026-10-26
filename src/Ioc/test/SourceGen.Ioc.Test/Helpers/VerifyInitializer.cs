using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace SourceGen.Ioc.Test.Helpers;

internal static partial class VerifyInitializer
{
    /// <summary>
    /// Replaces the generator version in <c>[GeneratedCode]</c>, which changes with every commit, so snapshots stay stable.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
        => VerifierSettings.ScrubLinesWithReplace(static line => GeneratedCodeVersion().Replace(line, "${prefix}{version}"));

    [GeneratedRegex("""(?<prefix>GeneratedCode\("SourceGen\.Ioc", ")[^"]*(?=")""")]
    private static partial Regex GeneratedCodeVersion();
}
