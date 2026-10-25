using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Running;
using SourceGen.Ioc.GeneratorBenchmark;

if(args is ["--compare", var baselinePath, var headPath, var outputPath, var threshold, .. var accepted] && accepted.Length <= 1)
{
    return BenchmarkComparison.Run(baselinePath, headPath, outputPath, double.Parse(threshold, CultureInfo.InvariantCulture),
        accepted is [var acceptedPath] ? acceptedPath : null);
}

if(args is ["--export-baseline", var resultsPath, var baselineOutputPath, var resultsSource])
{
    return BenchmarkComparison.Export(resultsPath, baselineOutputPath, resultsSource);
}

if(args is ["--verify", var path])
{
    var fingerprints = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach(var scenario in Enum.GetValues<GeneratorScenario>())
    {
        foreach(var count in new[] { 100, 500 })
        {
            var input = new GeneratorBenchmarkInput(scenario, count);
            foreach(var (name, compilation) in new[] { ("initial", input.Initial), ("edited", input.Edited) })
            {
                var driver = input.CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
                GeneratorBenchmarkInput.Validate(driver, output);
                foreach(var source in driver.GetRunResult().Results.Single().GeneratedSources)
                {
                    fingerprints.Add($"{scenario}/{count}/{name}/{source.HintName}", Hash(source.SourceText.ToString()));
                }
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

BenchmarkSwitcher.FromTypes([typeof(GeneratorBenchmarks), typeof(AnalyzerBenchmarks)]).Run(args);
return 0;
