using System.Collections.Frozen;
using BenchmarkDotNet.Configs;

namespace SourceGen.Ioc.Benchmark.Benchmarks;

/// <summary>
/// Benchmark comparing switch expression vs FrozenDictionary for Type-based lookups.
/// <para>
/// Tests the performance difference between:
/// <list type="bullet">
///   <item>Switch expression with Type pattern matching</item>
///   <item>FrozenDictionary&lt;Type, Func&lt;object&gt;&gt; lookup</item>
/// </list>
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class Switch_Vs_FrozenDictionaryBenchmark
{
    // FrozenDictionary instances
    private FrozenDictionary<Type, Func<object>> _frozenDict10 = null!;
    private FrozenDictionary<Type, Func<object>> _frozenDict25 = null!;
    private FrozenDictionary<Type, Func<object>> _frozenDict50 = null!;
    private FrozenDictionary<Type, Func<object>> _frozenDict75 = null!;
    private FrozenDictionary<Type, Func<object>> _frozenDict100 = null!;

    // Random lookup indices for realistic access patterns
    private int[] _randomIndices10 = null!;
    private int[] _randomIndices25 = null!;
    private int[] _randomIndices50 = null!;
    private int[] _randomIndices75 = null!;
    private int[] _randomIndices100 = null!;

    private const int LookupIterations = 100;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Build FrozenDictionaries using generated test types
        _frozenDict10 = BuildFrozenDictionary(GeneratedTestData.TestTypes10);
        _frozenDict25 = BuildFrozenDictionary(GeneratedTestData.TestTypes25);
        _frozenDict50 = BuildFrozenDictionary(GeneratedTestData.TestTypes50);
        _frozenDict75 = BuildFrozenDictionary(GeneratedTestData.TestTypes75);
        _frozenDict100 = BuildFrozenDictionary(GeneratedTestData.TestTypes100);

        // Generate random indices for lookup tests
        var random = new Random(42); // Fixed seed for reproducibility
        _randomIndices10 = [.. Enumerable.Range(0, LookupIterations).Select(_ => random.Next(10))];
        _randomIndices25 = [.. Enumerable.Range(0, LookupIterations).Select(_ => random.Next(25))];
        _randomIndices50 = [.. Enumerable.Range(0, LookupIterations).Select(_ => random.Next(50))];
        _randomIndices75 = [.. Enumerable.Range(0, LookupIterations).Select(_ => random.Next(75))];
        _randomIndices100 = [.. Enumerable.Range(0, LookupIterations).Select(_ => random.Next(100))];
    }

    private static FrozenDictionary<Type, Func<object>> BuildFrozenDictionary(Type[] types)
    {
        var dict = new Dictionary<Type, Func<object>>();
        Func<object> factory = static () => new object();

        foreach (var type in types)
        {
            dict[type] = factory;
        }

        return dict.ToFrozenDictionary();
    }

    #region 010 Items

    [BenchmarkCategory("010 Items"), Benchmark(Baseline = true)]
    public object? FrozenDictionary_10Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes10[_randomIndices10[i]];

            if (_frozenDict10.TryGetValue(type, out var func))
            {
                result = func;
            }
        }

        return result;
    }

    [BenchmarkCategory("010 Items"), Benchmark]
    public object? SwitchLookup_10Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes10[_randomIndices10[i]];
            result = GeneratedSwitchLookup.Lookup10(type);
        }

        return result;
    }

    #endregion

    #region 025 Items

    [BenchmarkCategory("025 Items"), Benchmark(Baseline = true)]
    public object? FrozenDictionary_25Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes25[_randomIndices25[i]];

            if (_frozenDict25.TryGetValue(type, out var func))
            {
                result = func;
            }
        }

        return result;
    }

    [BenchmarkCategory("025 Items"), Benchmark]
    public object? SwitchLookup_25Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes25[_randomIndices25[i]];
            result = GeneratedSwitchLookup.Lookup25(type);
        }

        return result;
    }

    #endregion

    #region 050 Items

    [BenchmarkCategory("050 Items"), Benchmark(Baseline = true)]
    public object? FrozenDictionary_50Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes50[_randomIndices50[i]];

            if (_frozenDict50.TryGetValue(type, out var func))
            {
                result = func;
            }
        }

        return result;
    }

    [BenchmarkCategory("050 Items"), Benchmark]
    public object? SwitchLookup_50Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes50[_randomIndices50[i]];
            result = GeneratedSwitchLookup.Lookup50(type);
        }

        return result;
    }

    #endregion

    #region 075 Items

    [BenchmarkCategory("075 Items"), Benchmark(Baseline = true)]
    public object? FrozenDictionary_75Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes75[_randomIndices75[i]];

            if (_frozenDict75.TryGetValue(type, out var func))
            {
                result = func;
            }
        }

        return result;
    }

    [BenchmarkCategory("075 Items"), Benchmark]
    public object? SwitchLookup_75Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes75[_randomIndices75[i]];
            result = GeneratedSwitchLookup.Lookup75(type);
        }

        return result;
    }

    #endregion

    #region 100 Items

    [BenchmarkCategory("100 Items"), Benchmark(Baseline = true)]
    public object? FrozenDictionary_100Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes100[_randomIndices100[i]];

            if (_frozenDict100.TryGetValue(type, out var func))
            {
                result = func;
            }
        }

        return result;
    }

    [BenchmarkCategory("100 Items"), Benchmark]
    public object? SwitchLookup_100Items()
    {
        object? result = null;

        for (int i = 0; i < LookupIterations; i++)
        {
            var type = GeneratedTestData.TestTypes100[_randomIndices100[i]];
            result = GeneratedSwitchLookup.Lookup100(type);
        }

        return result;
    }

    #endregion
}
