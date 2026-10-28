namespace SourceGen.Ioc;

/// <summary>
/// Partial class for SGIOC033: Func factories with arguments ignore the lifetime of the service they create.
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// Reports constructor parameters and injected members of type <c>Func&lt;T1, ..., TReturn&gt;</c> whose
    /// <c>TReturn</c> is registered as Singleton or Scoped: each call creates a new instance from the arguments,
    /// so the registered lifetime does not apply.
    /// </summary>
    private static void AnalyzeFuncWithArgumentsLifetime(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        AnalyzerContext analyzerContext,
        CancellationToken cancellationToken)
    {
        if(serviceInfo.Constructor is { } constructor)
        {
            foreach(var parameter in constructor.Parameters)
            {
                Analyze(parameter.Type);
            }
        }

        foreach(var (member, _) in serviceInfo.InjectedMembers)
        {
            switch(member)
            {
                case IPropertySymbol property:
                    Analyze(property.Type);
                    break;
                case IFieldSymbol field:
                    Analyze(field.Type);
                    break;
                case IMethodSymbol method:
                    foreach(var parameter in method.Parameters)
                    {
                        Analyze(parameter.Type);
                    }
                    break;
            }
        }

        void Analyze(ITypeSymbol type)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!TryGetFuncWithArgumentsReturnType(type, out var funcType, out var returnType))
                return;

            if(FindRegisteredDependency(analyzerContext, returnType) is not { Lifetime: ServiceLifetime.Singleton or ServiceLifetime.Scoped } dependency)
                return;

            reportDiagnostic(Diagnostic.Create(
                FuncWithArgumentsIgnoresLifetime,
                serviceInfo.Location,
                serviceInfo.Type.Name,
                funcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                returnType.Name,
                dependency.Lifetime));
        }
    }

    /// <summary>
    /// Gets whether <paramref name="type"/> is <c>System.Func&lt;T1, ..., TReturn&gt;</c> with at least one input parameter.
    /// </summary>
    private static bool TryGetFuncWithArgumentsReturnType(
        ITypeSymbol type,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? funcType,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INamedTypeSymbol? returnType)
    {
        funcType = null;
        returnType = null;

        if(type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 1 } namedType)
            return false;

        var originalDefinition = namedType.OriginalDefinition;
        if(originalDefinition.ContainingNamespace is not { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            || !originalDefinition.MetadataName.StartsWith("Func`", StringComparison.Ordinal)
            || namedType.TypeArguments[^1] is not INamedTypeSymbol funcReturnType)
        {
            return false;
        }

        funcType = namedType;
        returnType = funcReturnType;
        return true;
    }
}
