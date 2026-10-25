using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using static System.FormattableString;

namespace SourceGen.Ioc.GeneratorBenchmark;

/// <summary>
/// Compares benchmark results produced by the same harness for a baseline and a head build.
/// Results are BenchmarkDotNet JSON reports, a directory containing them, or a baseline file written by <see cref="Export"/>.
/// </summary>
internal static class BenchmarkComparison
{
    /// <summary>
    /// Writes a Markdown comparison to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="acceptedPath">An updated baseline whose allocations are accepted even when they regress from <paramref name="baselinePath"/>.</param>
    /// <returns>1 when allocations regress beyond <paramref name="thresholdPercent"/>, or a head result is missing, failed or lacks allocation data, unless the updated baseline accepts it; otherwise 0.</returns>
    public static int Run(string baselinePath, string headPath, string outputPath, double thresholdPercent, string? acceptedPath = null)
    {
        var baseline = Load(baselinePath);
        var head = Load(headPath);
        if(head.Measurements.Count == 0)
        {
            throw new InvalidOperationException($"No benchmark results were found in '{headPath}'.");
        }

        if(acceptedPath is not null && !File.Exists(acceptedPath) && !Directory.Exists(acceptedPath))
        {
            throw new FileNotFoundException($"The updated baseline '{acceptedPath}' does not exist.", acceptedPath);
        }

        var accepted = acceptedPath is null ? null : Load(acceptedPath);
        Row[] rows = [.. baseline.Measurements.Keys.Union(head.Measurements.Keys)
            .OrderBy(static key => key.Parameters, StringComparer.Ordinal)
            .ThenBy(static key => key.Method, StringComparer.Ordinal)
            .Select(key => Classify(key, baseline.Measurements.GetValueOrDefault(key), head.Measurements.GetValueOrDefault(key), accepted, thresholdPercent))];

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, Format(rows, thresholdPercent, baseline, head.Host));
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            WriteAnnotations(rows);
        }

        return rows.Any(static row => row.Status is Status.AllocationRegression or Status.MissingHead or Status.MissingAllocation) ? 1 : 0;
    }

    /// <summary>
    /// Writes the results in <paramref name="resultsPath"/> to <paramref name="outputPath"/> as a baseline file.
    /// </summary>
    /// <exception cref="InvalidOperationException">A benchmark failed or has no allocation data, so the baseline would be incomplete.</exception>
    public static int Export(string resultsPath, string outputPath, string source)
    {
        var report = Load(resultsPath);
        if(report.Measurements.Count == 0)
        {
            throw new InvalidOperationException($"No benchmark results were found in '{resultsPath}'.");
        }

        var incomplete = report.Measurements
            .Where(static pair => pair.Value.Failed || pair.Value.Allocated is null)
            .Select(static pair => $"{pair.Key.Method} [{DisplayParameters(pair.Key.Parameters)}]: {(pair.Value.Failed ? "failed" : "no allocation data")}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if(incomplete.Length > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to export an incomplete baseline from '{resultsPath}':{Environment.NewLine}{string.Join(Environment.NewLine, incomplete)}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var stream = File.Create(outputPath);
        using(var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("source", source);
            if(report.Host is { } host)
            {
                writer.WriteStartObject("host");
                writer.WriteString("processor", host.Processor);
                writer.WriteString("os", host.Os);
                writer.WriteString("runtime", host.Runtime);
                writer.WriteString("benchmarkDotNet", host.BenchmarkDotNet);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("benchmarks");
            foreach(var (key, measurement) in report.Measurements
                .OrderBy(static pair => pair.Key.Parameters, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Key.Method, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("method", key.Method);
                writer.WriteString("parameters", key.Parameters);
                WriteNumber(writer, "meanNs", measurement.Mean);
                WriteNumber(writer, "lowerNs", measurement.Lower);
                WriteNumber(writer, "upperNs", measurement.Upper);
                writer.WriteNumber("allocatedBytes", measurement.Allocated!.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        Console.WriteLine($"Exported {report.Measurements.Count} benchmark results to '{outputPath}'.");
        return 0;
    }

    private static Report Load(string path)
    {
        Dictionary<(string Parameters, string Method), Measurement> measurements = [];
        Host? host = null;
        string? source = null;
        string[] files = File.Exists(path) ? [path]
            : Directory.Exists(path) ? [.. Directory.EnumerateFiles(path, "*-report*.json").Order(StringComparer.Ordinal)]
            : [];

        foreach(var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = document.RootElement;
            if(root.TryGetProperty("Benchmarks", out var benchmarks))
            {
                if(host is null && root.TryGetProperty("HostEnvironmentInfo", out var info))
                {
                    host = new(Text(info, "ProcessorName") ?? "unknown", Text(info, "OsVersion") ?? "unknown",
                        Text(info, "RuntimeVersion") ?? "unknown", Text(info, "BenchmarkDotNetVersion") ?? "unknown");
                }

                foreach(var benchmark in benchmarks.EnumerateArray())
                {
                    var key = (Text(benchmark, "Parameters") ?? "", Text(benchmark, "Method") ?? "unknown");

                    // Failed benchmarks have no statistics; keeping them as failed measurements fails the gate instead of dropping them.
                    if(!benchmark.TryGetProperty("Statistics", out var statistics) || Number(statistics, "Mean") is double.NaN)
                    {
                        measurements.TryAdd(key, Measurement.FailedRun);
                        continue;
                    }

                    var interval = statistics.TryGetProperty("ConfidenceInterval", out var value) ? value : default;
                    var memory = benchmark.TryGetProperty("Memory", out value) ? value : default;
                    measurements.TryAdd(
                        key,
                        new(Number(statistics, "Mean"), Number(interval, "Lower"), Number(interval, "Upper"), Integer(memory, "BytesAllocatedPerOperation")));
                }
            }
            else
            {
                source ??= Text(root, "source");
                if(host is null && root.TryGetProperty("host", out var info))
                {
                    host = new(Text(info, "processor") ?? "unknown", Text(info, "os") ?? "unknown",
                        Text(info, "runtime") ?? "unknown", Text(info, "benchmarkDotNet") ?? "unknown");
                }

                foreach(var benchmark in root.GetProperty("benchmarks").EnumerateArray())
                {
                    if(Number(benchmark, "meanNs") is double.NaN)
                    {
                        continue;
                    }

                    measurements.TryAdd(
                        (Text(benchmark, "parameters") ?? "", Text(benchmark, "method") ?? "unknown"),
                        new(Number(benchmark, "meanNs"), Number(benchmark, "lowerNs"), Number(benchmark, "upperNs"), Integer(benchmark, "allocatedBytes")));
                }
            }
        }

        return new(measurements, host, source);
    }

    private static Row Classify((string Parameters, string Method) key, Measurement? baseline, Measurement? head, Report? accepted, double threshold)
    {
        if(head is null || head.Failed)
        {
            // An updated baseline that drops the benchmark accepts its removal; a failed run is never accepted.
            var removal = head is null && accepted is not null && !accepted.Measurements.ContainsKey(key) ? Status.Accepted : Status.MissingHead;
            return new(key.Parameters, key.Method, baseline, head, double.NaN, double.NaN, removal);
        }

        // Without allocation data the gate cannot detect regressions, so a disabled MemoryDiagnoser fails the check.
        if(head.Allocated is not { } headBytes)
        {
            return new(key.Parameters, key.Method, baseline, head, double.NaN, double.NaN, Status.MissingAllocation);
        }

        if(baseline is null || baseline.Failed)
        {
            return new(key.Parameters, key.Method, baseline, head, double.NaN, double.NaN, Status.NoBaseline);
        }

        var time = Change(baseline.Mean, head.Mean);
        var acceptedAllocation = accepted?.Measurements.GetValueOrDefault(key) is { Allocated: { } acceptedBytes }
            ? Change(acceptedBytes, headBytes)
            : double.NaN;
        if(baseline.Allocated is not { } baselineBytes)
        {
            // A baseline without allocation data cannot gate the head unless an updated baseline supplies it.
            var status = acceptedAllocation <= threshold ? Status.Accepted : Status.MissingAllocation;
            return new(key.Parameters, key.Method, baseline, head, time, double.NaN, status);
        }

        var allocation = Change(baselineBytes, headBytes);

        // Comparisons with NaN are false, so a missing confidence interval never flags a time change.
        var slower = time > threshold && head.Lower > baseline.Upper;
        var faster = time < -threshold && head.Upper < baseline.Lower;
        var result = (allocation > threshold, slower, faster || allocation < -threshold) switch
        {
            (true, _, _) when acceptedAllocation <= threshold => Status.Accepted,
            (true, _, _) => Status.AllocationRegression,
            (_, true, _) => Status.Slower,
            (_, _, true) => Status.Improved,
            _ => Status.Unchanged,
        };
        return new(key.Parameters, key.Method, baseline, head, time, allocation, result);
    }

    private static double Change(double baseline, double head) => baseline switch
    {
        > 0 => (head - baseline) / baseline * 100,
        _ when head > 0 => double.PositiveInfinity,
        _ => 0,
    };

    private static string Format(IReadOnlyList<Row> rows, double threshold, Report baseline, Host? headHost)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(" · ", rows
            .GroupBy(static row => row.Status)
            .OrderBy(static group => group.Key)
            .Select(static group => Invariant($"{Label(group.Key)}: {group.Count()}"))));
        builder.AppendLine();
        builder.AppendLine("| Benchmark | Parameters | Mean time (baseline → head) | Δ time | Allocated (baseline → head) | Δ allocated | Result |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | --- |");
        foreach(var row in rows)
        {
            builder.AppendLine(Invariant($"| {row.Method} | {DisplayParameters(row.Parameters).Replace("|", "\\|")} | {FormatTime(row.Baseline?.Mean)} → {FormatTime(row.Head?.Mean)} | {FormatChange(row.TimeChange)} | {FormatBytes(row.Baseline?.Allocated)} → {FormatBytes(row.Head?.Allocated)} | {FormatChange(row.AllocationChange)} | {Label(row.Status)} |"));
        }

        builder.AppendLine();
        builder.Append(Invariant($"Allocated is managed memory per operation. An allocation increase above {threshold}% fails the check unless an updated baseline accepts it. A time change above {threshold}% is reported only when the 99.9% confidence intervals do not overlap, and never fails the check because shared runners are noisy."));
        if(rows.Any(static row => row.Status is Status.Accepted))
        {
            builder.Append(" Accepted results differ from the baseline but match the updated baseline committed with this change.");
        }

        builder.AppendLine();
        if(baseline.Source is { } source)
        {
            builder.AppendLine();
            builder.AppendLine($"The baseline was measured from `{source.Replace("`", "'").ReplaceLineEndings(" ")}`.");
        }

        var baselineHost = baseline.Host;
        if(baselineHost is not null && headHost is not null && baselineHost != headHost)
        {
            builder.AppendLine();
            builder.AppendLine("> [!WARNING]");
            builder.AppendLine("> The baseline and head ran in different environments, so time comparisons may not be meaningful.");
            builder.AppendLine(">");
            builder.AppendLine($"> - Baseline: {Describe(baselineHost)}");
            builder.AppendLine($"> - Head: {Describe(headHost)}");
        }
        else if(headHost is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"Environment: {Describe(headHost)}.");
        }

        return builder.ToString();
    }

    private static void WriteAnnotations(IEnumerable<Row> rows)
    {
        foreach(var row in rows)
        {
            var (level, title, detail) = row.Status switch
            {
                Status.AllocationRegression => ("error", "Allocation regression",
                    $"allocated {FormatBytes(row.Baseline?.Allocated)} → {FormatBytes(row.Head?.Allocated)} ({FormatChange(row.AllocationChange)})"),
                Status.MissingHead => ("error", row.Head is null ? "Missing head result" : "Failed benchmark",
                    row.Head is null ? "the head run produced no result" : "the head run reported no statistics"),
                Status.MissingAllocation => ("error", "Missing allocation data", row.Head?.Allocated is null
                    ? "the head run reported no allocation data; MemoryDiagnoser must stay enabled"
                    : "the baseline has no allocation data; commit an updated baseline"),
                Status.Slower => ("warning", "Slower benchmark",
                    $"mean time {FormatTime(row.Baseline?.Mean)} → {FormatTime(row.Head?.Mean)} ({FormatChange(row.TimeChange)})"),
                Status.Accepted => ("notice", "Accepted benchmark change", row.Head is null
                    ? "the updated baseline removes this benchmark"
                    : $"allocated {FormatBytes(row.Baseline?.Allocated)} → {FormatBytes(row.Head.Allocated)} ({FormatChange(row.AllocationChange)}) matches the updated baseline"),
                _ => ((string?)null, "", ""),
            };
            if(level is not null)
            {
                // Workflow command messages must escape %, CR and LF.
                var message = $"{row.Method} [{DisplayParameters(row.Parameters)}]: {detail}"
                    .Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
                Console.WriteLine($"::{level} title={title}::{message}");
            }
        }
    }

    private static string Label(Status status) => status switch
    {
        Status.AllocationRegression => "🔴 Allocation regression",
        Status.MissingHead => "🔴 Missing or failed head result",
        Status.MissingAllocation => "🔴 Missing allocation data",
        Status.Slower => "🟠 Slower",
        Status.Accepted => "🟡 Accepted by updated baseline",
        Status.Improved => "🟢 Improved",
        Status.Unchanged => "⚪ No significant change",
        Status.NoBaseline => "➖ No baseline",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string DisplayParameters(string parameters) => parameters.Replace("&", ", ");

    private static string Describe(Host host) => $"{host.Processor}, {host.Os}, {host.Runtime}, BenchmarkDotNet {host.BenchmarkDotNet}";

    private static string FormatTime(double? nanoseconds) => nanoseconds switch
    {
        null or double.NaN => "—",
        >= 1e9 => Invariant($"{nanoseconds.Value / 1e9:F2} s"),
        >= 1e6 => Invariant($"{nanoseconds.Value / 1e6:F2} ms"),
        >= 1e3 => Invariant($"{nanoseconds.Value / 1e3:F2} μs"),
        _ => Invariant($"{nanoseconds.Value:F2} ns"),
    };

    private static string FormatBytes(long? bytes) => bytes switch
    {
        null => "—",
        >= 1L << 20 => Invariant($"{bytes.Value / (double)(1L << 20):F2} MB"),
        >= 1L << 10 => Invariant($"{bytes.Value / (double)(1L << 10):F2} KB"),
        _ => Invariant($"{bytes.Value} B"),
    };

    private static string FormatChange(double change) => change switch
    {
        double.NaN => "—",
        double.PositiveInfinity => "+∞%",
        _ => Invariant($"{change:+0.00;-0.00;0.00}%"),
    };

    private static double Number(JsonElement element, string name)
        => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number
            ? value.GetDouble()
            : double.NaN;

    private static void WriteNumber(Utf8JsonWriter writer, string name, double value)
    {
        if(double.IsFinite(value))
        {
            writer.WriteNumber(name, Math.Round(value, 2));
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static long? Integer(JsonElement element, string name)
        => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.Number && value.TryGetInt64(out var result)
                ? result
                : null;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind is JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private enum Status
    {
        AllocationRegression,
        MissingHead,
        MissingAllocation,
        Slower,
        Accepted,
        Improved,
        Unchanged,
        NoBaseline,
    }

    private sealed record Measurement(double Mean, double Lower, double Upper, long? Allocated)
    {
        /// <summary>A benchmark that was reported without statistics because its run failed.</summary>
        public static Measurement FailedRun { get; } = new(double.NaN, double.NaN, double.NaN, null);

        public bool Failed => double.IsNaN(Mean);
    }

    private sealed record Host(string Processor, string Os, string Runtime, string BenchmarkDotNet);

    private sealed record Report(Dictionary<(string Parameters, string Method), Measurement> Measurements, Host? Host, string? Source);

    private sealed record Row(string Parameters, string Method, Measurement? Baseline, Measurement? Head, double TimeChange, double AllocationChange, Status Status);
}
