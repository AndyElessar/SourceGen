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
        DependencyIndex dependencyIndex,
        CancellationToken cancellationToken)
    {
        // The diagnostic does not depend on the consumer's lifetime, so it is reported once, at the first registration that
        // calls the constructor or injects the members
        if(serviceInfo.Constructor is { } constructor)
        {
            var location = serviceInfo.GetFirstActivationLocation(injected: false);
            foreach(var parameter in constructor.Parameters)
            {
                Analyze(parameter.Type, parameter, null, location);
            }
        }

        if(!serviceInfo.InjectedMembers.IsEmpty)
        {
            var location = serviceInfo.GetFirstActivationLocation(injected: true);
            foreach(var (member, injectAttribute) in serviceInfo.InjectedMembers)
            {
                switch(member)
                {
                    case IPropertySymbol property:
                        Analyze(property.Type, null, injectAttribute, location);
                        break;
                    case IFieldSymbol field:
                        Analyze(field.Type, null, injectAttribute, location);
                        break;
                    case IMethodSymbol method:
                        foreach(var parameter in method.Parameters)
                        {
                            Analyze(parameter.Type, parameter, null, location);
                        }
                        break;
                }
            }
        }

        void Analyze(ITypeSymbol type, IParameterSymbol? parameter, AttributeData? injectAttribute, Location? location)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!TryGetFuncWithArgumentsReturnType(type, out var funcType, out var returnType))
                return;

            // TReturn is resolved with the service key of the parameter or member, to one registration per tag group; each
            // lifetime among them is reported once
            var key = parameter is not null ? parameter.GetServiceKeyAndAttributeInfo().ServiceKey : injectAttribute?.GetKeyInfo().Key;
            var reportedSingleton = false;
            var reportedScoped = false;
            foreach(var dependency in dependencyIndex.Find(returnType, key))
            {
                switch(dependency.Lifetime)
                {
                    case ServiceLifetime.Singleton when !reportedSingleton:
                        reportedSingleton = true;
                        break;
                    case ServiceLifetime.Scoped when !reportedScoped:
                        reportedScoped = true;
                        break;
                    default:
                        continue;
                }

                reportDiagnostic(Diagnostic.Create(
                    FuncWithArgumentsIgnoresLifetime,
                    location,
                    serviceInfo.Type.Name,
                    funcType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    returnType.Name,
                    dependency.Lifetime));
            }
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
