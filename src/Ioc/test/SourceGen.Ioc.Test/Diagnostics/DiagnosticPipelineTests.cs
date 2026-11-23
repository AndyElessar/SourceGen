#pragma warning disable RS2008 // The probe descriptors are only for this test

using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PolyType.Roslyn;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;

namespace SourceGen.Ioc.Test.Diagnostics;

/// <summary>
/// Reports <see cref="DiagnosticInfo"/> models through <see cref="DiagnosticReporter"/>, as the real generator does: a
/// class named <c>ProbeWarn</c> or <c>ProbeError</c> gets the diagnostic on its name, and <c>ProbeNoLocation</c> gets a
/// warning without a location.
/// </summary>
internal sealed class DiagnosticProbeGenerator : IIncrementalGenerator
{
    public const string Step = "ProbeDiagnostics";

    public static readonly DiagnosticDescriptor Warning = Descriptors.Create("PROBE001", "Probe warning", "Warning on '{0}'", DiagnosticSeverity.Warning);
    public static readonly DiagnosticDescriptor Error = Descriptors.Create("PROBE002", "Probe error", "Error on '{0}'", DiagnosticSeverity.Error);

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = new() { [Warning.Id] = Warning, [Error.Id] = Error };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<DiagnosticInfo> diagnostics = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { Identifier.Text: "ProbeWarn" or "ProbeError" or "ProbeNoLocation" },
                static (ctx, _) => ((ClassDeclarationSyntax)ctx.Node).Identifier is var name && name.Text == "ProbeNoLocation"
                    ? DiagnosticInfo.Create(Warning.Id, (Location?)null, name.Text)
                    : DiagnosticInfo.Create(name.Text == "ProbeWarn" ? Warning.Id : Error.Id, name.GetLocation(), name.Text))
            .WithTrackingName(Step);

        context.RegisterSourceOutput(
            diagnostics.Collect().Combine(context.CompilationProvider),
            static (spc, input) => DiagnosticReporter.Report(spc, input.Right, input.Left, ById));
    }
}

public class DiagnosticPipelineTests
{
    [Test]
    public async Task Report_DiagnosticInSyntaxTree_ReportedAtSpanWithArguments()
    {
        (IReadOnlyList<string> sources, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse(
            "public class Other;",
            "public class {|PROBE001:ProbeWarn|};");

        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<DiagnosticProbeGenerator>(sources);

        await result.VerifyDiagnosticsAsync(expected, idPrefix: "PROBE");
        await Assert.That(result.GetDiagnostic("PROBE001").GetMessage(CultureInfo.InvariantCulture)).IsEqualTo("Warning on 'ProbeWarn'");
    }

    [Test]
    public async Task Report_Pragma_SuppressesWarningButNotError()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<DiagnosticProbeGenerator>("""
            #pragma warning disable PROBE001, PROBE002
            public class ProbeWarn;
            public class ProbeError;
            """);

        await Assert.That(result.ReportedDiagnostics.Select(static d => d.Id)).IsEquivalentTo(["PROBE002"]);
    }

    [Test]
    public async Task Report_FileSeverityOption_SuppressesWarningButNotError()
    {
        Dictionary<string, ReportDiagnostic> suppress = new() { ["PROBE001"] = ReportDiagnostic.Suppress, ["PROBE002"] = ReportDiagnostic.Suppress };

        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<DiagnosticProbeGenerator>(
            "public class ProbeWarn; public class ProbeError;",
            new() { FileDiagnosticOptions = new Dictionary<string, IReadOnlyDictionary<string, ReportDiagnostic>> { ["File0.cs"] = suppress } });

        await Assert.That(result.ReportedDiagnostics.Select(static d => d.Id)).IsEquivalentTo(["PROBE002"]);
    }

    [Test]
    public async Task Report_NoFilePath_ReportsWithoutLocation()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<DiagnosticProbeGenerator>("public class ProbeNoLocation;");

        await Assert.That(result.GetDiagnostic("PROBE001").Location).IsEqualTo(Location.None);
    }

    [Test]
    public async Task Report_TwoTreesWithSamePath_ReportsWithoutLocation()
    {
        Compilation compilation = SourceGeneratorTestHelper.CreateCompilation(["public class ProbeWarn;"]);
        SyntaxTree first = compilation.SyntaxTrees.First();
        compilation = compilation.AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class Other;", (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)first.Options, first.FilePath));

        Microsoft.CodeAnalysis.GeneratorDriver driver = Microsoft.CodeAnalysis.CSharp.CSharpGeneratorDriver
            .Create(new DiagnosticProbeGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        Diagnostic diagnostic = driver.GetRunResult().Diagnostics.Single(static d => d.Id == "PROBE001");

        await Assert.That(diagnostic.Location).IsEqualTo(Location.None);
    }

    [Test]
    public async Task Report_CodeMoved_ReportedAtNewSpan()
    {
        GeneratorTestResult first = SourceGeneratorTestHelper.RunGenerator<DiagnosticProbeGenerator>(["public class ProbeWarn;"], new() { TrackSteps = true });
        (IReadOnlyList<string> moved, IReadOnlyList<ExpectedDiagnostic> expected) = DiagnosticMarkup.Parse("// a comment\npublic class {|PROBE001:ProbeWarn|};");

        GeneratorTestResult second = IncrementalTestHelper.RunAgainWithEdit(first, 0, moved[0]);

        await second.VerifyDiagnosticsAsync(expected, idPrefix: "PROBE");
    }

    [Test]
    public async Task DiagnosticInfo_InPipeline_HoldsNoLocationOrSymbol()
    {
        GeneratorTestResult result = SourceGeneratorTestHelper.RunGenerator<DiagnosticProbeGenerator>(
            "public class ProbeWarn; public class ProbeError;",
            new() { TrackSteps = true });

        await Assert.That(IncrementalTestHelper.FindForbiddenValues(result, DiagnosticProbeGenerator.Step)).IsEmpty();
    }

    [Test]
    [Arguments(DiagnosticSeverity.Error, true)]
    [Arguments(DiagnosticSeverity.Warning, false)]
    [Arguments(DiagnosticSeverity.Info, false)]
    public async Task Descriptors_Create_FollowsDescriptorRules(DiagnosticSeverity severity, bool notConfigurable)
    {
        DiagnosticDescriptor descriptor = Descriptors.Create("SGIOC999", "Title", "Message", severity);

        await Assert.That(descriptor.Category).IsEqualTo("SourceGen.Ioc");
        await Assert.That(descriptor.IsEnabledByDefault).IsTrue();
        await Assert.That(descriptor.DefaultSeverity).IsEqualTo(severity);
        await Assert.That(descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable)).IsEqualTo(notConfigurable);
    }

    [Test]
    public async Task GeneratedFile_HeaderAndAttribute_PassGeneratedCodeRules()
    {
        SourceWriter writer = new();
        GeneratedFile.WriteHeader(writer);
        writer.WriteLine();
        writer.WriteLine(GeneratedFile.GeneratedCodeAttribute);
        writer.WriteLine("internal static class Generated;");
        Compilation compilation = SourceGeneratorTestHelper.CreateCompilation(["public class User;", writer.ToString()]);

        IReadOnlyList<string> problems = GeneratedCodeRules.Check(compilation, [compilation.SyntaxTrees.Last()]);

        await Assert.That(problems).IsEmpty();
    }
}
