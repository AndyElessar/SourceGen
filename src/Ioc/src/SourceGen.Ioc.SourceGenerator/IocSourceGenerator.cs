using SourceGen.Ioc.Analysis;
using SourceGen.Ioc.Diagnostics;
using SourceGen.Ioc.Emit;
using SourceGen.Ioc.Model;
using SourceGen.Ioc.Parse;

namespace SourceGen.Ioc;

/// <summary>
/// Generates the IoC container, the module output, and the service collection extension.
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

        // The definite check of the assembly view (Analysis.Model.spec, "Two levels of checks")
        IncrementalValueProvider<ImmutableEquatableArray<GraphProblem>> definiteProblems = definitions
            .Select(static (d, ct) => DependencyGraph.DefiniteCheck(d, ct));

        context.RegisterSourceOutput(
            results.Combine(definiteProblems).Combine(context.CompilationProvider),
            static (spc, input) =>
            {
                ImmutableEquatableArray<DeclarationResult> declared = input.Left.Left;
                Dictionary<(NamedTypeRef, int), ParameterSite> sites = ParameterSites(declared);
                IEnumerable<DiagnosticInfo> diagnostics = declared.SelectMany(static r => r.Diagnostics)
                    .Concat(Definitions.CheckMerge(declared.SelectMany(static r => r.Sites)))
                    .Concat(DefiniteDiagnostics(input.Left.Right, sites));
                DiagnosticReporter.Report(spc, input.Right, diagnostics);
            });

        IncrementalValueProvider<(ServiceCollectionExtensionSettings? Settings, DiagnosticInfo? Diagnostic)> settings = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ServiceCollectionExtensionReader.Attribute,
                static (node, _) => node is CompilationUnitSyntax,
                static (ctx, _) => ServiceCollectionExtensionReader.ReadAttribute(ctx))
            .Collect()
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? string.Empty))
            .Combine(context.AnalyzerConfigOptionsProvider.Select(static (o, _) =>
                o.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? value) ? value : null))
            .Select(static (input, _) => input.Left.Left.IsEmpty
                ? (null, null)
                : ServiceCollectionExtensionReader.Decide(input.Left.Left[0], input.Left.Right, input.Right));

        context.RegisterSourceOutput(
            settings.Combine(context.CompilationProvider),
            static (spc, input) =>
            {
                if(input.Left.Diagnostic is { } diagnostic)
                {
                    DiagnosticReporter.Report(spc, input.Right, [diagnostic]);
                }
            });

        // An Error of the definite check stops the service collection extension (ServiceCollection.Output.spec, "Checks")
        IncrementalValueProvider<bool> definiteErrors = definiteProblems
            .Select(static (problems, _) => problems.Any(static p => !ContainerAnalysis.IsWarning(p.Id)));

        context.RegisterSourceOutput(
            settings.Select(static (s, _) => s.Settings).Combine(definitions).Combine(definiteErrors),
            static (spc, input) =>
            {
                if(input.Left.Left is { } extensionSettings && !input.Right)
                {
                    ServiceCollectionExtensionModel model = new(extensionSettings, input.Left.Right);
                    spc.AddSource(ServiceCollectionExtensionEmitter.HintName(extensionSettings), ServiceCollectionExtensionEmitter.Generate(model));
                }
            });

        InitializeContainers(context, results, declarations, definitions, definiteProblems);
    }

    /// <summary>The site of each constructor parameter. A parameter declared in source wins over the attribute of a declaration.</summary>
    private static Dictionary<(NamedTypeRef, int), ParameterSite> ParameterSites(ImmutableEquatableArray<DeclarationResult> results)
    {
        Dictionary<(NamedTypeRef, int), ParameterSite> sites = [];
        foreach(ParameterSite site in results.SelectMany(static r => r.ParameterSites)
            .OrderBy(static s => s.InSource ? 0 : 1)
            .ThenBy(static s => s.FilePath, StringComparer.Ordinal)
            .ThenBy(static s => s.Span.Start))
        {
            if(!sites.ContainsKey((site.Implementation, site.Index)))
            {
                sites[(site.Implementation, site.Index)] = site;
            }
        }

        return sites;
    }

    /// <summary>
    /// The problems of the definite check at their dependency sites. A dependency site from metadata is reported at the
    /// registration attribute (Analysis.Model.spec, "Two levels of checks").
    /// </summary>
    private static IEnumerable<DiagnosticInfo> DefiniteDiagnostics(ImmutableEquatableArray<GraphProblem> problems, Dictionary<(NamedTypeRef, int), ParameterSite> sites)
        => Reported(problems, sites).Select(static r => new DiagnosticInfo(r.Problem.Id, r.Problem.Arguments, r.Site.FilePath, r.Site.Span));

    private static IEnumerable<(GraphProblem Problem, ParameterSite Site)> Reported(ImmutableEquatableArray<GraphProblem> problems, Dictionary<(NamedTypeRef, int), ParameterSite> sites)
    {
        foreach(GraphProblem problem in problems)
        {
            if(sites.TryGetValue((problem.Implementation, problem.Parameter), out ParameterSite? site))
            {
                yield return (problem, site);
            }
        }
    }

    private static void InitializeContainers(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableEquatableArray<DeclarationResult>> results,
        IncrementalValueProvider<ImmutableEquatableArray<RegistrationDeclaration>> declarations,
        IncrementalValueProvider<ImmutableEquatableArray<ClosedDefinition>> definitions,
        IncrementalValueProvider<ImmutableEquatableArray<GraphProblem>> definiteProblems)
    {
        IncrementalValuesProvider<ContainerResult> containers = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ContainerReader.Attribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => ContainerReader.Read(ctx, ct));

        // The skipped instance definitions: a dependency on one of them, by the implementation type or by a known
        // service type, is not a missing dependency
        IncrementalValueProvider<ImmutableEquatableArray<TypeRef>> errorNodes = declarations
            .Combine(definitions)
            .Select(static (input, _) =>
            {
                HashSet<NamedTypeRef> generated = [.. input.Right.Select(static d => d.Implementation)];
                return input.Left
                    .Where(d => !generated.Contains(d.Implementation))
                    .SelectMany(static d => d.ServiceTypes.Prepend(d.Implementation))
                    .Distinct()
                    .OrderBy(static t => t.CanonicalString, StringComparer.Ordinal)
                    .ToImmutableEquatableArray();
            });

        // The fallback provider (IntegrateServiceProvider), ExplicitOnly, and IncludeTags come in later slices.
        // Until then only a container without them is generated.
        IncrementalValuesProvider<ContainerAnalysisResult> analyses = containers
            .Select(static (r, _) => r.Container)
            .Where(static c => c is { IntegrateServiceProvider: false, ExplicitOnly: false, HasIncludeTags: false })
            .Combine(definitions.Combine(errorNodes))
            .WithTrackingName(TrackingNames.Views)
            .Select(static (input, ct) => ContainerAnalysis.Analyze(input.Left!, input.Right.Left, input.Right.Right, ct))
            .WithTrackingName(TrackingNames.Analysis);

        context.RegisterSourceOutput(analyses, static (spc, analysis) =>
        {
            if(analysis.Plan is { } plan)
            {
                spc.AddSource(plan.Container.HintName, ContainerEmitter.Generate(plan));
            }
        });

        // The full check reports on the identifier of the container class. The positions come from the sites, so a
        // move of code does not run the analysis again.
        context.RegisterSourceOutput(
            containers.Collect().Combine(analyses.Collect()).Combine(results.Combine(definiteProblems)).Combine(context.CompilationProvider),
            static (spc, input) =>
            {
                ImmutableArray<ContainerResult> containerResults = input.Left.Left.Left;
                Dictionary<(NamedTypeRef, int), ParameterSite> parameterSites = ParameterSites(input.Left.Right.Left);
                HashSet<(string, NamedTypeRef, int, ImmutableEquatableArray<string>)> reportedByDefiniteCheck = [.. Reported(input.Left.Right.Right, parameterSites)
                    .Select(static r => (r.Problem.Id, r.Problem.Implementation, r.Problem.Parameter, r.Problem.Arguments))];
                List<DiagnosticInfo> diagnostics = [.. containerResults.Select(static r => r.Diagnostic).OfType<DiagnosticInfo>()];
                Dictionary<NamedTypeRef, ContainerSite> sites = [];
                foreach(ContainerSite site in containerResults.Select(static r => r.Site).OfType<ContainerSite>())
                {
                    sites[site.Type] = site;
                }

                foreach(ContainerAnalysisResult analysis in input.Left.Left.Right)
                {
                    if(!sites.TryGetValue(analysis.Type, out ContainerSite? site))
                    {
                        continue;
                    }

                    // The same diagnostic with the same message at the same edge, which the definite check reports, is not reported again
                    diagnostics.AddRange(analysis.Problems
                        .Where(p => p.Implementation is null || !reportedByDefiniteCheck.Contains((p.Id, p.Implementation, p.Parameter, p.Arguments)))
                        .Select(p => new DiagnosticInfo(p.Id, p.Arguments, site.FilePath, site.Span)));
                }

                DiagnosticReporter.Report(spc, input.Right, diagnostics);
            });
    }
}
