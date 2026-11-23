using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>A diagnostic that a test expects: the ID, the file, and the span.</summary>
public readonly record struct ExpectedDiagnostic(string Id, string FilePath, TextSpan Span)
{
    public override string ToString() => $"{Id} at {FilePath}[{Span.Start}..{Span.End})";
}

/// <summary>
/// Source text with markers of the expected diagnostics. <c>{|SGIOC002:Foo|}</c> expects <c>SGIOC002</c> on the
/// text <c>Foo</c>. Markers do not nest.
/// </summary>
public static class DiagnosticMarkup
{
    /// <summary>Removes the markers from each file and returns the clean sources and the expected diagnostics.</summary>
    public static (IReadOnlyList<string> Sources, IReadOnlyList<ExpectedDiagnostic> Expected) Parse(params IReadOnlyList<string> markedSources)
    {
        List<string> sources = [];
        List<ExpectedDiagnostic> expected = [];
        for(int i = 0; i < markedSources.Count; i++)
        {
            (string source, IEnumerable<(string Id, TextSpan Span)> markers) = ParseOne(markedSources[i]);
            sources.Add(source);
            expected.AddRange(markers.Select(m => new ExpectedDiagnostic(m.Id, SourceGeneratorTestHelper.FilePath(i), m.Span)));
        }

        return (sources, expected);
    }

    private static (string Source, IEnumerable<(string Id, TextSpan Span)> Markers) ParseOne(string marked)
    {
        StringBuilder builder = new(marked.Length);
        List<(string, TextSpan)> markers = [];
        int position = 0;
        while(true)
        {
            int open = marked.IndexOf("{|", position, StringComparison.Ordinal);
            if(open < 0)
            {
                builder.Append(marked, position, marked.Length - position);
                return (builder.ToString(), markers);
            }

            int colon = marked.IndexOf(':', open);
            int close = marked.IndexOf("|}", open, StringComparison.Ordinal);
            if(colon < 0 || close < 0 || colon > close)
            {
                throw new FormatException($"A marker at {open} is not '{{|ID:text|}}'.");
            }

            builder.Append(marked, position, open - position);
            int start = builder.Length;
            builder.Append(marked, colon + 1, close - colon - 1);
            markers.Add((marked[(open + 2)..colon], TextSpan.FromBounds(start, builder.Length)));
            position = close + 2;
        }
    }

    /// <summary>
    /// Compares the reported diagnostics whose ID starts with <paramref name="idPrefix"/> with the expected ones, and
    /// returns a description of each difference. The comparison counts duplicates: a diagnostic that is reported two
    /// times at the same span and expected one time gives one "unexpected" entry.
    /// </summary>
    public static IReadOnlyList<string> Compare(IEnumerable<ExpectedDiagnostic> expected, IEnumerable<Diagnostic> actual, string idPrefix = "SGIOC")
    {
        Dictionary<ExpectedDiagnostic, int> balance = [];
        foreach(ExpectedDiagnostic d in expected)
        {
            balance[d] = balance.GetValueOrDefault(d) + 1;
        }

        foreach(Diagnostic d in actual.Where(d => !d.IsSuppressed && d.Id.StartsWith(idPrefix, StringComparison.Ordinal)))
        {
            ExpectedDiagnostic key = new(d.Id, d.Location.SourceTree?.FilePath ?? "<no file>", d.Location.SourceSpan);
            balance[key] = balance.GetValueOrDefault(key) - 1;
        }

        return [
            .. balance.Where(static p => p.Value > 0).SelectMany(static p => Enumerable.Repeat($"missing: {p.Key}", p.Value)),
            .. balance.Where(static p => p.Value < 0).SelectMany(static p => Enumerable.Repeat($"unexpected: {p.Key}", -p.Value)),
        ];
    }

    /// <summary>
    /// Verifies that the generator reports exactly the diagnostics that the markers expect, at the marked spans.
    /// </summary>
    public static async Task VerifyDiagnosticsAsync(this GeneratorTestResult result, IEnumerable<ExpectedDiagnostic> expected, string idPrefix = "SGIOC")
    {
        IReadOnlyList<string> differences = Compare(expected, result.Diagnostics, idPrefix);
        await Assert.That(differences).IsEmpty()
            .Because($"The diagnostics differ:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}{Environment.NewLine}Reported:{Environment.NewLine}{string.Join(Environment.NewLine, result.ReportedDiagnostics)}");
    }
}
