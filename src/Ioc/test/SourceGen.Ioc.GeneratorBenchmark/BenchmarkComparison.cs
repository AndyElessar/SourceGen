using System.Text;
using System.Text.Json;
using static System.FormattableString;

namespace SourceGen.Ioc.GeneratorBenchmark;

/// <summary>
/// Compares BenchmarkDotNet JSON reports (a file or a directory containing them) produced by the same harness
/// for a base and a head build.
/// </summary>
internal static class BenchmarkComparison
{
    /// <summary>
    /// Writes a Markdown comparison to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="allocationThresholds">Allocation thresholds by benchmark class name, overriding <paramref name="thresholdPercent"/> for noisier benchmarks.</param>
    /// <param name="acceptRegressions">Reports allocation regressions and removed benchmarks as accepted instead of failing.</param>
    /// <param name="driftSuffix">The method name suffix of the drift control benchmarks, which do the same work without the code under test.</param>
    /// <returns>1 when allocations regress beyond the benchmark's threshold, a base benchmark is missing from the head, or a head result failed or lacks allocation data, unless <paramref name="acceptRegressions"/> accepts it; otherwise 0.</returns>
    public static int Run(string baselinePath, string headPath, string outputPath, double thresholdPercent,
        IReadOnlyDictionary<string, double> allocationThresholds, bool acceptRegressions = false, string driftSuffix = "Overhead")
    {
        var baseline = Load(baselinePath);
        var head = Load(headPath);
        if(head.Measurements.Count == 0)
        {
            throw new InvalidOperationException($"No benchmark results were found in '{headPath}'.");
        }

        var drift = Drift(baseline, head, driftSuffix);
        Row[] rows = [.. baseline.Measurements.Keys.Union(head.Measurements.Keys)
            .OrderBy(static key => key.Type, StringComparer.Ordinal)
            .ThenBy(static key => key.Parameters, StringComparer.Ordinal)
            .ThenBy(static key => key.Method, StringComparer.Ordinal)
            .Select(key => Classify(key, baseline.Measurements.GetValueOrDefault(key), head.Measurements.GetValueOrDefault(key), acceptRegressions, thresholdPercent, allocationThresholds, drift, driftSuffix))];

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, Format(rows, thresholdPercent, allocationThresholds, drift, driftSuffix, baseline.Hosts, head.Hosts));
        if(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            WriteAnnotations(rows);
        }

        return rows.Any(static row => row.Status is Status.AllocationRegression or Status.MissingHead or Status.MissingAllocation) ? 1 : 0;
    }

    private static Report Load(string path)
    {
        Dictionary<(string Type, string Parameters, string Method), Measurement> measurements = [];
        Dictionary<string, Host> hosts = new(StringComparer.Ordinal);
        // Subdirectories are included because CI splits a run into one BenchmarkDotNet run per class and scenario group.
        string[] files = File.Exists(path) ? [path]
            : Directory.Exists(path) ? [.. Directory.EnumerateFiles(path, "*-report*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];

        foreach(var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = document.RootElement;
            if(!root.TryGetProperty("Benchmarks", out var benchmarks))
            {
                continue;
            }

            Host? host = root.TryGetProperty("HostEnvironmentInfo", out var info)
                ? new(Text(info, "ProcessorName") ?? "unknown", Text(info, "OsVersion") ?? "unknown",
                    Text(info, "RuntimeVersion") ?? "unknown", Text(info, "BenchmarkDotNetVersion") ?? "unknown")
                : null;

            foreach(var benchmark in benchmarks.EnumerateArray())
            {
                // Classes may share method names, such as the MS.DI baselines of the runtime benchmarks
                var type = Text(benchmark, "Type") ?? "";
                var key = (type, Text(benchmark, "Parameters") ?? "", Text(benchmark, "Method") ?? "unknown");
                if(host is not null)
                {
                    hosts.TryAdd(type, host);
                }

                // Failed benchmarks have no statistics; keeping them as failed measurements fails the gate instead of dropping them.
                if(!benchmark.TryGetProperty("Statistics", out var statistics) || Number(statistics, "Mean") is double.NaN)
                {
                    measurements.TryAdd(key, Measurement.FailedRun with { Type = type });
                    continue;
                }

                var interval = statistics.TryGetProperty("ConfidenceInterval", out var value) ? value : default;
                var memory = benchmark.TryGetProperty("Memory", out value) ? value : default;
                measurements.TryAdd(
                    key,
                    new(Number(statistics, "Mean"), Number(interval, "Lower"), Number(interval, "Upper"), Integer(memory, "BytesAllocatedPerOperation"), type));
            }
        }

        return new(measurements, hosts);
    }

    /// <summary>
    /// Estimates how much the runner slowed down or sped up between the base and head runs
    /// from the head/base mean time ratios of the drift control benchmarks, whose names end with <paramref name="driftSuffix"/>.
    /// </summary>
    /// <remarks>
    /// Drift control benchmarks do the same work without the code under test, such as the generator benchmarks'
    /// <c>*Overhead</c> benchmarks (Roslyn without the IoC generator or analyzers) and the runtime benchmarks' <c>*_MSDI</c>
    /// benchmarks (resolution with Microsoft.Extensions.DependencyInjection). A change in their time comes from the
    /// environment, which shifts every benchmark that ran after the base run.
    /// </remarks>
    private static RunnerDrift Drift(Report baseline, Report head, string driftSuffix)
    {
        (string Type, double Ratio)[] ratios = [.. head.Measurements
            .Where(pair => IsDriftControl(pair.Key.Method, driftSuffix) && !pair.Value.Failed)
            .Select(pair => (pair.Value.Type, Head: pair.Value.Mean, Base: baseline.Measurements.GetValueOrDefault(pair.Key)?.Mean ?? double.NaN))
            .Where(static ratio => ratio.Base > 0)
            .Select(static ratio => (ratio.Type, ratio.Head / ratio.Base))];

        return new(
            ratios.GroupBy(static ratio => ratio.Type, static ratio => ratio.Ratio, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => Median(group), StringComparer.Ordinal),
            ratios.Length > 0 ? Median(ratios.Select(static ratio => ratio.Ratio)) : 1);
    }

    private static bool IsDriftControl(string method, string driftSuffix) => method.EndsWith(driftSuffix, StringComparison.Ordinal);

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = [.. values.Order()];
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    private static Row Classify((string Type, string Parameters, string Method) key, Measurement? baseline, Measurement? head, bool acceptRegressions,
        double threshold, IReadOnlyDictionary<string, double> allocationThresholds, RunnerDrift drift, string driftSuffix)
    {
        if(head is null || head.Failed)
        {
            // Removing a benchmark can be accepted; a failed run never is.
            var removal = head is null && acceptRegressions ? Status.Accepted : Status.MissingHead;
            return new(key, baseline, head, double.NaN, double.NaN, removal);
        }

        // Without allocation data the gate cannot detect regressions, so a disabled MemoryDiagnoser fails the check.
        if(head.Allocated is not { } headBytes)
        {
            return new(key, baseline, head, double.NaN, double.NaN, Status.MissingAllocation);
        }

        if(baseline is null || baseline.Failed)
        {
            return new(key, baseline, head, double.NaN, double.NaN, Status.NoBaseline);
        }

        // Head times are divided by the class's drift, so the comparison shows the change the code made rather than the runner.
        var factor = drift.For(head.Type);
        var time = Change(baseline.Mean, head.Mean / factor);
        if(baseline.Allocated is not { } baselineBytes)
        {
            var status = acceptRegressions ? Status.Accepted : Status.MissingAllocation;
            return new(key, baseline, head, time, double.NaN, status);
        }

        var allocation = Change(baselineBytes, headBytes);
        var allocationThreshold = allocationThresholds.GetValueOrDefault(head.Type, threshold);

        // Comparisons with NaN are false, so a missing confidence interval never flags a time change.
        var slower = time > threshold && head.Lower / factor > baseline.Upper;
        var faster = time < -threshold && head.Upper / factor < baseline.Lower;
        // Drift control benchmarks run no code under test, so a change in their allocations is not caused by the change.
        var gated = !IsDriftControl(key.Method, driftSuffix);
        var result = (gated && allocation > allocationThreshold, slower, faster || (gated && allocation < -allocationThreshold)) switch
        {
            (true, _, _) when acceptRegressions => Status.Accepted,
            (true, _, _) => Status.AllocationRegression,
            (_, true, _) => Status.Slower,
            (_, _, true) => Status.Improved,
            _ => Status.Unchanged,
        };
        return new(key, baseline, head, time, allocation, result);
    }

    private static double Change(double baseline, double head) => baseline switch
    {
        > 0 => (head - baseline) / baseline * 100,
        _ when head > 0 => double.PositiveInfinity,
        _ => 0,
    };

    private static string Format(IReadOnlyList<Row> rows, double threshold, IReadOnlyDictionary<string, double> allocationThresholds,
        RunnerDrift drift, string driftSuffix, IReadOnlyDictionary<string, Host> baselineHosts, IReadOnlyDictionary<string, Host> headHosts)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(" · ", rows
            .GroupBy(static row => row.Status)
            .OrderBy(static group => group.Key)
            .Select(static group => Invariant($"{Label(group.Key)}: {group.Count()}"))));
        builder.AppendLine();
        builder.AppendLine("| Benchmark | Parameters | Mean time (baseline → head) | Δ time | Allocated (baseline → head) | Δ allocated | Result |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | --- |");
        var names = DisplayNames(rows);
        foreach(var row in rows)
        {
            builder.AppendLine(Invariant($"| {names[row.Key]} | {DisplayParameters(row.Key.Parameters).Replace("|", "\\|")} | {FormatTime(row.Baseline?.Mean)} → {FormatTime(row.Head?.Mean)} | {FormatChange(row.TimeChange)} | {FormatBytes(row.Baseline?.Allocated)} → {FormatBytes(row.Head?.Allocated)} | {FormatChange(row.AllocationChange)} | {Label(row.Status)} |"));
        }

        builder.AppendLine();
        var overrides = string.Concat(allocationThresholds
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => Invariant($" ({pair.Value}% for `{pair.Key}`)")));
        builder.Append(Invariant($"Allocated is managed memory per operation. An allocation increase above {threshold}%{overrides} fails the check unless it is accepted; `*{driftSuffix}` benchmarks, which do the same work without the code under test, are not checked. A time change above {threshold}% is reported only when the 99.9% confidence intervals do not overlap, and never fails the check because shared runners are noisy."));
        if(drift.ByClass.Count > 0)
        {
            var drifts = string.Join(", ", drift.ByClass
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => Invariant($"{FormatChange((pair.Value - 1) * 100)} for `{pair.Key}`")));
            builder.Append(Invariant($" Δ time and the confidence intervals are adjusted for runner drift: head times are divided by the median time change of the class's `*{driftSuffix}` benchmarks ({drifts}), or of all of them for other classes ({FormatChange((drift.Overall - 1) * 100)})."));
        }

        if(rows.Any(static row => row.Status is Status.Accepted))
        {
            builder.Append(" Accepted results are allocation regressions or removed benchmarks that the change explicitly accepts.");
        }

        builder.AppendLine();
        // Classes can run on different machines, so only the baseline and head of the same class must match.
        var mismatched = headHosts
            .Where(pair => baselineHosts.TryGetValue(pair.Key, out var baselineHost) && baselineHost != pair.Value)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        if(mismatched.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("> [!WARNING]");
            builder.AppendLine("> The baseline and head of these classes ran in different environments, so their time comparisons may not be meaningful.");
            foreach(var (type, headHost) in mismatched)
            {
                builder.AppendLine(">");
                builder.AppendLine($"> - `{type}` baseline: {Describe(baselineHosts[type])}");
                builder.AppendLine($"> - `{type}` head: {Describe(headHost)}");
            }
        }
        else if(headHosts.Values.Distinct().ToArray() is [var host])
        {
            builder.AppendLine();
            builder.AppendLine($"Environment: {Describe(host)}.");
        }
        else if(headHosts.Count > 0)
        {
            builder.AppendLine();
            foreach(var (type, headHost) in headHosts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"- Environment of `{type}`: {Describe(headHost)}.");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Names each benchmark by its method, qualified with its class when another class has a method of the same name.
    /// </summary>
    private static Dictionary<(string Type, string Parameters, string Method), string> DisplayNames(IReadOnlyList<Row> rows)
    {
        var ambiguous = rows
            .GroupBy(static row => row.Key.Method, StringComparer.Ordinal)
            .Where(static group => group.Select(static row => row.Key.Type).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        return rows.ToDictionary(
            static row => row.Key,
            row => ambiguous.Contains(row.Key.Method) ? $"{row.Key.Type}.{row.Key.Method}" : row.Key.Method);
    }

    private static void WriteAnnotations(IReadOnlyList<Row> rows)
    {
        var names = DisplayNames(rows);
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
                    : "the base run has no allocation data"),
                Status.Slower => ("warning", "Slower benchmark",
                    $"mean time {FormatTime(row.Baseline?.Mean)} → {FormatTime(row.Head?.Mean)} ({FormatChange(row.TimeChange)})"),
                Status.Accepted => ("notice", "Accepted benchmark change", row.Head is null
                    ? "the benchmark was removed"
                    : $"allocated {FormatBytes(row.Baseline?.Allocated)} → {FormatBytes(row.Head.Allocated)} ({FormatChange(row.AllocationChange)})"),
                _ => ((string?)null, "", ""),
            };
            if(level is not null)
            {
                // Workflow command messages must escape %, CR and LF.
                var message = $"{names[row.Key]} [{DisplayParameters(row.Key.Parameters)}]: {detail}"
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
        Status.Accepted => "🟡 Accepted",
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

    private sealed record Measurement(double Mean, double Lower, double Upper, long? Allocated, string Type = "")
    {
        /// <summary>A benchmark that was reported without statistics because its run failed.</summary>
        public static Measurement FailedRun { get; } = new(double.NaN, double.NaN, double.NaN, null);

        public bool Failed => double.IsNaN(Mean);
    }

    private sealed record Host(string Processor, string Os, string Runtime, string BenchmarkDotNet);

    /// <param name="ByClass">The head/base time ratio of each benchmark class that has overhead benchmarks in both runs.</param>
    /// <param name="Overall">The ratio over all overhead benchmarks, used for classes without their own, or 1 when there are none.</param>
    private sealed record RunnerDrift(Dictionary<string, double> ByClass, double Overall)
    {
        public double For(string type) => ByClass.GetValueOrDefault(type, Overall);
    }

    /// <param name="Hosts">The environment of each benchmark class, from the first report that contains it.</param>
    private sealed record Report(Dictionary<(string Type, string Parameters, string Method), Measurement> Measurements, Dictionary<string, Host> Hosts);

    private sealed record Row((string Type, string Parameters, string Method) Key, Measurement? Baseline, Measurement? Head, double TimeChange, double AllocationChange, Status Status);
}
