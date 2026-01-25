using Microsoft.CodeAnalysis.Diagnostics;

namespace SourceGen.Ioc;

/// <summary>
/// Partial class for ServiceKey analysis (SGIOC013-014).
/// </summary>
public sealed partial class RegisterAnalyzer
{
    /// <summary>
    /// SGIOC013 and SGIOC014: Analyzes [ServiceKey] parameter usage.
    /// SGIOC013: Reports error when the parameter type does not match the registered key type.
    /// SGIOC014: Reports error when [ServiceKey] is used but no Key is registered.
    /// </summary>
    private static void AnalyzeServiceKeyTypeMismatch(
        Action<Diagnostic> reportDiagnostic,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken)
    {
        var keyTypeSymbol = serviceInfo.KeyTypeSymbol;
        var hasKey = serviceInfo.HasKey;

        // Check constructor parameters (using cached constructor)
        var constructor = serviceInfo.Constructor;
        if (constructor is not null)
        {
            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, constructor.Parameters, keyTypeSymbol, hasKey, cancellationToken);
        }

        // Check [Inject] method parameters (using cached injected members)
        foreach (var (member, _) in serviceInfo.InjectedMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
                continue;

            AnalyzeServiceKeyParametersInMethod(reportDiagnostic, method.Parameters, keyTypeSymbol, hasKey, cancellationToken);
        }
    }

    /// <summary>
    /// Analyzes parameters for [ServiceKey] attribute and reports diagnostics.
    /// SGIOC013: Reports when parameter type does not match registered key type.
    /// SGIOC014: Reports when [ServiceKey] is used but no Key is registered.
    /// </summary>
    private static void AnalyzeServiceKeyParametersInMethod(
        Action<Diagnostic> reportDiagnostic,
        ImmutableArray<IParameterSymbol> parameters,
        ITypeSymbol? keyTypeSymbol,
        bool hasKey,
        CancellationToken cancellationToken)
    {
        foreach (var param in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if parameter has [ServiceKey] attribute
            var serviceKeyAttribute = param.GetAttributes()
                .FirstOrDefault(attr =>
                    attr.AttributeClass?.Name == "ServiceKeyAttribute"
                    && attr.AttributeClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection");

            if (serviceKeyAttribute is null)
                continue;

            var location = serviceKeyAttribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation()
                ?? param.Locations.FirstOrDefault();

            // SGIOC014: No key is registered but [ServiceKey] is used
            if (!hasKey)
            {
                reportDiagnostic(Diagnostic.Create(
                    ServiceKeyNotRegistered,
                    location,
                    param.Name));
                continue;
            }

            // Skip type checking if KeyType is Csharp (keyTypeSymbol will be null)
            if (keyTypeSymbol is null)
                continue;

            var paramType = param.Type;

            // SGIOC013: Check if the parameter type is compatible with the key type
            // The parameter type should be the same as or assignable from the key type
            if (!IsAssignable(paramType, keyTypeSymbol))
            {
                reportDiagnostic(Diagnostic.Create(
                    ServiceKeyTypeMismatch,
                    location,
                    param.Name,
                    paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    keyTypeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }
}
