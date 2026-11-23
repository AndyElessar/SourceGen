using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// Tests of incremental behavior: run the generator, edit the compilation, run again, and check the reasons of the
/// tracked steps. The first run needs <see cref="GeneratorRunOptions.TrackSteps"/>.
/// </summary>
public static class IncrementalTestHelper
{
    /// <summary>The types that MUST NOT flow through a pipeline step.</summary>
    private static readonly Type[] ForbiddenTypes =
    [
        typeof(ISymbol), typeof(SyntaxNode), typeof(SyntaxTree), typeof(Location),
        typeof(Compilation), typeof(SemanticModel), typeof(Diagnostic),
    ];

    /// <summary>Collections that compare by reference. In a model they break the cache.</summary>
    private static readonly Type[] ReferenceEqualCollections =
    [
        typeof(List<>), typeof(ImmutableArray<>), typeof(Dictionary<,>), typeof(HashSet<>),
        typeof(ImmutableList<>), typeof(ImmutableDictionary<,>), typeof(ImmutableHashSet<>),
    ];

    /// <summary>Replaces the text of the file at <paramref name="fileIndex"/> and runs the driver of <paramref name="previous"/> again.</summary>
    public static GeneratorTestResult RunAgainWithEdit(GeneratorTestResult previous, int fileIndex, string newSource)
    {
        Compilation input = previous.InputCompilation;
        SyntaxTree old = input.SyntaxTrees.Single(t => t.FilePath == SourceGeneratorTestHelper.FilePath(fileIndex));
        input = input.ReplaceSyntaxTree(old, CSharpSyntaxTree.ParseText(newSource, SourceGeneratorTestHelper.ParseOptions, old.FilePath));
        return RunAgain(previous, input);
    }

    /// <summary>Runs the driver of <paramref name="previous"/> again on <paramref name="input"/>.</summary>
    public static GeneratorTestResult RunAgain(GeneratorTestResult previous, Compilation input)
    {
        GeneratorDriver driver = previous.Driver.RunGeneratorsAndUpdateCompilation(input, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        SourceGeneratorTestHelper.ThrowIfGeneratorFailed(driver);
        return new GeneratorTestResult(driver, input, output, diagnostics);
    }

    /// <summary>The reasons of all outputs of the step with <paramref name="trackingName"/>.</summary>
    public static IReadOnlyList<IncrementalStepRunReason> GetReasons(GeneratorTestResult result, string trackingName)
        => [.. GetSteps(result, trackingName).SelectMany(static s => s.Outputs).Select(static o => o.Reason)];

    /// <summary>
    /// Verifies that each step has at least one output and that each output is <see cref="IncrementalStepRunReason.Cached"/>
    /// or <see cref="IncrementalStepRunReason.Unchanged"/>. A step without outputs fails, because it verifies nothing.
    /// </summary>
    public static async Task VerifyNotRunAgainAsync(GeneratorTestResult result, params IReadOnlyList<string> trackingNames)
    {
        foreach(string name in trackingNames)
        {
            IReadOnlyList<IncrementalStepRunReason> reasons = GetReasons(result, name);
            await Assert.That(reasons).IsNotEmpty().Because($"Step '{name}' has no outputs, so the test verifies nothing.");
            await Assert.That(reasons.All(static r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)).IsTrue()
                .Because($"Step '{name}' ran again: {string.Join(", ", reasons)}");
        }
    }

    /// <summary>
    /// Finds the values in the outputs of the steps named <paramref name="trackingNames"/> that break the cache: a symbol,
    /// a syntax node, a location, a compilation, and so on, or a collection that compares by reference inside a model.
    /// Only the named steps are checked: Roslyn tracks its own steps too (<c>Compilation</c>, <c>SourceOutput</c>), and
    /// they hold such objects by design.
    /// </summary>
    public static IReadOnlyList<string> FindForbiddenValues(GeneratorTestResult result, params IReadOnlyList<string> trackingNames)
    {
        List<string> problems = [];
        foreach(string name in trackingNames)
        {
            foreach(IncrementalGeneratorRunStep step in GetSteps(result, name))
            {
                foreach((object value, IncrementalStepRunReason _) in step.Outputs)
                {
                    Walk(value, name, new HashSet<object>(ReferenceEqualityComparer.Instance), problems, depth: 0, inModel: false);
                }
            }
        }

        return problems;
    }

    private static ImmutableArray<IncrementalGeneratorRunStep> GetSteps(GeneratorTestResult result, string trackingName)
        => result.RunResult.TrackedSteps.TryGetValue(trackingName, out ImmutableArray<IncrementalGeneratorRunStep> steps)
            ? steps
            : throw new KeyNotFoundException($"No step is tracked with the name '{trackingName}'. Tracked: {string.Join(", ", result.RunResult.TrackedSteps.Keys)}");

    /// <param name="inModel">
    /// The value is a field of a model. The output of <c>Collect()</c> (an <see cref="ImmutableArray{T}"/>) and of
    /// <c>Combine()</c> (a tuple) are compared by Roslyn itself, so a collection there is not a problem.
    /// </param>
    private static void Walk(object? value, string path, HashSet<object> visited, List<string> problems, int depth, bool inModel)
    {
        if(value is null || depth > 32)
        {
            return;
        }

        Type type = value.GetType();
        if(type.IsPrimitive || type.IsEnum || value is string or decimal)
        {
            return;
        }

        if(ForbiddenTypes.FirstOrDefault(t => t.IsInstanceOfType(value)) is { } forbidden)
        {
            problems.Add($"{path}: {forbidden.Name} ({type.Name})");
            return;
        }

        if(inModel && (type.IsArray || type.IsGenericType && ReferenceEqualCollections.Contains(type.GetGenericTypeDefinition())))
        {
            problems.Add($"{path}: collection without value equality ({type.Name})");
            return;
        }

        if(!type.IsValueType && !visited.Add(value))
        {
            return;
        }

        if(value is IEnumerable items)
        {
            IEnumerator enumerator;
            try
            {
                enumerator = items.GetEnumerator();
            }
            catch(InvalidOperationException)
            {
                return;   // A default ImmutableArray cannot be enumerated
            }

            int index = 0;
            while(enumerator.MoveNext())
            {
                Walk(enumerator.Current, $"{path}[{index++}]", visited, problems, depth + 1, inModel);
            }

            return;
        }

        bool fieldsInModel = inModel || value is not ITuple;
        for(Type? t = type; t is not null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
        {
            foreach(FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                Walk(field.GetValue(value), $"{path}.{field.Name}", visited, problems, depth + 1, fieldsInModel);
            }
        }
    }
}
