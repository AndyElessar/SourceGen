using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc.Test.Helpers;

/// <summary>
/// Helper class for testing source generators.
/// </summary>
public static class SourceGeneratorTestHelper
{
    private const string AttributeSource = """
        using System;
        using System.Diagnostics;
        using Microsoft.Extensions.DependencyInjection;

        namespace SourceGen.Ioc;

        public enum KeyType
        {
            Value = 0,
            Csharp = 1
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class IoCRegisterAttribute : Attribute
        {
            public ServiceLifetime Lifetime { get; init; }
            public bool RegisterAllInterfaces { get; init; }
            public bool RegisterAllBaseClasses { get; init; }
            public Type[] ServiceTypes { get; init; } = [];
            public KeyType KeyType { get; init; } = KeyType.Value;
            public object? Key { get; init; }
        }

        [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
        public sealed class IoCRegisterForAttribute(Type targetType) : Attribute
        {
            public Type TargetType { get; } = targetType;
            public ServiceLifetime Lifetime { get; init; }
            public bool RegisterAllInterfaces { get; init; }
            public bool RegisterAllBaseClasses { get; init; }
            public Type[] ServiceTypes { get; init; } = [];
            public KeyType KeyType { get; init; } = KeyType.Value;
            public object? Key { get; init; }
        }

        [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
        public sealed class IoCRegisterDefaultSettingsAttribute(Type targetServiceType, ServiceLifetime lifetime) : Attribute
        {
            public Type TargetServiceType { get; } = targetServiceType;
            public ServiceLifetime Lifetime { get; } = lifetime;
            public bool RegisterAllInterfaces { get; init; }
            public bool RegisterAllBaseClasses { get; init; }
            public Type[] ServiceTypes { get; init; } = [];
        }
        """;

    /// <summary>
    /// Runs the source generator and returns the generated source texts.
    /// </summary>
    public static GeneratorRunResult RunGenerator<TGenerator>(string source, string assemblyName = "TestAssembly")
        where TGenerator : IIncrementalGenerator, new()
    {
        var userSyntaxTree = CSharpSyntaxTree.ParseText(source);
        var attributeSyntaxTree = CSharpSyntaxTree.ParseText(AttributeSource);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add reference to Microsoft.Extensions.DependencyInjection.Abstractions
        var diAbstractionsAssembly = typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly;
        references.Add(MetadataReference.CreateFromFile(diAbstractionsAssembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [userSyntaxTree, attributeSyntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new TGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        return runResult.Results.Single();
    }

    /// <summary>
    /// Gets all generated source texts from the generator run result.
    /// </summary>
    public static IEnumerable<(string HintName, string SourceText)> GetGeneratedSources(GeneratorRunResult result)
    {
        return result.GeneratedSources
            .Select(s => (s.HintName, s.SourceText.ToString()));
    }

    /// <summary>
    /// Gets a single generated source by hint name.
    /// </summary>
    public static string? GetGeneratedSource(GeneratorRunResult result, string hintNameContains)
    {
        return result.GeneratedSources
            .FirstOrDefault(s => s.HintName.Contains(hintNameContains))
            .SourceText?.ToString();
    }

    /// <summary>
    /// Runs the analyzer and returns the diagnostics.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<TAnalyzer>(string source, string assemblyName = "TestAssembly")
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var userSyntaxTree = CSharpSyntaxTree.ParseText(source);
        var attributeSyntaxTree = CSharpSyntaxTree.ParseText(AttributeSource);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add reference to Microsoft.Extensions.DependencyInjection.Abstractions
        var diAbstractionsAssembly = typeof(Microsoft.Extensions.DependencyInjection.ServiceLifetime).Assembly;
        references.Add(MetadataReference.CreateFromFile(diAbstractionsAssembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [userSyntaxTree, attributeSyntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new TAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics;
    }

    /// <summary>
    /// Gets diagnostics with a specific ID.
    /// </summary>
    public static IEnumerable<Diagnostic> GetDiagnosticsById(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        return diagnostics.Where(d => d.Id == diagnosticId);
    }
}
