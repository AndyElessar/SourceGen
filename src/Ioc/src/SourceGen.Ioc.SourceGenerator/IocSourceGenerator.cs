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
        IncrementalValuesProvider<ImmutableEquatableArray<RegistrationDeclaration>> register = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclarationReader.RegisterAttribute,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, ct) => DeclarationReader.ReadRegister(ctx, ct))
            .WithTrackingName(TrackingNames.Declarations);

        IncrementalValuesProvider<ImmutableEquatableArray<RegistrationDeclaration>> registerFor = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclarationReader.RegisterForAttribute,
                static (node, _) => node is CompilationUnitSyntax,
                static (ctx, ct) => DeclarationReader.ReadRegisterFor(ctx, ct))
            .WithTrackingName(TrackingNames.Declarations);

        IncrementalValueProvider<ImmutableEquatableArray<ClosedDefinition>> definitions = register.Collect()
            .Combine(registerFor.Collect())
            .Select(static (pair, ct) => Definitions.Merge(pair.Left.Concat(pair.Right).SelectMany(static d => d), ct))
            .WithTrackingName(TrackingNames.Analysis);

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
