using SourceGen.Ioc.Analysis;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;
using SourceGen.Ioc.Parse;

namespace SourceGen.Ioc;

/// <summary>
/// Generates the IoC container, the module output, and the <c>Add{Name}</c> method.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class IocSourceGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<DeclarationResult> register = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclarationReader.RegisterAttribute,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, ct) => DeclarationReader.ReadRegister(ctx, ct));

        IncrementalValuesProvider<DeclarationResult> registerFor = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclarationReader.RegisterForAttribute,
                static (node, _) => node is CompilationUnitSyntax or ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, ct) => DeclarationReader.ReadRegisterFor(ctx, ct));

        // The generation pipeline takes only the declarations. The sites and the diagnostics carry positions, and a
        // change of position alone must not run the analysis again (Diagnostics.spec, "Two pipelines").
        IncrementalValueProvider<ImmutableEquatableArray<DeclarationResult>> results = register.Collect()
            .Combine(registerFor.Collect())
            .Select(static (pair, _) => pair.Left.Concat(pair.Right).ToImmutableEquatableArray());

        IncrementalValueProvider<ImmutableEquatableArray<RegistrationDeclaration>> declarations = register
            .Select(static (r, _) => r.Declarations)
            .WithTrackingName(TrackingNames.Declarations)
            .Collect()
            .Combine(registerFor.Select(static (r, _) => r.Declarations).WithTrackingName(TrackingNames.Declarations).Collect())
            .Select(static (pair, _) => pair.Left.Concat(pair.Right).SelectMany(static d => d).ToImmutableEquatableArray());

        IncrementalValueProvider<ImmutableEquatableArray<ClosedDefinition>> definitions = declarations
            .Select(static (d, ct) => Definitions.Merge(d, ct))
            .WithTrackingName(TrackingNames.Analysis);

        context.RegisterSourceOutput(
            results.Combine(context.CompilationProvider),
            static (spc, input) =>
            {
                IEnumerable<DiagnosticInfo> diagnostics = input.Left.SelectMany(static r => r.Diagnostics)
                    .Concat(Definitions.CheckMerge(input.Left.SelectMany(static r => r.Sites)));
                DiagnosticReporter.Report(spc, input.Right, diagnostics);
            });

        IncrementalValueProvider<(AddMethodSettings? Settings, DiagnosticInfo? Diagnostic)> settings = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AddMethodReader.Attribute,
                static (node, _) => node is CompilationUnitSyntax,
                static (ctx, _) => AddMethodReader.ReadAttribute(ctx))
            .Collect()
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? string.Empty))
            .Combine(context.AnalyzerConfigOptionsProvider.Select(static (o, _) =>
                o.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? value) ? value : null))
            .Select(static (input, _) => input.Left.Left.IsEmpty
                ? (null, null)
                : AddMethodReader.Decide(input.Left.Left[0], input.Left.Right, input.Right));

        context.RegisterSourceOutput(
            settings.Combine(context.CompilationProvider),
            static (spc, input) =>
            {
                if(input.Left.Diagnostic is { } diagnostic)
                {
                    DiagnosticReporter.Report(spc, input.Right, [diagnostic]);
                }
            });

        context.RegisterSourceOutput(
            settings.Select(static (s, _) => s.Settings).Combine(definitions),
            static (spc, input) =>
            {
                if(input.Left is { } addSettings)
                {
                    AddMethodModel model = new(addSettings, input.Right);
                    spc.AddSource(AddMethodEmitter.HintName(addSettings), AddMethodEmitter.Generate(model));
                }
            });
    }
}
