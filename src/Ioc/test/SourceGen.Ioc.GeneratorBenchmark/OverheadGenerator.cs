using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceGen.Ioc.GeneratorBenchmark;

/// <summary>
/// Subscribes to the same attribute, invocation and compilation inputs as <see cref="IocSourceGenerator"/> with empty transforms.
/// Its cost is Roslyn's share of a generator run: finding attributed nodes, binding their symbols and running the driver.
/// </summary>
// Passed directly to the driver and never loaded as a compiler extension, so it needs no [Generator] registration.
internal sealed class OverheadGenerator : IIncrementalGenerator
{
    private static readonly string[] AttributeNames =
    [
        "SourceGen.Ioc.IocRegisterAttribute",
        "SourceGen.Ioc.IocRegisterAttribute`1",
        "SourceGen.Ioc.IocRegisterForAttribute",
        "SourceGen.Ioc.IocRegisterForAttribute`1",
        "SourceGen.Ioc.IocRegisterDefaultsAttribute",
        "SourceGen.Ioc.IocRegisterDefaultsAttribute`1",
        "SourceGen.Ioc.IocImportModuleAttribute",
        "SourceGen.Ioc.IocImportModuleAttribute`1",
        "SourceGen.Ioc.IocDiscoverAttribute",
        "SourceGen.Ioc.IocDiscoverAttribute`1",
        "SourceGen.Ioc.IocContainerAttribute",
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        foreach(var name in AttributeNames)
        {
            var provider = context.SyntaxProvider.ForAttributeWithMetadataName(name, static (_, _) => true, static (_, _) => 0).Collect();
            context.RegisterSourceOutput(provider, static (_, _) => { });
        }

        var invocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "GetService" or "GetRequiredService" or "GetKeyedService" or "GetRequiredKeyedService" or "GetServices" or "GetKeyedServices"
                }
            },
            static (_, _) => 0).Collect();
        context.RegisterSourceOutput(invocations.Combine(context.CompilationProvider.Select(static (_, _) => 0)), static (_, _) => { });
    }
}
