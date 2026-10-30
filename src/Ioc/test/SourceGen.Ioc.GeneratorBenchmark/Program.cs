using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Running;
using SourceGen.Ioc.GeneratorBenchmark;

if(args is ["--compare", var baselinePath, var headPath, var outputPath, var threshold, .. var options])
{
    var acceptRegressions = false;
    var allocationThresholds = new Dictionary<string, double>(StringComparer.Ordinal);
    for(var i = 0; i < options.Length; i++)
    {
        switch(options[i])
        {
            case "--accept-regressions":
                acceptRegressions = true;
                break;
            // --allocation-threshold <benchmark class>=<percent>, e.g. AnalyzerBenchmarks=10
            case "--allocation-threshold" when i + 1 < options.Length && options[i + 1].Split('=') is [var type, var percent]:
                allocationThresholds[type] = double.Parse(percent, CultureInfo.InvariantCulture);
                i++;
                break;
            default:
                throw new ArgumentException($"Unknown or incomplete --compare option '{options[i]}'.");
        }
    }

    return BenchmarkComparison.Run(baselinePath, headPath, outputPath, double.Parse(threshold, CultureInfo.InvariantCulture),
        allocationThresholds, acceptRegressions);
}

if(args is ["--verify", var path])
{
    var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach(var scenario in Enum.GetValues<GeneratorScenario>())
    {
        foreach(var count in new[] { 100, 500 })
        {
            var input = new GeneratorBenchmarkInput(scenario, count);
            var states = Enum.GetValues<GeneratorEdit>().Select(edit => ($"edited-{edit}", input.CreateEdited(edit))).Prepend(("initial", input.Initial));
            foreach(var (name, compilation) in states)
            {
                var driver = input.CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
                GeneratorBenchmarkInput.Validate(driver, output);
                var sources = driver.GetRunResult().Results.Single().GeneratedSources;
                foreach(var source in sources)
                {
                    fingerprints.Add($"{scenario}/{count}/{name}/{source.HintName}", Hash(source.SourceText.ToString()));
                }
                // The generated size shows output growth that hashes alone do not quantify.
                fingerprints.Add($"{scenario}/{count}/{name}/size", sources.Sum(static source => source.SourceText.Length).ToString(CultureInfo.InvariantCulture));
            }

            var analyzerInput = new AnalyzerBenchmarkInput(scenario, count);
            analyzerInput.Validate();
            foreach(var (name, analyzers) in new[] { ("RegisterAnalyzer", AnalyzerBenchmarkInput.Register), ("ContainerAnalyzer", AnalyzerBenchmarkInput.Container) })
            {
                var diagnostics = analyzerInput.Run(analyzers).Select(static d => d.ToString()).Order(StringComparer.Ordinal);
                fingerprints.Add($"{scenario}/{count}/analyzer/{name}", Hash(string.Join("\n", diagnostics)));
            }
        }
    }
    File.WriteAllText(path, JsonSerializer.Serialize(fingerprints, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Verified {fingerprints.Count} generated source and analyzer diagnostic fingerprints.");
    return 0;

    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

BenchmarkSwitcher.FromTypes([typeof(ColdGeneratorBenchmarks), typeof(GeneratorBenchmarks), typeof(AnalyzerBenchmarks), typeof(GeneratedCodeBenchmarks)]).Run(args);
return 0;
