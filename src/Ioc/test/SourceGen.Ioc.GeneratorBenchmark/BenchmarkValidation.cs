using Microsoft.CodeAnalysis;

namespace SourceGen.Ioc.GeneratorBenchmark;

/// <summary>
/// Validates the generator output in benchmark setups.
/// </summary>
internal static class BenchmarkValidation
{
    /// <summary>
    /// The environment variable that skips compiling the output compilation in benchmark setups when set to <c>true</c>.
    /// </summary>
    public const string SkipCompileVariable = "SOURCEGEN_BENCHMARK_SKIP_COMPILE_VALIDATION";

    // Binding a large output compilation takes seconds to minutes in every benchmark process. CI skips it because
    // GeneratorPerformanceTests compiles the output of every scenario at a smaller size; generator failures and errors are still checked.
    private static readonly bool SkipCompile = Environment.GetEnvironmentVariable(SkipCompileVariable) == "true";

    public static void Validate(GeneratorDriver driver, Compilation output)
        => GeneratorBenchmarkInput.Validate(driver, SkipCompile ? null : output);
}
